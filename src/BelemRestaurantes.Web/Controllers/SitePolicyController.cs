using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.Controllers;
[AllowAnonymous]
public sealed class SitePolicyController:Controller
{
    [HttpGet("privacidade"),HttpGet("privacy")]
    public IActionResult Privacy()=>View();
    [HttpGet("termos")]
    public IActionResult Terms()=>View();
}
