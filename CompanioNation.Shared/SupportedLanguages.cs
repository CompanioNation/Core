namespace CompanioNation.Shared;

/// <summary>
/// Canonical list of languages supported by CompanioNation, shared by the
/// Blazor client (CultureService) and the API (MaintenanceEventService).
/// </summary>
public static class SupportedLanguages
{
    public static readonly string[] Codes = ["en", "es", "pt", "fr", "zh", "ja"];

    public static string NativeName(string languageCode) => languageCode switch
    {
        "en" => "English",
        "es" => "Español",
        "pt" => "Português",
        "fr" => "Français",
        "zh" => "中文",
        "ja" => "日本語",
        _ => "English"
    };

    /// <summary>
    /// Maps arbitrary culture input (e.g. "pt-BR", "zh-Hans") to a supported two-letter code,
    /// falling back to English when unknown.
    /// </summary>
    public static string Normalize(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            string code = languageCode.Trim().ToLowerInvariant().Split('-')[0];
            if (Codes.Contains(code))
                return code;
        }

        return "en";
    }
}
