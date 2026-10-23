namespace BelemRestaurantes.Application.Abstractions;
public interface IAdminPasswordRecovery
{
    Task RequestAsync(string identifier,CancellationToken ct);
    Task<bool> ResetAsync(string token,string password,CancellationToken ct);
}
