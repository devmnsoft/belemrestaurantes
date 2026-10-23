using System.Runtime.CompilerServices;
namespace BelemRestaurantes.IntegrationTests;
internal static class CloudIsolation
{
    [ModuleInitializer]
    internal static void DisableRealPlanDuringTests()
    {
        Environment.SetEnvironmentVariable("WebSearch__PublicSites__Enabled","false");
        // Tests never consume an operator's locally configured prepaid AI plan.
        Environment.SetEnvironmentVariable("Ai__Drael__Enabled","false");
        Environment.SetEnvironmentVariable("Ai__Drael__SearchEnabled","false");
    }
}
