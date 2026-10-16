using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.Infrastructure.Ai;

/// <summary>Configuração comum de um provedor de IA da Cuia.</summary>
public sealed class AiProviderOptions
{
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>Preço por mil tokens, em micros de USD. Vem do orçamento aprovado.</summary>
    public long InputMicrosPerThousand { get; set; }
    public long OutputMicrosPerThousand { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>
/// Base para provedores com API compatível com OpenAI (Groq e DeepSeek).
/// A diferença entre eles fica em BaseUrl, modelo e preço — o formato do
/// corpo e da resposta é o mesmo, então não há duplicação de parser.
/// </summary>
public abstract class OpenAiCompatibleProvider(
    HttpClient httpClient,
    AiProviderOptions options) : IAiProvider
{
    public abstract AiProvider Provider { get; }

    public bool IsConfigured => options.IsConfigured;

    public TokenPrice Price => new(options.InputMicrosPerThousand, options.OutputMicrosPerThousand);

    public async Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException($"Provedor {Provider} não configurado.");
        }

        EndpointGuard.EnsureHttps(options.BaseUrl, $"base URL de {Provider}");

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl), "chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        message.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = options.Model,
                temperature = request.Temperature,
                max_tokens = request.MaxOutputTokens,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = request.SystemInstruction },
                    new { role = "user", content = request.UserPayload }
                }
            }),
            Encoding.UTF8,
            "application/json");

        var startedAt = System.Diagnostics.Stopwatch.StartNew();

        using var response = await httpClient.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        startedAt.Stop();

        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(Provider, (int)response.StatusCode, Truncate(body, 400));
        }

        return ParseResponse(body, startedAt.Elapsed);
    }

    private AiCompletionResponse ParseResponse(string body, TimeSpan latency)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return Invalid(latency);
        }

        var content = choices[0]
            .TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString()
                : null;

        var usage = document.RootElement.TryGetProperty("usage", out var u) ? u : default;

        var inputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("prompt_tokens", out var p)
            ? p.GetInt32()
            : 0;

        var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("completion_tokens", out var c)
            ? c.GetInt32()
            : 0;

        return new AiCompletionResponse(
            Provider,
            content ?? string.Empty,
            inputTokens,
            outputTokens,
            0,
            latency,
            IsValid: !string.IsNullOrWhiteSpace(content));
    }

    private AiCompletionResponse Invalid(TimeSpan latency) =>
        new(Provider, string.Empty, 0, 0, 0, latency, IsValid: false);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

/// <summary>Provedor Groq.</summary>
public sealed class GroqProvider(HttpClient httpClient, AiProviderOptions options)
    : OpenAiCompatibleProvider(httpClient, options)
{
    public override AiProvider Provider => AiProvider.Groq;
}

/// <summary>Provedor DeepSeek.</summary>
public sealed class DeepSeekProvider(HttpClient httpClient, AiProviderOptions options)
    : OpenAiCompatibleProvider(httpClient, options)
{
    public override AiProvider Provider => AiProvider.DeepSeek;
}

/// <summary>
/// Gemini. Formato diferente (generateContent), por isso implementação própria
/// em vez de herdar do caminho compatível com OpenAI.
/// </summary>
public sealed class GeminiProvider(
    HttpClient httpClient,
    AiProviderOptions options) : IAiProvider
{
    public AiProvider Provider => AiProvider.Gemini;

    public bool IsConfigured => options.IsConfigured;

    public TokenPrice Price => new(options.InputMicrosPerThousand, options.OutputMicrosPerThousand);

    public async Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Gemini não configurado.");
        }

        EndpointGuard.EnsureHttps(options.BaseUrl, "base URL do Gemini");

        // A chave viaja na URL porque é assim que a API do Gemini autentica.
        // Por isso a URL nunca é logada em nível de debug.
        var url = new Uri(
            $"{options.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(options.Model)}:generateContent");

        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Add("x-goog-api-key", options.ApiKey);

        message.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = request.UserPayload } } } },
                systemInstruction = new { parts = new[] { new { text = request.SystemInstruction } } },
                generationConfig = new
                {
                    temperature = request.Temperature,
                    maxOutputTokens = request.MaxOutputTokens,
                    responseMimeType = "application/json"
                }
            }),
            Encoding.UTF8,
            "application/json");

        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        startedAt.Stop();

        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(Provider, (int)response.StatusCode, Truncate(body, 400));
        }

        return ParseResponse(body, startedAt.Elapsed);
    }

    private AiCompletionResponse ParseResponse(string body, TimeSpan latency)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            return new AiCompletionResponse(Provider, string.Empty, 0, 0, 0, latency, IsValid: false);
        }

        var text = candidates[0]
            .TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts) &&
            parts.ValueKind == JsonValueKind.Array &&
            parts.GetArrayLength() > 0 &&
            parts[0].TryGetProperty("text", out var textElement)
                ? textElement.GetString()
                : null;

        var usage = document.RootElement.TryGetProperty("usageMetadata", out var u) ? u : default;

        var inputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("promptTokenCount", out var p)
            ? p.GetInt32()
            : 0;

        var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("candidatesTokenCount", out var c)
            ? c.GetInt32()
            : 0;

        return new AiCompletionResponse(
            Provider,
            text ?? string.Empty,
            inputTokens,
            outputTokens,
            0,
            latency,
            IsValid: !string.IsNullOrWhiteSpace(text));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public sealed class AiProviderException(AiProvider provider, int statusCode, string detail)
    : Exception($"Provedor {provider} respondeu {statusCode}: {detail}")
{
    public AiProvider Provider { get; } = provider;

    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Guarda contra SSRF na configuração de endpoint. Base URL vem de
/// configuração/segredo, mas um valor errado não deve permitir que o processo
/// faça requisição para rede interna.
/// </summary>
public static class EndpointGuard
{
    private static readonly string[] BlockedHostFragments =
    [
        "localhost", "127.0.0.1", "0.0.0.0", "::1", "169.254.169.254", "metadata.google.internal"
    ];

    public static void EnsureHttps(string url, string description)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{description} inválida: '{url}'.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{description} deve usar HTTPS. Recebido: {uri.Scheme}.");
        }

        var host = uri.Host.ToLowerInvariant();

        if (BlockedHostFragments.Any(fragment => host.Contains(fragment, StringComparison.Ordinal)) ||
            host.StartsWith("10.", StringComparison.Ordinal) ||
            host.StartsWith("192.168.", StringComparison.Ordinal) ||
            (host.StartsWith("172.", StringComparison.Ordinal) &&
             host.Length > 4 &&
             int.TryParse(host[4..].Split('.')[0], out var octet) &&
             octet is >= 16 and <= 31))
        {
            throw new InvalidOperationException($"{description} aponta para endereço interno: '{host}'.");
        }
    }
}