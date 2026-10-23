using BelemRestaurantes.Infrastructure.Partner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Worker;

public sealed class EditorialReviewHostedService(IServiceScopeFactory scopes,ILogger<EditorialReviewHostedService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try{using var scope=scopes.CreateScope();var count=await scope.ServiceProvider.GetRequiredService<EditorialReviewScheduler>().EnqueueDueAsync(ct);if(count>0)logger.LogInformation("{Count} roteiros entraram na fila de revisão de fontes. A rotina não confirma dados externos automaticamente.",count);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning("Fila editorial indisponível ({Type}); nova tentativa no próximo ciclo.",ex.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromHours(1),ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        }
    }
}
