using System.Resources;

namespace CompanioNation.Shared;

/// <summary>
/// Strongly-typed accessor for localized CompanioNita AI messages that are shown to users by
/// BOTH the server (as the hub error text) and the browser client (when it re-renders that
/// text). Resources live in CompanioNitaStrings.resx and its culture variants.
/// </summary>
public sealed class CompanioNitaStrings
{
    private static readonly ResourceManager ResourceManager =
        new("CompanioNation.Shared.CompanioNitaStrings", typeof(CompanioNitaStrings).Assembly);

    private CompanioNitaStrings() { }

    public static string CompanioNita_DailyLimitReached => GetResource(nameof(CompanioNita_DailyLimitReached));

    private static string GetResource(string name) => ResourceManager.GetString(name) ?? string.Empty;
}
