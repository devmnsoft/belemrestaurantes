using System.Runtime.CompilerServices;
namespace BelemRestaurantes.IntegrationTests;
internal static class ExternalCallDefaults
{
    // Development user-secrets may enable the live source provider; automated hosts must remain offline.
    [ModuleInitializer] internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("WebSearch__PublicSources__Enabled","false");
        Environment.SetEnvironmentVariable("Research__FetchPages","false");
        Environment.SetEnvironmentVariable("Ai__Gemini__ApiKey","");
        Environment.SetEnvironmentVariable("Ai__Groq__ApiKey","");
        Environment.SetEnvironmentVariable("WebSearch__Tavily__Enabled","false");
        Environment.SetEnvironmentVariable("WebSearch__Brave__Enabled","false");
    }
}
