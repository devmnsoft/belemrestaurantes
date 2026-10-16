using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Members;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace BelemRestaurantes.IntegrationTests;

public sealed class SmtpTransportTests
{
    [Fact]
    public async Task Real_smtp_transport_sends_localized_mime_over_tls_to_loopback_only()
    {
        using var rootKey=RSA.Create(2048);
        var rootRequest=new CertificateRequest("CN=BelRest test CA",rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
        using var root=rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(1));
        using var serverKey=RSA.Create(2048);
        var request=new CertificateRequest("CN=localhost",serverKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");san.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(san.Build());
        using var unsigned=request.Create(root,DateTimeOffset.UtcNow.AddMinutes(-2),DateTimeOffset.UtcNow.AddMinutes(30),RandomNumberGenerator.GetBytes(16));
        using var signedCertificate=unsigned.CopyWithPrivateKey(serverKey);
        using var serverCertificate=X509CertificateLoader.LoadPkcs12(signedCertificate.Export(X509ContentType.Pkcs12),null,X509KeyStorageFlags.Exportable);
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var received=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server=ServeAsync(listener,serverCertificate,received,cancellation.Token);
        try
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Smtp:Host"]="127.0.0.1",["Smtp:Port"]=port.ToString(),["Smtp:TlsMode"]="SslOnConnect",["Smtp:Username"]="local-test",["Smtp:Password"]=Guid.NewGuid().ToString("N") }).Build();
            var sender=new SmtpTransactionalMailSender(config,new LocalTrustFactory(root));
            await sender.SendAsync(Guid.NewGuid().ToString("N"),new TransactionalMail("test@example.invalid","<Test>","confirm","https://belemrestaurantes.com.br/account/confirm#token=synthetic","ja"),cancellation.Token);
            var raw=await received.Task.WaitAsync(cancellation.Token);
            using var message=MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(raw)));
            Assert.Equal("メールアドレスの確認",message.Subject);
            Assert.Equal("comercial@belemrestaurantes.com.br",Assert.IsType<MailboxAddress>(Assert.Single(message.From)).Address);
            Assert.Contains("&lt;Test&gt;",message.HtmlBody);
            Assert.Contains("#token=synthetic",message.TextBody);
            await server;
        }
        catch { if(server.IsFaulted)await server; throw; }
        finally {await cancellation.CancelAsync();listener.Stop();}
    }

    private sealed class LocalTrustFactory(X509Certificate2 root) : ISmtpTransportFactory
    {
        public ISmtpClient Create()=>new SmtpClient
        {
            ServerCertificateValidationCallback=(_,certificate,_,errors)=>
            {
                if((errors&(SslPolicyErrors.RemoteCertificateNameMismatch|SslPolicyErrors.RemoteCertificateNotAvailable))!=0||certificate is not X509Certificate2 leaf)return false;
                using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;
                return chain.Build(leaf);
            }
        };
    }

    private static async Task ServeAsync(TcpListener listener,X509Certificate2 certificate,TaskCompletionSource<string> received,CancellationToken ct)
    {
        using var connection=await listener.AcceptTcpClientAsync(ct);
        await using var stream=new SslStream(connection.GetStream());
        await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=certificate,EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},ct);
        using var reader=new StreamReader(stream,new UTF8Encoding(false),leaveOpen:true);
        await using var writer=new StreamWriter(stream,new UTF8Encoding(false),leaveOpen:true){AutoFlush=true,NewLine="\r\n"};
        await writer.WriteLineAsync("220 localhost ESMTP test");
        while(await reader.ReadLineAsync(ct) is {} line)
        {
            if(line.StartsWith("EHLO",StringComparison.Ordinal)){await writer.WriteLineAsync("250-localhost");await writer.WriteLineAsync("250 AUTH PLAIN");}
            else if(line.StartsWith("AUTH",StringComparison.Ordinal))await writer.WriteLineAsync("235 2.7.0 Accepted");
            else if(line.StartsWith("MAIL FROM:",StringComparison.Ordinal)||line.StartsWith("RCPT TO:",StringComparison.Ordinal))await writer.WriteLineAsync("250 2.1.0 OK");
            else if(line=="DATA")
            {
                await writer.WriteLineAsync("354 End with a dot");var body=new StringBuilder();
                while(await reader.ReadLineAsync(ct) is {} data&&data!=".")body.AppendLine(data.StartsWith("..",StringComparison.Ordinal)?data[1..]:data);
                received.TrySetResult(body.ToString());await writer.WriteLineAsync("250 2.0.0 Queued");
            }
            else if(line=="QUIT"){await writer.WriteLineAsync("221 Bye");return;}
            else await writer.WriteLineAsync("250 OK");
        }
    }
}
