namespace BelemRestaurantes.Web.Services;
public sealed record ProviderState(string Name,bool Configured,string Notes);
public sealed class ProviderRegistry(IConfiguration config) {
 public IReadOnlyList<ProviderState> GetStates()=>[
 new("Google Places",!string.IsNullOrWhiteSpace(config["GOOGLE_PLACES_API_KEY"]),"Descoberta por fonte autorizada; exige verificação"),
 new("Instagram / Facebook",!string.IsNullOrWhiteSpace(config["META_APP_SECRET"]),"Conector pendente; API e permissões necessárias"),
 new("TikTok",!string.IsNullOrWhiteSpace(config["TIKTOK_CLIENT_SECRET"]),"Conector pendente; API e consentimento necessários"),
 new("YouTube",!string.IsNullOrWhiteSpace(config["YOUTUBE_API_KEY"]),"Conector pendente; API e quotas necessárias"),
 new("Groq",!string.IsNullOrWhiteSpace(config["GROQ_API_KEY"]),"Adaptador de IA pendente"),
 new("Gemini",!string.IsNullOrWhiteSpace(config["GEMINI_API_KEY"]),"Adaptador de IA pendente"),
 new("DeepSeek",!string.IsNullOrWhiteSpace(config["DEEPSEEK_API_KEY"]),"Adaptador de IA pendente")
 ];
}