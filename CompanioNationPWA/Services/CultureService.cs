using System.Globalization;
using Microsoft.JSInterop;

namespace CompanioNationPWA.Services;

/// <summary>
/// Manages the user's culture preference via localStorage and browser auto-detection.
/// </summary>
internal sealed class CultureService
{
    private readonly IJSRuntime _jsRuntime;

    private static readonly string[] SupportedCultures = ["en", "fr", "es"];

    public CultureService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Initializes the app culture from localStorage or browser language auto-detection.
    /// Call once during app startup, after the host is built.
    /// </summary>
    public async Task InitializeCultureAsync()
    {
        var savedCulture = await _jsRuntime.InvokeAsync<string>("blazorCulture.get");

        string culture;
        if (!string.IsNullOrEmpty(savedCulture) && SupportedCultures.Contains(savedCulture))
        {
            culture = savedCulture;
        }
        else
        {
            var browserLanguages = await _jsRuntime.InvokeAsync<string[]>("blazorCulture.getBrowserLanguages");
            culture = browserLanguages
                .Select(lang => lang.Split('-')[0])
                .FirstOrDefault(lang => SupportedCultures.Contains(lang))
                ?? "en";
        }

        ApplyCulture(culture);
        await _jsRuntime.InvokeVoidAsync("blazorCulture.setDocLang", culture);
    }

    /// <summary>
    /// Changes the culture, saves to localStorage, and reloads the app.
    /// </summary>
    public async Task SetCultureAsync(string culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (!SupportedCultures.Contains(culture))
            return;

        await _jsRuntime.InvokeVoidAsync("blazorCulture.set", culture);
        ApplyCulture(culture);
        await _jsRuntime.InvokeVoidAsync("blazorCulture.setDocLang", culture);

        // Reload so the new culture takes effect across all components
        await _jsRuntime.InvokeVoidAsync("location.reload");
    }

    /// <summary>
    /// Returns the current culture code (e.g., "en", "fr").
    /// </summary>
    public static string GetCurrentCulture() =>
        CultureInfo.DefaultThreadCurrentUICulture?.TwoLetterISOLanguageName ?? "en";

    /// <summary>
    /// Returns the list of supported culture codes.
    /// </summary>
    public static IReadOnlyList<string> GetSupportedCultures() => SupportedCultures;

    private static void ApplyCulture(string culture)
    {
        var cultureInfo = new CultureInfo(culture);
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    }
}
