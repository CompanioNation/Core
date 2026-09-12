namespace CompanioNation.Shared;

/// <summary>
/// Telemetry for a single nightly daily-advice AI call — either the shared English outline
/// or one language's column. Captured inside the retry/fallback loop and carried back to
/// the maintenance job so the nightly report can state exactly what happened (which
/// provider answered, whether thinking had to be disabled, how many tries, how long)
/// without an admin having to read server logs.
/// </summary>
public sealed class DailyAdviceGenerationReport
{
    /// <summary>
    /// "outline" for the shared English outline, otherwise the language code (e.g. "es").
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Type name of the primary provider that served this call (e.g. "CompanioNitaDeepSeek").
    /// </summary>
    public string PrimaryProviderName { get; set; } = string.Empty;

    /// <summary>Whether the call produced usable text.</summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Whether the first attempt succeeded outright — no retries and no fallback.
    /// </summary>
    public bool SucceededFirstTry { get; set; }

    /// <summary>
    /// Primary attempts actually made (1 + retries used). Zero when the circuit breaker was
    /// open and the call went straight to the fallback provider.
    /// </summary>
    public int PrimaryAttemptsMade { get; set; }

    /// <summary>Number of retries consumed before success, fallback, or exhaustion.</summary>
    public int RetriesUsed { get; set; }

    /// <summary>
    /// 1-based attempt index on which the provider's reasoning/thinking mode was disabled,
    /// or null when thinking was never disabled (including providers without a thinking mode).
    /// </summary>
    public int? ThinkingDisabledOnAttempt { get; set; }

    /// <summary>Whether the last attempt was served by the fallback provider.</summary>
    public bool FallbackUsed { get; set; }

    /// <summary>Fallback provider type name, when one is configured.</summary>
    public string? FallbackProviderName { get; set; }

    /// <summary>Wall-clock duration of each primary attempt, in attempt order.</summary>
    public List<double> PrimaryAttemptSeconds { get; set; } = [];

    /// <summary>Total wall-clock duration of the whole call, including retry delays.</summary>
    public double TotalSeconds { get; set; }

    /// <summary>Failure reason when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Result of a daily-advice generation call: the generated text plus its telemetry.
/// </summary>
public sealed class DailyAdviceGeneration
{
    /// <summary>The generated outline or column text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Telemetry for the call that produced <see cref="Text"/>.</summary>
    public DailyAdviceGenerationReport? Report { get; set; }
}

/// <summary>
/// Selections accepted by the admin daily-advice recovery tool, alongside the language
/// codes in <see cref="SupportedLanguages.Codes"/>.
/// </summary>
public static class DailyAdviceRecoveryTarget
{
    /// <summary>
    /// Sentinel selection that regenerates the outline and every language column. This is the
    /// recovery for a night where the outline itself failed and therefore no columns exist.
    /// Also used as the <see cref="DailyAdviceGenerationReport.Kind"/> of the outline call, so
    /// the report a reader sees and the recovery option they pick can never drift apart.
    /// </summary>
    public const string Outline = "outline";

    /// <summary>
    /// Whether a report describes the shared outline call rather than a per-language column.
    /// </summary>
    public static bool IsOutline(DailyAdviceGenerationReport? report)
        => report is not null
           && string.Equals(report.Kind, Outline, StringComparison.OrdinalIgnoreCase);
}
