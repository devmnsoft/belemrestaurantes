using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace BelemRestaurantes.Web.Localization;

public static class SupportedLanguages
{
    public const string DefaultCulture = "pt-BR";
    public static readonly IReadOnlyList<(string Code, string Name)> All = Array.AsReadOnly(new[]
    {
        ("pt-BR", "Português"), ("en", "English"), ("fr", "Français"), ("es", "Español"),
        ("ru", "Русский"), ("de", "Deutsch"), ("ja", "日本語"), ("zh-Hans", "简体中文")
    });

    public static RequestLocalizationOptions Options()
    {
        var cultures = All.Select(language => new CultureInfo(language.Code)).ToArray();
        return new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(DefaultCulture),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
            ApplyCurrentCultureToResponseHeaders = true,
            RequestCultureProviders = [new CookieRequestCultureProvider(), new LanguageHeaderProvider()]
        };
    }
}

internal sealed class LanguageHeaderProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext context)
    {
        try
        {
            var languages = context.Request.GetTypedHeaders().AcceptLanguage;
            if (languages is not null)
                foreach (var language in languages.Where(item => (item.Quality ?? 1) > 0).OrderByDescending(item => item.Quality ?? 1).Take(20))
                {
                    var primary = language.Value.Value?.Split('-')[0].ToLowerInvariant();
                    var code = primary switch { "zh" => "zh-Hans", "pt" => "pt-BR", _ => primary };
                    if (SupportedLanguages.All.Any(item => item.Code == code))
                        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(code!));
                }
        }
        catch (FormatException) { /* Ignore malformed language preferences. */ }
        return Task.FromResult<ProviderCultureResult?>(null);
    }
}
