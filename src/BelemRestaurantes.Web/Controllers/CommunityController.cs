using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = MemberAuth.Policy)]
[Route("community")]
public sealed class CommunityController(
    IFavoriteRepository favorites,
    IRatingRepository ratings,
    IDeletionRequestRepository deletions) : ControllerBase
{
    private string Subject() => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("Usuário sem identificador.");

    [HttpPost("favorite/{id:long}")]
    public async Task<IActionResult> Favorite(long id, CancellationToken ct)
    {
        var added = await favorites.AddAsync(Subject(), id, ct);
        return Ok(new { favorited = true, created = added });
    }

    [HttpDelete("favorite/{id:long}")]
    public async Task<IActionResult> Unfavorite(long id, CancellationToken ct)
    {
        var removed = await favorites.RemoveAsync(Subject(), id, ct);
        return Ok(new { favorited = false, removed });
    }

    [HttpGet("favorite/{id:long}")]
    public async Task<IActionResult> IsFavorite(long id, CancellationToken ct) =>
        Ok(new { favorited = await favorites.IsFavoriteAsync(Subject(), id, ct) });

    [HttpPost("rate/{id:long}")]
    public async Task<IActionResult> Rate(long id, [FromBody] RatingInput input, CancellationToken ct)
    {
        if (input.Stars is < 1 or > 5 || input.Review?.Length > 3000)
        {
            return BadRequest(new { message = "Nota inválida" });
        }

        var reportId = await ratings.UpsertAsync(Subject(), id, input.Stars, input.Review, ct);
        return Ok(new { id = reportId, status = "pending_moderation" });
    }

    [HttpGet("ratings/{id:long}")]
    public async Task<IActionResult> Ratings(long id, CancellationToken ct)
    {
        var list = await ratings.ListApprovedAsync(id, ct);
        var average = await ratings.AverageAsync(id, ct);
        return Ok(new { average, items = list });
    }

    [HttpPost("deletion-request")]
    public async Task<IActionResult> RequestDeletion([FromBody] DeletionInput input, CancellationToken ct)
    {
        var id = await deletions.CreateAsync(Subject(), input.TargetType, input.TargetId, input.Reason, ct);
        return Ok(new { id, status = "pending" });
    }

    public sealed record RatingInput(int Stars, string? Review);
    public sealed record DeletionInput(string TargetType, long TargetId, string? Reason);
}