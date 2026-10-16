using BelemRestaurantes.Web.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

// Requires externally-issued JWT with moderator role. Bearer tokens are not sent automatically by browsers.
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/admin/review")]
[Authorize(Policy = "RestaurantModerator")]
public sealed class AdminReviewController(DbFactory db) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        await using var c = db.Open();
        var items = await c.QueryAsync(new CommandDefinition(
            "SELECT id,name,city,category,created_source_url,google_place_id,created_at FROM public.restaurants WHERE status='pending' ORDER BY id LIMIT 100",
            cancellationToken: ct));
        return Ok(items);
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct) => await SetStatus(id, "published", ct);

    [HttpPost("{id:long}/reject")]
    public async Task<IActionResult> Reject(long id, CancellationToken ct) => await SetStatus(id, "rejected", ct);

    private async Task<IActionResult> SetStatus(long id, string status, CancellationToken ct)
    {
        await using var c = db.Open();
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var changed = await c.ExecuteAsync(new CommandDefinition(
            "UPDATE public.restaurants SET status=@status,updated_at=now() WHERE id=@id AND status='pending'",
            new { id, status }, transaction: tx, cancellationToken: ct));
        if (changed == 0) { await tx.RollbackAsync(ct); return NotFound(); }
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT INTO public.audit_events(actor_subject,action,entity_type,entity_id) VALUES(@actor,@status,'restaurant',@id)",
            new { actor = User.Identity?.Name ?? "authenticated", status, id }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Ok(new { id, status });
    }
}