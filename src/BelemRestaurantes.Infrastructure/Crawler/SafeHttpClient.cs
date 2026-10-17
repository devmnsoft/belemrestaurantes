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
/// Valida cada IP resolvido antes de estabelecer conexão e impede conexões para redes privadas,
/// loopback, metadados de provedores de nuvem ou portas não padrão de navegação web.
/// </summary>
public sealed class SafeHttpClient : ISafeHttpClient
{
    private const int MaxResponseBytes = 2 * 1024 * 1024; // 2 MB máximo para HTML / XML
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
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
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
            // Validação preventiva de DNS para o hostname
            await ValidateDnsSecurityAsync(uri.Host, ct);

            // Criação do socket handler com checagem SSRF em nível de conexão
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

                    // Validação de todos os IPs retornados (proteção contra DNS Rebinding)
                    foreach (var ip in addresses)
                    {
                        if (IsDisallowedIp(ip))
                        {
                            throw new SecurityException($"Bloqueio SSRF: O endereço IP {ip} associado ao host {host} é restrito.");
                        }
                    }

                    var targetIp = addresses.First(ip => !IsDisallowedIp(ip));
                    var socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(targetIp, context.DnsEndPoint.Port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))
            };

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CuiaCrawler/1.0 (+https://belemrestaurantes.com.br/crawler-info; comercial@belemrestaurantes.com.br)");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml,text/xml,application/rss+xml,application/json;q=0.9,*/*;q=0.5");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.StartsWith("\"") ? etag : $"\"{etag}\""));
            }

            if (!string.IsNullOrWhiteSpace(lastModified) && DateTimeOffset.TryParse(lastModified, out var dt))
            {
                request.Headers.IfModifiedSince = dt;
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new SafeDownloadResult(true, (int)response.StatusCode, null, null, etag, lastModified, null, NotModified: true);
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

            var textContent = Encoding.UTF8.GetString(memoryStream.ToArray());
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
        catch (SecurityException secEx)
        {
            _logger.LogWarning("Tentativa de acesso bloqueada por segurança SSRF: {Url}. Erro: {Msg}", url, secEx.Message);
            return new SafeDownloadResult(false, 0, null, null, null, null, $"Segurança SSRF: {secEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha na requisição para {Url}", url);
            return new SafeDownloadResult(false, 0, null, null, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Verifica se o IP pertence a redes restritas ou não roteáveis na internet pública.
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

            // 10.0.0.0/8 (Privada RFC 1918)
            if (bytes[0] == 10) return true;

            // 172.16.0.0/12 (Privada RFC 1918)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.168.0.0/16 (Privada RFC 1918)
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 127.0.0.0/8 (Loopback)
            if (bytes[0] == 127) return true;

            // 169.254.0.0/16 (Link-Local e Cloud Metadata: AWS/GCP/Azure 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 100.64.0.0/10 (Carrier-Grade NAT RFC 6598)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) return true;

            // 0.0.0.0/8 (Rede atual)
            if (bytes[0] == 0) return true;

            // 224.0.0.0/4 (Multicast) e 240.0.0.0/4 (Reservado)
            if (bytes[0] >= 224) return true;
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

        if (addresses.All(IsDisallowedIp))
        {
            throw new SecurityException($"Todos os endereços IP resolvidos para '{host}' são restritos.");
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
