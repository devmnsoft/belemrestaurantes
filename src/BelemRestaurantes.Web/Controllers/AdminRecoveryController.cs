using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace BelemRestaurantes.Web.Controllers;
[AllowAnonymous,Route("admin"),EnableRateLimiting("account"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AdminRecoveryController(IAdminPasswordRecovery recovery,IConfiguration config,IWebHostEnvironment environment):Controller
{
    [HttpGet("forgot")]public IActionResult Forgot()=>View(new AdminRecoveryInput());
    [HttpPost("forgot"),ValidateAntiForgeryToken]public async Task<IActionResult> ForgotSubmit(AdminRecoveryInput input,CancellationToken ct)
    {
        if(ModelState.IsValid)await recovery.RequestAsync(input.Identifier,ct);
        ViewData["Message"]="Se os dados corresponderem a uma conta administrativa habilitada, enviaremos uma recuperação ao e-mail cadastrado. O segundo fator continuará obrigatório.";
        ViewData["DeliveryNotice"]=config.GetValue<bool>("Smtp:Enabled")?"Verifique a caixa de entrada e o spam.":"O envio externo de e-mail está indisponível neste ambiente.";
        ViewData["AdminRecovery"]=true;
        ViewData["LocalMailbox"]=DevelopmentInboxController.Allowed(environment,config,HttpContext);
        return View("~/Views/Account/Message.cshtml");
    }
    [HttpGet("reset")]public IActionResult Reset()=>View(new ResetInput());
    [HttpPost("reset"),ValidateAntiForgeryToken]public async Task<IActionResult> ResetSubmit(ResetInput input,CancellationToken ct)
    {
        if(!ModelState.IsValid){input.Password="";input.ConfirmPassword="";return View("Reset",input);}
        ViewData["Message"]=await recovery.ResetAsync(input.Token,input.Password,ct)?"Senha administrativa alterada. Entre com usuário ou e-mail, a nova senha e o segundo fator.":"Link inválido, expirado ou já utilizado. Solicite uma nova recuperação.";
        ViewData["AdminRecovery"]=true;return View("~/Views/Account/Message.cshtml");
    }
}
public sealed class AdminRecoveryInput
{
    [System.ComponentModel.DataAnnotations.Required,System.ComponentModel.DataAnnotations.StringLength(200)]public string Identifier{get;set;}="";
}
