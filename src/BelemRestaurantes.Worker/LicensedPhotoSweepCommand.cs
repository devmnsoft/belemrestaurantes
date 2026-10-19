using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Worker;

public static class LicensedPhotoSweepCommand
{
    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>();
        await using var db = await factory.OpenAsync(default);
        var tiers = (await db.QueryAsync("SELECT status, catalog_tier AS tier, COUNT(*) AS total FROM restaurants GROUP BY status, catalog_tier ORDER BY status, catalog_tier")).ToList();
        Console.WriteLine("Catálogo local:");
        foreach (var row in tiers) Console.WriteLine($"  {row.status} / {row.tier}: {row.total}");
        var enrichment = scope.ServiceProvider.GetRequiredService<IEnrichmentService>();
        var queued = await enrichment.EnqueueLicensedPhotoSweepAsync(15, default);
        var pendingPhotos = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurant_enrichment_queue WHERE fields_to_enrich='photos' AND state='pending' AND next_attempt_at<=UTC_TIMESTAMP(6)");
        var processed = pendingPhotos == 0 ? 0 : await enrichment.RunEnrichmentCycleAsync(Math.Min(pendingPhotos, 15), default);
        var applied = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurants WHERE photo_license IN ('cc0','cc-by','cc-by-sa','pd')");
        var pending = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM restaurant_media WHERE status='pending' AND license_type IN ('cc0','cc-by','cc-by-sa','pd','site_a_conferir')");
        var looked = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM licensed_photo_lookups");
        Console.WriteLine($"Fotos: {queued} enfileiradas, {processed} processadas, {applied} aplicadas na ficha, {pending} ainda pendentes, {looked} consultas registradas.");
    }
}
