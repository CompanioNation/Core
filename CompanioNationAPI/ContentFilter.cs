using System.Text.RegularExpressions;

namespace CompanioNationAPI;

/// <summary>
/// Server-side content filter for user-generated text (messages, profile names/descriptions).
/// Uses whole-word matching to avoid false positives (e.g., "Scunthorpe").
/// Apple App Review Guideline 1.2b — method for filtering objectionable content.
/// </summary>
public static partial class ContentFilter
{
    // Compiled regex for word-boundary matching, built once at startup
    private static readonly Regex s_pattern = BuildPattern();

    /// <summary>
    /// Returns <c>true</c> if the text contains prohibited content.
    /// </summary>
    public static bool ContainsProhibitedContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return s_pattern.IsMatch(text);
    }

    /// <summary>
    /// Returns the text with prohibited words replaced with "***".
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        return s_pattern.Replace(text, "***");
    }

    private static Regex BuildPattern()
    {
        // Each term is matched as a whole word (\b...\b) to prevent false positives.
        // Categories: slurs, hate speech, threats of violence, explicit sexual content.
        // This is intentionally a conservative starter list — expand as needed.
        string[] terms =
        [
            // Racial / ethnic slurs
            "nigger", "nigga", "chink", "gook", "spic", "wetback", "kike",
            "beaner", "raghead", "towelhead", "coon", "darkie",

            // Homophobic / transphobic slurs
            "faggot", "fag", "dyke", "tranny",

            // Misogynistic slurs
            "cunt",

            // Hate group references
            "white power", "heil hitler", "sieg heil", "white supremacy",
            "kill all",

            // Threats of violence
            "i will kill you", "i'll kill you", "gonna kill you",
            "death threat", "i will murder",

            // Explicit sexual content (solicitation / graphic acts)
            "send nudes", "dick pic", "wanna fuck",
        ];

        // Build alternation: \b(term1|term2|...)\b with escaped terms
        string alternation = string.Join("|", terms.Select(Regex.Escape));
        string pattern = $@"\b({alternation})\b";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
