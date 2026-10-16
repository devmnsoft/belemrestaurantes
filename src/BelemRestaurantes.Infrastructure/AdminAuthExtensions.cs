using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.MySql;
using BelemRestaurantes.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.Infrastructure;

public static class AdminAuthExtensions
{
    public static IServiceCollection AddAdminAuth(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        services.AddSingleton<ITotpProvider, TotpProvider>();
        services.AddScoped<IAdminUserRepository, MySqlAdminUserRepository>();
        return services;
    }
}