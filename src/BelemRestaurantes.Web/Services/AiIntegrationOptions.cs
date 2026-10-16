namespace BelemRestaurantes.Web.Services;
public sealed class AiIntegrationOptions {
 public string Provider {get;set;}="Disabled";
 public int MonthlyRequestLimit {get;set;}=1000;
 public bool AutoPublish {get;set;}=false;
 public string? GeminiApiKey {get;set;}
 public string? GroqApiKey {get;set;}
 public string? DeepSeekApiKey {get;set;}
 public bool IsConfigured => Provider.ToLowerInvariant() switch {
  "gemini"=>!string.IsNullOrWhiteSpace(GeminiApiKey),
  "groq"=>!string.IsNullOrWhiteSpace(GroqApiKey),
  "deepseek"=>!string.IsNullOrWhiteSpace(DeepSeekApiKey),
  _=>false
 };
}