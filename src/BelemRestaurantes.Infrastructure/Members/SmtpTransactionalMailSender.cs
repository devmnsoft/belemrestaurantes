using BelemRestaurantes.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using System.Net;

namespace BelemRestaurantes.Infrastructure.Members;

public interface ISmtpTransportFactory { ISmtpClient Create(); }
public sealed class DefaultSmtpTransportFactory : ISmtpTransportFactory
{
    public ISmtpClient Create() => new SmtpClient();
}

public sealed class SmtpTransactionalMailSender(IConfiguration configuration, ISmtpTransportFactory transports) : ITransactionalMailSender
{
    public async Task SendAsync(string messageId, TransactionalMail message, CancellationToken ct)
    {
        var host = configuration["Smtp:Host"];
        var user = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"];
        var mode = configuration["Smtp:TlsMode"] ?? "StartTls";
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password) || mode is not ("StartTls" or "SslOnConnect"))
            throw new InvalidOperationException("SMTP requires host, credentials and mandatory TLS.");
        var strings = MailCopy.For(message.Culture);
        var subject = message.Kind switch { "confirm" => strings.Confirm, "reset" => strings.Reset, "password_changed" => strings.Changed, _ => throw new InvalidOperationException("Unsupported mail kind.") };
        var text = message.Kind switch { "confirm" => strings.ConfirmBody, "reset" => strings.ResetBody, _ => strings.Changed };
        var mail = new MimeMessage { MessageId = messageId + "@belemrestaurantes.com.br", Subject = subject };
        mail.From.Add(new MailboxAddress("Belém Restaurantes", "comercial@belemrestaurantes.com.br"));
        mail.To.Add(new MailboxAddress(message.Name, message.Recipient));
        var link = message.Link;
        if (link is not null && (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "https")) throw new InvalidOperationException("Invalid action link.");
        var action = link is null ? "" : $"<p><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"display:inline-block;padding:12px 18px;background:#123e33;color:white;border-radius:6px\">{WebUtility.HtmlEncode(subject)}</a></p>";
        var body = new BodyBuilder
        {
            TextBody = $"Belém Restaurantes\n{message.Name}\n{text}\n{link}",
            HtmlBody = $"<!doctype html><html lang=\"{WebUtility.HtmlEncode(message.Culture)}\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><body style=\"font-family:Arial,sans-serif;background:#f7f5ed;padding:20px\"><main style=\"max-width:560px;margin:auto;background:white;padding:24px\"><h1>Belém Restaurantes</h1><p>{WebUtility.HtmlEncode(message.Name)}</p><p>{WebUtility.HtmlEncode(text)}</p>{action}</main></body></html>"
        };
        mail.Body = body.ToMessageBody();
        using var smtp = transports.Create();
        smtp.Timeout = 30000;
        await smtp.ConnectAsync(host, configuration.GetValue("Smtp:Port", mode == "SslOnConnect" ? 465 : 587), mode == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await smtp.AuthenticateAsync(user, password, ct);
        await smtp.SendAsync(mail, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}

internal sealed record MailCopy(string Confirm, string Reset, string Changed, string ConfirmBody, string ResetBody)
{
    internal static MailCopy For(string culture) => culture switch
    {
        "en" => new("Confirm your email", "Reset your password", "Your password has changed", "Confirm your email before publishing. This link expires in 24 hours. Ignore this message if you did not request it.", "Choose a new password. This single-use link expires in 30 minutes. Ignore this message if you did not request it."),
        "fr" => new("Confirmez votre e-mail", "Réinitialisez votre mot de passe", "Votre mot de passe a été modifié", "Confirmez votre e-mail avant de publier. Ce lien expire dans 24 heures. Ignorez ce message si vous ne l’avez pas demandé.", "Choisissez un nouveau mot de passe. Ce lien à usage unique expire dans 30 minutes. Ignorez ce message si vous ne l’avez pas demandé."),
        "es" => new("Confirma tu correo", "Restablece tu contraseña", "Tu contraseña ha cambiado", "Confirma tu correo antes de publicar. Este enlace caduca en 24 horas. Ignora este mensaje si no lo solicitaste.", "Elige una nueva contraseña. Este enlace de un solo uso caduca en 30 minutos. Ignora este mensaje si no lo solicitaste."),
        "ru" => new("Подтвердите электронную почту", "Сброс пароля", "Ваш пароль изменён", "Подтвердите почту перед публикацией. Ссылка действует 24 часа. Если вы не запрашивали это письмо, проигнорируйте его.", "Выберите новый пароль. Одноразовая ссылка действует 30 минут. Если вы не запрашивали это письмо, проигнорируйте его."),
        "de" => new("E-Mail-Adresse bestätigen", "Passwort zurücksetzen", "Ihr Passwort wurde geändert", "Bestätigen Sie Ihre E-Mail-Adresse vor dem Veröffentlichen. Der Link ist 24 Stunden gültig. Ignorieren Sie diese Nachricht, wenn Sie sie nicht angefordert haben.", "Wählen Sie ein neues Passwort. Dieser einmalige Link ist 30 Minuten gültig. Ignorieren Sie diese Nachricht, wenn Sie sie nicht angefordert haben."),
        "ja" => new("メールアドレスの確認", "パスワードの再設定", "パスワードが変更されました", "投稿前にメールアドレスを確認してください。このリンクの有効期限は24時間です。お心当たりがない場合は無視してください。", "新しいパスワードを設定してください。このリンクは一度だけ使用でき、有効期限は30分です。お心当たりがない場合は無視してください。"),
        "zh-Hans" => new("确认您的电子邮箱", "重置密码", "您的密码已更改", "请在发布前确认电子邮箱。此链接将在24小时后失效。若非您本人申请，请忽略此邮件。", "请设置新密码。此链接仅可使用一次，30分钟后失效。若非您本人申请，请忽略此邮件。"),
        _ => new("Confirme seu e-mail", "Recupere sua senha", "Sua senha foi alterada", "Confirme seu e-mail antes de publicar. Este link expira em 24 horas. Ignore esta mensagem se você não a solicitou.", "Escolha uma nova senha. Este link de uso único expira em 30 minutos. Ignore esta mensagem se você não a solicitou.")
    };
}
