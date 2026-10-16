using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[ApiController]
[Route("api/admin/review")]
[Authorize(Policy = "RestaurantModerator")]
public sealed class AdminReviewController(
    IModerationRepository moderation) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var pending = await moderation.ListPendingAsync(page, pageSize, ct);
        return Ok(pending);
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var changed = await moderation.ApproveAsync(id, Actor(), ct);
        return changed
            ? Ok(new { id, status = "published" })
            : NotFound(new { message = "Registro não encontrado ou fora do estado pending" });
    }

    [HttpPost("{id:long}/reject")]
    public async Task<IActionResult> Reject(long id, [FromBody] RejectInput input, CancellationToken ct)
    {
        var changed = await moderation.RejectAsync(id, Actor(), input?.Reason ?? "rejeitado pelo moderador", ct);
        return changed
            ? Ok(new { id, status = "rejected" })
            : NotFound(new { message = "Registro não encontrado ou fora do estado pending" });
    }

    private string Actor() => User.Identity?.Name ?? "moderator";

    public sealed record RejectInput(string? Reason);
}