using System.Security.Cryptography;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.Members;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;
[Collection("MySql")]
public sealed class Sprint48IdentityTests
{
    internal static string NewCpf()
    {
        var digits=Enumerable.Range(0,9).Select(_=>RandomNumberGenerator.GetInt32(10)).ToList();
        for(var length=9;length<=10;length++){var sum=digits.Select((d,i)=>d*(length+1-i)).Sum();var digit=sum*10%11;digits.Add(digit==10?0:digit);}
        return string.Concat(digits);
    }

    [Fact]
    public async Task ConcurrentRegistrationsDeduplicateCpfAndEmailWithoutLeakingFullDocument()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var prefix="s48-"+Guid.NewGuid().ToString("N");var cpf=NewCpf();
        var accounts=services.GetRequiredService<IMemberAccounts>();
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try{
            await Task.WhenAll(Enumerable.Range(0,4).Select(i=>accounts.RegisterAsync(new("Teste de identidade",prefix+i+"@example.invalid","SafeS48!"+prefix,true,"pt-BR",cpf),default)));
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_users WHERE email LIKE @term",new{term=prefix+"%"}));
            var row=await db.QuerySingleAsync("SELECT cpf_digest,cpf_last4,email FROM member_users WHERE email LIKE @term",new{term=prefix+"%"});
            Assert.Equal(cpf[^4..],(string)row.cpf_last4);Assert.NotEqual(cpf,(string)row.cpf_digest);
            var secondCpf=NewCpf();await accounts.RegisterAsync(new("Duplicado",((string)row.email).ToUpperInvariant(),"SafeS48!"+prefix,true,"pt-BR",secondCpf),default);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_users WHERE email LIKE @term",new{term=prefix+"%"}));
            Assert.Equal(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_users WHERE cpf_digest=@digest",new{digest=services.GetRequiredService<DocumentIdentityProtector>().Digest("cpf",secondCpf)}));
        }finally{await db.ExecuteAsync("DELETE FROM member_users WHERE email LIKE @term",new{term=prefix+"%"});}
    }

    [Fact]
    public async Task SharedOrganizationDoesNotDuplicateOrGrantPartnerRights()
    {
        using var services=(ServiceProvider)ServiceContainer.Build();await services.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var accounts=services.GetRequiredService<IMemberAccounts>();var marker="s48-company-"+Guid.NewGuid().ToString("N");
        const string cnpj="12.ABC.345/01DE-35";
        var digest=services.GetRequiredService<DocumentIdentityProtector>().Digest("cnpj",BelemRestaurantes.Domain.Identity.BrazilianDocument.Normalize(cnpj));
        await using var db=await services.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        try{
            await accounts.RegisterAsync(new("Responsável A",marker+"a@example.invalid","SafeS48!"+marker,true,"pt-BR",NewCpf(),cnpj,marker),default);
            await accounts.RegisterAsync(new("Responsável B",marker+"b@example.invalid","SafeS48!"+marker,true,"pt-BR",NewCpf(),cnpj,"Não sobrescrever nome"),default);
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_organizations WHERE tax_digest=@digest",new{digest}));
            Assert.Equal(marker,await db.ExecuteScalarAsync<string>("SELECT legal_name FROM member_organizations WHERE tax_digest=@digest",new{digest}));
            Assert.Equal(2,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_organization_requests q JOIN member_users u ON u.id=q.user_id WHERE u.email LIKE @term AND q.state='pending'",new{term=marker+"%"}));
            Assert.Equal(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM member_organizations WHERE tax_digest=@digest AND verified_at IS NOT NULL",new{digest}));
        }finally{await db.ExecuteAsync("DELETE FROM member_users WHERE email LIKE @term",new{term=marker+"%"});await db.ExecuteAsync("DELETE FROM member_organizations WHERE tax_digest=@digest AND legal_name=@marker",new{digest,marker});}
    }
}
