using System.Net;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Photos;

public sealed class WikimediaLicensedPhotoFinder(IHttpClientFactory httpClientFactory, ILogger<WikimediaLicensedPhotoFinder> logger) : ILicensedPhotoFinder
{
    public async Task<LicensedPhotoLookup> FindAsync(string restaurantName, string city, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(restaurantName) || string.IsNullOrWhiteSpace(city)) return LicensedPhotoLookup.Empty;
        var search = $"intitle:\"{restaurantName.Trim()}\" {city.Trim()}";
        var path = "/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=6&prop=imageinfo&iiprop=url|mime|size|extmetadata&iiurlwidth=960&gsrsearch="
            + Uri.EscapeDataString(search);
        try
        {
            var client = httpClientFactory.CreateClient("wikimedia");
            using var response = await client.GetAsync(path, ct);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                return LicensedPhotoLookup.Failed("Wikimedia pediu espera.");
            if (!response.IsSuccessStatusCode)
                return LicensedPhotoLookup.Failed($"Wikimedia respondeu {(int)response.StatusCode}.");
            var json = await response.Content.ReadAsStringAsync(ct);
            if (json.Length > 1_500_000) return LicensedPhotoLookup.Failed("Resposta do Wikimedia grande demais.");
            return new LicensedPhotoLookup(true, null, WikimediaPhotoParser.Read(json, restaurantName, city));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Consulta de foto licenciada indisponível para {City}.", city);
            return LicensedPhotoLookup.Failed("Falha de rede ao consultar o Wikimedia.");
        }
    }
}
