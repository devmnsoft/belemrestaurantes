using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy = "AdminArea")]
[Route("admin")]
public sealed class AdminCommunityController(
    IExperienceService experienceService,
    IPhotoStorageService photoService,
    ICuiaService cuiaService,
    ILgpdService lgpdService,
    IMemberAccounts memberAccounts,
    MySqlConnectionFactory factory,
    IStringLocalizer<SharedResource> text) : Controller
{
    private string Actor() => User.Identity?.Name ?? "admin";

    // 1. Moderação de Experiências
    [HttpGet("experiences")]
    public async Task<IActionResult> Experiences(int page = 1, CancellationToken ct = default)
    {
        var pending = await experienceService.ListPendingModerationAsync(page, 50, ct);
        ViewData["Page"] = page;
        return View("~/Views/AdminAccount/Experiences.cshtml", pending);
    }

    [HttpPost("experiences/{id:long}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveExperience(long id, CancellationToken ct)
    {
        await experienceService.ModerateAsync(id, "approved", null, Actor(), ct);
        TempData["Flash"] = text["ItemApproved"].Value;
        return RedirectToAction("Experiences");
    }

    [HttpPost("experiences/{id:long}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectExperience(long id, [FromForm] string reason, CancellationToken ct)
    {
        await experienceService.ModerateAsync(id, "rejected", reason, Actor(), ct);
        TempData["Flash"] = text["ItemRejected"].Value;
        return RedirectToAction("Experiences");
    }

    [HttpPost("experiences/{id:long}/appeal-decision")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AppealDecision(long id, [FromForm] bool accept, CancellationToken ct)
    {
        await experienceService.ReviewAppealAsync(id, accept, Actor(), ct);
        TempData["Flash"] = accept ? text["ItemApproved"].Value : text["ItemRejected"].Value;
        return RedirectToAction("Experiences");
    }

    // 2. Moderação de Fotos
    [HttpGet("photos")]
    public async Task<IActionResult> Photos(int page = 1, CancellationToken ct = default)
    {
        var photos = await photoService.ListPendingPhotosAsync(page, 50, ct);
        ViewData["Page"] = page;
        return View("~/Views/AdminAccount/Photos.cshtml", photos);
    }

    [HttpPost("photos/{id:long}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePhoto(long id, CancellationToken ct)
    {
        await photoService.ModeratePhotoAsync(id, "approved", Actor(), ct);
        TempData["Flash"] = text["PhotoApproved"].Value;
        return RedirectToAction("Photos");
    }

    [HttpPost("photos/{id:long}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPhoto(long id, CancellationToken ct)
    {
        await photoService.ModeratePhotoAsync(id, "rejected", Actor(), ct);
        TempData["Flash"] = text["PhotoRejected"].Value;
        return RedirectToAction("Photos");
    }

    // 3. Denúncias da Comunidade
    [HttpGet("reports")]
    public async Task<IActionResult> Reports(int page = 1, CancellationToken ct = default)
    {
        var reports = await experienceService.ListPendingReportsAsync(page, 50, ct);
        ViewData["Page"] = page;
        return View("~/Views/AdminAccount/Reports.cshtml", reports);
    }

    [HttpPost("reports/{id:long}/review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewReport(long id, [FromForm] bool uphold, CancellationToken ct)
    {
        await experienceService.ReviewReportAsync(id, uphold, Actor(), ct);
        TempData["Flash"] = uphold ? text["ReportUpheld"].Value : text["ReportDismissed"].Value;
        return RedirectToAction("Reports");
    }

    // 4. Gestão de Visitantes / Membros
    [HttpGet("members")]
    public async Task<IActionResult> Members([FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var members = await memberAccounts.ListMembersAdminAsync(page, 50, search, ct);
        ViewData["Search"] = search;
        ViewData["Page"] = page;
        return View("~/Views/AdminAccount/Members.cshtml", members);
    }

    [HttpPost("members/{id}/lock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LockMember(string id, [FromForm] string reason, CancellationToken ct)
    {
        await memberAccounts.LockAccountAsync(id, reason, ct);
        TempData["Flash"] = text["MemberLocked"].Value;
        return RedirectToAction("Members");
    }

    [HttpPost("members/{id}/unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockMember(string id, CancellationToken ct)
    {
        await memberAccounts.UnlockAccountAsync(id, ct);
        TempData["Flash"] = text["MemberUnlocked"].Value;
        return RedirectToAction("Members");
    }

    // 5. Atendimento LGPD
    [HttpGet("lgpd")]
    public async Task<IActionResult> Lgpd(CancellationToken ct)
    {
        var requests = await lgpdService.ListPendingRequestsAsync(ct);
        return View("~/Views/AdminAccount/Lgpd.cshtml", requests);
    }

    [HttpPost("lgpd/{id:long}/anonymize")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnonymizeMember(long id, [FromForm] string userId, CancellationToken ct)
    {
        await lgpdService.AnonymizeAccountAsync(userId, Actor(), ct);
        await lgpdService.ReviewRequestAsync(id, true, "Anonimizado administrativamente", Actor(), ct);
        TempData["Flash"] = text["AccountAnonymized"].Value;
        return RedirectToAction("Lgpd");
    }

    [HttpPost("lgpd/{id:long}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteLgpdRequest(long id, [FromForm] string? notes, CancellationToken ct)
    {
        await lgpdService.ReviewRequestAsync(id, true, notes, Actor(), ct);
        TempData["Flash"] = text["LgpdCompleted"].Value;
        return RedirectToAction("Lgpd");
    }

    // 6. Selo Cuia e Rankings
    [HttpGet("cuia")]
    public async Task<IActionResult> Cuia(CancellationToken ct)
    {
        var highlights = await cuiaService.GetAllHighlightsAsync(ct);
        return View("~/Views/AdminAccount/Cuia.cshtml", highlights);
    }

    [HttpPost("cuia/recalculate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecalculateCuia(CancellationToken ct)
    {
        await cuiaService.RecalculateRankingsAsync(ct);
        TempData["Flash"] = text["CuiaRecalculated"].Value;
        return RedirectToAction("Cuia");
    }

    // 7. Monitoramento SMTP (Outbox)
    [HttpGet("smtp")]
    public async Task<IActionResult> Smtp(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        const string statsSql = """
            SELECT state, COUNT(*) AS count
            FROM transactional_email_outbox
            GROUP BY state
            """;

        var stateCounts = (await connection.QueryAsync<StateCountRow>(new CommandDefinition(statsSql, cancellationToken: ct))).ToDictionary(r => r.state, r => r.count);

        const string recentSql = """
            SELECT id, state, attempts, next_attempt_at, created_at, expires_at, last_error, completed_at
            FROM transactional_email_outbox
            ORDER BY created_at DESC
            LIMIT 50
            """;

        var recent = (await connection.QueryAsync<OutboxRow>(new CommandDefinition(recentSql, cancellationToken: ct))).AsList();

        ViewData["StateCounts"] = stateCounts;
        return View("~/Views/AdminAccount/Smtp.cshtml", recent);
    }

    private sealed class StateCountRow { public string state { get; set; } = ""; public int count { get; set; } }
    private sealed class OutboxRow
    {
        public string id { get; set; } = "";
        public string state { get; set; } = "";
        public int attempts { get; set; }
        public DateTimeOffset next_attempt_at { get; set; }
        public DateTimeOffset created_at { get; set; }
        public DateTimeOffset expires_at { get; set; }
        public string? last_error { get; set; }
        public DateTimeOffset? completed_at { get; set; }
    }
}
