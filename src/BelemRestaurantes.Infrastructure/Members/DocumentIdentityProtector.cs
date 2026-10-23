using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.Infrastructure.Members;

/// <summary>Stable keyed lookup without retaining full CPF/CNPJ. Key file belongs to the private shared key ring.</summary>
public sealed class DocumentIdentityProtector(IConfiguration configuration) : IDisposable
{
    private IDataProtectionProvider? owner;
    private readonly object gate = new();
    private byte[]? key;

    public string Digest(string kind, string normalized)
    {
        lock (gate)
        {
            if (key is null)
            {
                var folder = configuration["MailEncryption:KeyRingPath"];
                if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) throw new InvalidOperationException("A private identity key ring is required.");
                Directory.CreateDirectory(folder);
                owner = DataProtectionProvider.Create(new DirectoryInfo(folder), builder =>
                {
                    builder.SetApplicationName("BelemRestaurantes.DocumentIdentity.v1");
                    if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
                });
                var protector = owner.CreateProtector("LookupKey.v1");
                var path = Path.Combine(folder, "document-lookup.v1.bin");
                if (!File.Exists(path))
                {
                    var temp = Path.Combine(folder, "document-lookup-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        File.WriteAllBytes(temp, protector.Protect(RandomNumberGenerator.GetBytes(32)));
                        try { File.Move(temp, path, overwrite: false); }
                        catch (IOException) when (File.Exists(path)) { }
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                key = protector.Unprotect(File.ReadAllBytes(path));
            }
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(kind + ":" + normalized)));
        }
    }

    public void Dispose()
    {
        if (key is not null) CryptographicOperations.ZeroMemory(key);
        if (owner is IDisposable disposable) disposable.Dispose();
    }
}
