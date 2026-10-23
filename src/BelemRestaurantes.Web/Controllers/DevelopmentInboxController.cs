using System.Net;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Members;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.Controllers;
public sealed record LocalMail(string Recipient,string Kind,string Link);
[AllowAnonymous,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DevelopmentInboxController(IWebHostEnvironment environment,IConfiguration config,MySqlConnectionFactory factory,MailPayloadProtector protector):Controller
{
    public static bool Allowed(IWebHostEnvironment environment,IConfiguration config,HttpContext context)=>environment.IsDevelopment()&&config.GetValue<bool>("Development:LocalMailboxEnabled")&&context.Connection.RemoteIpAddress is {} ip&&IPAddress.IsLoopback(ip)&&context.Request.Host.Host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
    [HttpGet("/dev/caixa-de-entrada")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if(!Allowed(environment,config,HttpContext))return NotFound();
        Response.Headers["Referrer-Policy"]="no-referrer";Response.Headers["X-Frame-Options"]="DENY";
        var messages=new List<LocalMail>();await using var db=await factory.OpenAsync(ct);
        var payloads=await db.QueryAsync<byte[]>(new CommandDefinition("SELECT payload FROM transactional_email_outbox WHERE expires_at>UTC_TIMESTAMP(6) AND payload IS NOT NULL AND OCTET_LENGTH(payload)>0 ORDER BY created_at DESC LIMIT 25",cancellationToken:ct));
        foreach(var payload in payloads)
        {
            if(payload is null or {Length:0})continue;
            TransactionalMail message;try{message=protector.Unprotect(payload);}catch(Exception ex)when(ex is System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidOperationException){continue;}
            if(message.Link is not {} link||!Uri.TryCreate(link,UriKind.Absolute,out var uri)||uri.AbsolutePath is not ("/account/confirm" or "/account/reset" or "/admin/reset")||!System.Text.RegularExpressions.Regex.IsMatch(uri.Fragment,@"^#token=[A-Fa-f0-9]{64}$"))continue;
            messages.Add(new(message.Recipient,message.Kind,uri.AbsolutePath+uri.Fragment));
        }
        return View(messages);
    }
}
