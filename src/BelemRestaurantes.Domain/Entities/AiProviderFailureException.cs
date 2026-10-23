using BelemRestaurantes.Domain.Enums;
namespace BelemRestaurantes.Domain.Entities;

public class AiProviderFailureException(AiProvider provider,int statusCode):Exception($"Provedor {provider} respondeu HTTP {statusCode}.")
{
    public AiProvider Provider {get;}=provider;
    public int StatusCode {get;}=statusCode;
}
