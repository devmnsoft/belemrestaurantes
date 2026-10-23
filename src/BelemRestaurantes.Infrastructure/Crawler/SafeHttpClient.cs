using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Crawler;

/// <summary>
/// Implementa cliente HTTP seguro com proteção rigorosa contra SSRF e DNS Rebinding.
/// Rejeita domínios com QUALQUER resolução de IP proibida, valida redirects manualmente
/// e impede conexões para redes privadas, loopback, metadados de provedores de nuvem ou portas não padrão.
/// </summary>
public sealed class SafeHttpClient : ISafeHttpClient
{
    private const int MaxResponseBytes = 2 * 1024 * 1024; // 2 MB máximo para HTML / XML
    private const int MaxRedirects = 3;
    private readonly ILogger<SafeHttpClient> _logger;

    public SafeHttpClient(ILogger<SafeHttpClient> logger)
    {
        _logger = logger;
    }

    public async Task<SafeDownloadResult> DownloadAsync(
        string url,
        string? etag = null,
        string? lastModified = null,
        int timeoutSeconds = 15,
        CancellationToken ct = default)
    {
        var callerToken=ct;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds,1,30)));
        ct=deadline.Token;
        var currentUrl = url;
        var redirectCount = 0;

        while (redirectCount <= MaxRedirects)
        {
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri))
            {
                return new SafeDownloadResult(false, 0, null, null, null, null, "URL absoluta inválida");
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return new SafeDownloadResult(false, 0, null, null, null, null, $"Esquema de URL não permitido: {uri.Scheme}");
            }

            // Portas permitidas estritamente: 80 (HTTP) e 443 (HTTPS)
            if (uri.Port != 80 && uri.Port != 443)
            {
                return new SafeDownloadResult(false, 0, null, null, null, null, $"Porta não padrão rejeitada por segurança: {uri.Port}");
            }

            try
            {
                // 1. Validação preventiva de DNS para o hostname atual (incluindo redirects)
                await ValidateDnsSecurityAsync(uri.Host, ct);

                // 2. Socket handler com controle manual de redirect e checagem no handshake TCP
                using var handler = new SocketsHttpHandler
                {
                    ConnectCallback = async (context, cancellationToken) =>
                    {
                        var host = context.DnsEndPoint.Host;
                        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

                        if (addresses.Length == 0)
                        {
                            throw new SocketException((int)SocketError.HostNotFound);
                        }

                        // Rejeitar se QUALQUER endereço IP resolvido for restrito (mitiga DNS Rebinding multi-homed)
                        var badIp = addresses.FirstOrDefault(IsDisallowedIp);
                        if (badIp != null)
                        {
                            throw new SecurityException($"Bloqueio SSRF: O endereço IP {badIp} associado ao host '{host}' é restrito.");
                        }

                        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(ConnectionOrder(addresses), context.DnsEndPoint.Port, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    },
                    AllowAutoRedirect = false, // Controle manual de redirects para validação de segurança a cada salto
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                using var httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))
                };

                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CuiaCrawler/2.0 (+https://belemrestaurantes.com.br/crawler-info; comercial@belemrestaurantes.com.br)");
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml,text/xml,application/rss+xml,application/json;q=0.9,*/*;q=0.5");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);

                // ETag e If-Modified-Since somente para a URL original (não enviar em redirects)
                if (redirectCount == 0)
                {
                    if (!string.IsNullOrWhiteSpace(etag))
                    {
                        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.StartsWith("\"") ? etag : $"\"{etag}\""));
                    }

                    if (!string.IsNullOrWhiteSpace(lastModified) && DateTimeOffset.TryParse(lastModified, out var dt))
                    {
                        request.Headers.IfModifiedSince = dt;
                    }
                }

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new SafeDownloadResult(true, (int)response.StatusCode, null, null, etag, lastModified, null, NotModified: true);
                }

                // Tratar redirecionamento manualmente com validação de segurança no salto
                if (IsRedirect(response.StatusCode))
                {
                    redirectCount++;
                    var location = response.Headers.Location;
                    if (location == null)
                    {
                        return new SafeDownloadResult(false, (int)response.StatusCode, null, null, null, null, "Redirecionamento sem cabeçalho Location.");
                    }

                    var nextUri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    currentUrl = nextUri.ToString();
                    _logger.LogInformation("Seguindo redirecionamento {Step} para {NextUrl}", redirectCount, currentUrl);
                    continue;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;

                // Rejeição rigorosa de arquivos executáveis, binários ou mídias pesadas
                if (IsDisallowedMediaType(contentType))
                {
                    return new SafeDownloadResult(false, (int)response.StatusCode, null, contentType, null, null, $"Tipo de mídia rejeitado: {contentType}");
                }

                // Leitura com limitação de tamanho para proteção de memória (máximo 2 MB)
                await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
                using var memoryStream = new MemoryStream();
                var buffer = new byte[8192];
                int bytesRead;
                long totalRead = 0;

                while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    totalRead += bytesRead;
                    if (totalRead > MaxResponseBytes)
                    {
                        return new SafeDownloadResult(false, (int)response.StatusCode, null, contentType, null, null, $"Tamanho do conteúdo excedeu o limite máximo seguro de {MaxResponseBytes / 1024} KB");
                    }
                    memoryStream.Write(buffer, 0, bytesRead);
                }

                var textContent = CulinaryPageText.Decode(memoryStream.ToArray(), response.Content.Headers.ContentType?.CharSet);
                var resETag = response.Headers.ETag?.Tag;
                var resLastModified = response.Content.Headers.LastModified?.ToString("R");

                return new SafeDownloadResult(
                    response.IsSuccessStatusCode,
                    (int)response.StatusCode,
                    textContent,
                    contentType,
                    resETag,
                    resLastModified,
                    response.IsSuccessStatusCode ? null : $"HTTP {response.StatusCode}");
            }
            catch(OperationCanceledException)when(callerToken.IsCancellationRequested){throw;}
            catch(OperationCanceledException){return new(false,0,null,null,null,null,"Prazo de recuperação da fonte excedido.");}
            catch (SecurityException secEx)
            {
                _logger.LogWarning("Tentativa de acesso bloqueada por segurança SSRF: {Url}. Motivo: {Msg}", currentUrl, secEx.Message);
                return new SafeDownloadResult(false, 0, null, null, null, null, $"Segurança SSRF: {secEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Falha na requisição para {Url}: {Error}", currentUrl, ex.Message);
                return new SafeDownloadResult(false, 0, null, null, null, null, "Falha de conexão com o servidor de origem.");
            }
        }

        return new SafeDownloadResult(false, 0, null, null, null, null, "Limite máximo de 3 redirecionamentos excedido.");
    }

    // Keep all DNS security checks above; prefer IPv4 where an advertised IPv6 route may black-hole.
    public static IPAddress[] ConnectionOrder(IEnumerable<IPAddress> addresses)=>addresses.OrderBy(a=>a.AddressFamily==AddressFamily.InterNetwork?0:1).ToArray();
    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.Moved
             or HttpStatusCode.Redirect
             or HttpStatusCode.SeeOther
             or HttpStatusCode.TemporaryRedirect
             or (HttpStatusCode)308;

    /// <summary>
    /// Verifica se o IP pertence a redes restritas, loopback, privadas ou não roteáveis na internet pública.
    /// </summary>
    public static bool IsDisallowedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None))
        {
            return true;
        }

        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
        {
            return true;
        }

        // Mapear IPv4 embutido em IPv6
        var targetIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        if (targetIp.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = targetIp.GetAddressBytes();

            // 0.0.0.0/8 (Rede atual)
            if (bytes[0] == 0) return true;

            // 10.0.0.0/8 (Privada RFC 1918)
            if (bytes[0] == 10) return true;

            // 127.0.0.0/8 (Loopback)
            if (bytes[0] == 127) return true;

            // 100.64.0.0/10 (Carrier-Grade NAT RFC 6598)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) return true;

            // 169.254.0.0/16 (Link-Local e Cloud Metadata: AWS/GCP/Azure 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 172.16.0.0/12 (Privada RFC 1918)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.0.2.0/24 (TEST-NET-1 RFC 5737)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) return true;

            // 192.168.0.0/16 (Privada RFC 1918)
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 198.18.0.0/15 (Benchmark de rede RFC 2544)
            if (bytes[0] == 198 && (bytes[1] & 0xFE) == 18) return true;

            // 198.51.100.0/24 (TEST-NET-2 RFC 5737)
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return true;

            // 203.0.113.0/24 (TEST-NET-3 RFC 5737)
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return true;

            // 224.0.0.0/4 (Multicast) e 240.0.0.0/4 (Reservado)
            if (bytes[0] >= 224) return true;

            // 255.255.255.255 (Broadcast)
            if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255) return true;
        }
        else if (targetIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = targetIp.GetAddressBytes();

            // fc00::/7 (Unique Local Address)
            if ((bytes[0] & 0xFE) == 0xFC) return true;

            // 2001:db8::/32 (Documentação)
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return true;
        }

        return false;
    }

    private static async Task ValidateDnsSecurityAsync(string host, CancellationToken ct)
    {
        // Bloqueio preventivo de hostnames conhecidos de metadados de nuvem e redes internas
        var lowerHost = host.ToLowerInvariant();
        if (lowerHost == "localhost" ||
            lowerHost.EndsWith(".local") ||
            lowerHost.EndsWith(".internal") ||
            lowerHost == "metadata.google.internal" ||
            lowerHost == "instance-data")
        {
            throw new SecurityException($"O hostname '{host}' é reservado para infraestrutura interna.");
        }

        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        // Se QUALQUER endereço retornado pelo DNS for proibido, abortar imediatamente
        var restricted = addresses.FirstOrDefault(IsDisallowedIp);
        if (restricted != null)
        {
            throw new SecurityException($"O host '{host}' resolve para endereço IP restrito ({restricted}). Acesso proibido por política SSRF.");
        }
    }

    private static bool IsDisallowedMediaType(string mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return false;

        string[] disallowedPrefixes = [
            "application/x-msdownload",
            "application/octet-stream",
            "application/zip",
            "application/x-rar",
            "application/x-7z",
            "application/pdf",
            "application/vnd.",
            "video/",
            "audio/"
        ];

        return disallowedPrefixes.Any(p => mediaType.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
