using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.Infrastructure.Members;

public sealed class MailPayloadProtector : IDisposable
{
    private IDataProtectionProvider? owner;
    private readonly Lazy<IDataProtector> protector;
    public MailPayloadProtector(IConfiguration configuration)
    {
        protector=new(() =>
        {
            var path=configuration["MailEncryption:KeyRingPath"];
            if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))
                throw new InvalidOperationException("Configure a private absolute MailEncryption:KeyRingPath shared by Web and Worker.");
            Directory.CreateDirectory(path);
            owner=DataProtectionProvider.Create(new DirectoryInfo(path),builder=>
            {
                builder.SetApplicationName("BelemRestaurantes.TransactionalMail.v1");
                if(OperatingSystem.IsWindows())builder.ProtectKeysWithDpapi(protectToLocalMachine:true);
            });
            return owner.CreateProtector("Outbox.Payload.v1");
        });
    }
    public byte[] Protect(TransactionalMail message)=>protector.Value.Protect(JsonSerializer.SerializeToUtf8Bytes(message));
    public TransactionalMail Unprotect(byte[] bytes)=>JsonSerializer.Deserialize<TransactionalMail>(protector.Value.Unprotect(bytes))??throw new InvalidOperationException("Invalid mail payload.");
    public void Dispose(){if(owner is IDisposable disposable)disposable.Dispose();}
}
