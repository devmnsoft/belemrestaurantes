using System.Security.Cryptography;
using System.Text.Json;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Identity;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BelemRestaurantes.Worker;

public static class HomologationProvision
{
    public static async Task RunAsync(IServiceProvider services,bool authorized,bool reset)
    {
        if(!OperatingSystem.IsWindows() || !authorized || !services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            throw new InvalidOperationException("Provisionamento exige Windows, Development e --authorize-homologation.");
        using var scope=services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var users=scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();
        var passwords=scope.ServiceProvider.GetRequiredService<IPasswordVerifier>();
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MNSOFT","BelemRestaurantes","homologacao.credentials.dpapi");
        var credentials=File.Exists(path)?JsonSerializer.Deserialize<Dictionary<string,string>>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(path),null,DataProtectionScope.CurrentUser))!:new Dictionary<string,string>();
        await using var db=await scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        foreach(var (name,roles) in new[]{("superadmin.mnsoft",new[]{AdminRoles.PlatformAdmin}),("operador.teste",new[]{AdminRoles.Operator,AdminRoles.Moderator}),("editor.teste",new[]{AdminRoles.Editor})})
        {
            var user=await users.FindByNameAsync(name,default);
            if(user is null || reset)
            {
                var password="Aa1!"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                credentials[name]=password;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporaryPath=path+".tmp";
                await File.WriteAllBytesAsync(temporaryPath,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials),null,DataProtectionScope.CurrentUser));
                File.Move(temporaryPath,path,true);
                if(user is null)
                {
                    user=new(Guid.NewGuid().ToString(),name,name+"@homologacao.invalid",passwords.Hash(password),false,null,0,null,null);
                    await users.CreateAsync(user,roles,default);
                }
                else
                    await db.ExecuteAsync("UPDATE admin_users SET password_hash=@hash,password_changed_at=UTC_TIMESTAMP(6),access_failed_count=0,lockout_end_at=NULL WHERE id=@id",new{id=user.Id,hash=passwords.Hash(password)});
                await db.ExecuteAsync("UPDATE admin_users SET must_change_password=TRUE WHERE id=@id",new{id=user.Id});
            }
            await using var tx=await db.BeginTransactionAsync();
            await db.ExecuteAsync("DELETE FROM admin_user_roles WHERE user_id=@id",new{id=user.Id},tx);
            foreach(var role in roles)await db.ExecuteAsync("INSERT INTO admin_user_roles(user_id,role_id) SELECT @id,id FROM admin_roles WHERE name=@role",new{id=user.Id,role},tx);
            await db.ExecuteAsync("INSERT INTO audit_events(actor_subject,action,entity_type,metadata,occurred_at) VALUES('local-provision','admin.homologation_reconcile','admin_user',@meta,UTC_TIMESTAMP(6))",new{meta=JsonSerializer.Serialize(new{name,roles,reset})},tx);
            await tx.CommitAsync();
            var persisted=await users.FindByNameAsync(name,default);
            var checkedPassword=credentials.TryGetValue(name,out var secret)&&passwords.Verify(secret,persisted!.PasswordHash);
            Console.WriteLine($"{name}: persistido; papéis={string.Join(',',await users.GetRolesAsync(user.Id,default))}; hash validado={checkedPassword}; MFA será exigido no acesso.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials),null,DataProtectionScope.CurrentUser));
        Console.WriteLine("Credenciais protegidas com DPAPI CurrentUser. Exiba somente no terminal local com scripts/windows/show-homologation-credentials.ps1. Nenhuma senha foi enviada aos logs.");
    }
}
