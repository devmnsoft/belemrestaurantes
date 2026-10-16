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
            RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()]
        };
    }
}
