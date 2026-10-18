using System.Security.Cryptography;
using System.Text;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;

namespace BelemRestaurantes.Infrastructure.OpenData;

/// <summary>Retomada no próprio banco de destino. Replay de página incompleta usa a deduplicação por CNPJ.</summary>
public sealed class RegistryImportLedger(MySqlConnectionFactory factory)
{
    public static string HashPage(string page)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page)));

    public async Task<bool> IsCompletedAsync(string hash,CancellationToken ct)
    {
        await using var connection=await factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM registry_import_pages WHERE page_sha256=@hash",new{hash},cancellationToken:ct))>0;
    }

    public async Task CompleteAsync(string hash,int readCount,long cycleId,CancellationToken ct)
    {
        if(hash.Length!=64 || !hash.All(Uri.IsHexDigit) || readCount is <0 or >1024 || cycleId<1)
            throw new ArgumentException("Checkpoint cadastral inválido.");
        await using var connection=await factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT IGNORE INTO registry_import_pages(page_sha256,record_count,completed_at,cycle_id)
            VALUES(@hash,@readCount,UTC_TIMESTAMP(6),@cycleId)
            """,new{hash,readCount,cycleId},cancellationToken:ct));
    }
}
