using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "RestaurantModerator")]
[Route("admin/suggestions")]
public sealed class AdminSuggestionsController(MySqlConnectionFactory factory, IRestaurantIngestionRepository ingestion,
    IAuditWriter audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<SuggestionRow>(new CommandDefinition("""
            SELECT id,name,city,website,notes,target_restaurant_id AS TargetRestaurantId,submitted_at AS SubmittedAt
            FROM suggestions WHERE status='pending' ORDER BY submitted_at,id LIMIT 100
            """, cancellationToken: ct));
        return View(rows.AsList());
    }

    [HttpPost("{id:long}/accept")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var suggestion = await connection.QueryFirstOrDefaultAsync<SuggestionRow>(new CommandDefinition(
            "SELECT id,name,city,website,notes,target_restaurant_id AS TargetRestaurantId FROM suggestions WHERE id=@id AND status='pending' FOR UPDATE", new { id }, transaction, cancellationToken: ct));
        if (suggestion is null) return NotFound();
        if (BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(suggestion.Name).Length == 0 ||
            !BelemRestaurantes.Domain.Data.ParaOfficialCatalog.Municipalities.Any(c => c.NormalizedName == BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(suggestion.City)))
        {
            TempData["ErrorMessage"] = "Revise o nome e o município do Pará antes de encaminhar a indicação.";
            return RedirectToAction(nameof(Index));
        }
        if (suggestion.TargetRestaurantId is { } target)
        {
            var changed = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE restaurants SET name=@name,normalized_name=@normalizedName,city=@city,normalized_city=@normalizedCity,
                  website=COALESCE(@website,website),updated_at=UTC_TIMESTAMP(6) WHERE id=@target AND status='published'
                """, new { target, name = suggestion.Name.Trim(), normalizedName = BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(suggestion.Name),
                    city = suggestion.City, normalizedCity = BelemRestaurantes.Domain.Rules.NameNormalizer.Normalize(suggestion.City),
                    website = BelemRestaurantes.Domain.Rules.SocialLinkRules.TryValidate(suggestion.Website, out var validWebsite) ? validWebsite : null }, transaction, cancellationToken: ct));
            if (changed == 0) return NotFound();
            await connection.ExecuteAsync(new CommandDefinition("UPDATE suggestions SET status='approved' WHERE id=@id AND status='pending'", new { id }, transaction, cancellationToken: ct));
            await audit.WriteAsync("suggestion.correction.applied", "restaurant", target, System.Text.Json.JsonSerializer.Serialize(new { suggestionId = id }), ct);
            await transaction.CommitAsync(ct);
            TempData["Flash"] = $"Correção revisada e aplicada ao cadastro #{target}. Nenhum restaurante duplicado foi criado.";
            return RedirectToAction(nameof(Index));
        }
        var place = new DiscoveredPlace("suggestion:" + id, suggestion.Name, null, suggestion.City, "Restaurante", null,
            null, null, null, BelemRestaurantes.Domain.Rules.SocialLinkRules.TryValidate(suggestion.Website, out var safe) ? safe : null,
            null, null, ["restaurant"]);
        var validation = PlaceValidator.Validate(place);
        if (!validation.IsAcceptable)
        {
            TempData["ErrorMessage"] = validation.Reason;
            return RedirectToAction(nameof(Index));
        }
        var restaurantId = await ingestion.UpsertDiscoveredAsync(place, "community_suggestion", DateTimeOffset.UtcNow, ct);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE suggestions SET status='approved' WHERE id=@id AND status='pending'", new { id }, transaction, cancellationToken: ct));
        await audit.WriteAsync("suggestion.accept", "restaurant", restaurantId, System.Text.Json.JsonSerializer.Serialize(new { suggestionId = id }), ct);
        await transaction.CommitAsync(ct);
        TempData["Flash"] = $"Indicação encaminhada ao cadastro #{restaurantId}. Revise e publique na triagem de restaurantes.";
        return Redirect("/admin/discovery");
    }

    [HttpPost("{id:long}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        var changed = await connection.ExecuteAsync(new CommandDefinition("UPDATE suggestions SET status='rejected' WHERE id=@id AND status='pending'", new { id }, cancellationToken: ct));
        if (changed == 0) return NotFound();
        await audit.WriteAsync("suggestion.reject", "suggestion", id, null, ct);
        TempData["Flash"] = "Indicação rejeitada. Nenhum cadastro foi publicado.";
        return RedirectToAction(nameof(Index));
    }

    public sealed class SuggestionRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string City { get; set; } = "";
        public string? Website { get; set; }
        public string? Notes { get; set; }
        public long? TargetRestaurantId { get; set; }
        public DateTime SubmittedAt { get; set; }
    }
}
