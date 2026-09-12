using CompanioNation.Shared;
using System.Net;
using System.Text;


namespace CompanioNationAPI
{
    public class MaintenanceEventService : BackgroundService
    {
        private readonly Database _database; // Inject the Database class
        private readonly CompanioNita _companioNita;


        public MaintenanceEventService(Database database, CompanioNita companioNita)
        {
            _database = database;
            _companioNita = companioNita;
        }

        private const int DailyMaintenanceHourUtc = 8;
        private const int MaxMaintenanceJitterSeconds = 300;

        /// <summary>
        /// Returns the next future 8:00 UTC daily maintenance slot plus a randomized
        /// 0-300s offset so multiple deployments (e.g. staging and production) don't hit
        /// the AI provider at the same instant and trip shared rate limits.
        /// </summary>
        internal static DateTime GetNextScheduledRun(DateTime nowUtc)
        {
            DateTime baseSlot = nowUtc.Date.AddHours(DailyMaintenanceHourUtc);
            if (baseSlot <= nowUtc)
                baseSlot = baseSlot.AddDays(1);
            return ApplyMaintenanceJitter(baseSlot);
        }

        /// <summary>
        /// Returns tomorrow's 8:00 UTC slot with a fresh jitter offset. Used after a
        /// catch-up run so today's regular slot is skipped regardless of the current time.
        /// </summary>
        private static DateTime GetTomorrowsSlot()
            => ApplyMaintenanceJitter(DateTime.UtcNow.Date.AddDays(1).AddHours(DailyMaintenanceHourUtc));

        private static DateTime ApplyMaintenanceJitter(DateTime baseSlotUtc)
            => baseSlotUtc.AddSeconds(Random.Shared.Next(0, MaxMaintenanceJitterSeconds + 1));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Set next run to be at 8am GMT (around midnight Pacific Time) plus a
                // randomized 0-300s offset so the daily maintenance doesn't always fire
                // at the exact same instant across environments.
                DateTime now = DateTime.UtcNow;
                DateTime nextRun = GetNextScheduledRun(now);

                Settings? settings = await _database.GetAllSettingsAsync();
                if (settings == null)
                {
                    await ErrorLog.LogErrorMessage("DAILY MAINTENANCE: Could not fetch database settings. Will retry at next scheduled run.");
                }
                else if (settings.LastMaintenanceRun < now.AddDays(-1))
                {
                    await ErrorLog.LogInfo("Last Daily Maintenance Was over 24 hours ago. Running now...");
                    // The last maintenance run was over 24 hours ago, so run it now
                    await RunDailyMaintenanceAsync(stoppingToken);
                    await ErrorLog.LogInfo("Daily Maintenance Successfully Completed!");
                    // Skip today's slot (we just ran); schedule tomorrow's with a fresh offset
                    nextRun = GetTomorrowsSlot();
                }

                // Set up the regular daily run
                while (!stoppingToken.IsCancellationRequested)
                {
                    now = DateTime.UtcNow; // Refresh so delay is accurate on every iteration
                    TimeSpan delay = nextRun - now;
                    if (delay < TimeSpan.Zero) delay += TimeSpan.FromHours(24);

                    //DateTime nextRun = DateTime.UtcNow.AddSeconds(10); // For testing, run in 10 seconds

                    await ErrorLog.LogInfo("MaintenanceEventService: NEXT RUN is at " + nextRun.ToString("GMT yyyy-MM-dd hh:mm:ss tt"));
                    await ErrorLog.LogInfo("Delaying for " + delay.ToString());

                    if (delay.TotalMilliseconds > 0)
                    {
                        try
                        {
                            await Task.Delay(delay, stoppingToken); // Wait until the next run time
                        }
                        catch (OperationCanceledException)
                        {
                            break; // Graceful shutdown (Ctrl-C, Azure restart, etc.)
                        }
                    }
                    if (stoppingToken.IsCancellationRequested) break; // Check for cancellation after delay

                    await RunDailyMaintenanceAsync(stoppingToken);
                    nextRun = GetNextScheduledRun(DateTime.UtcNow); // Next day's slot, fresh offset

                    // Delay by three hours so that we don't have duplicate events on daylight savings time change days
                    // Plus on regular days we don't want to spin through the loop too fast and get duplicate events triggering
                    try
                    {
                        await Task.Delay(new TimeSpan(3, 0, 0), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Graceful shutdown
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown — no need to log, this is expected during app shutdown
            }
        }

        // Method for generating and storing daily advice
        public async Task RunDailyMaintenanceAsync(CancellationToken cancellationToken)
        {
            var reports = new List<DailyAdviceGenerationReport>();
            string? fatalError = null;
            string? housekeepingError = null;
            string warmupStatus = "not run";

            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Housekeeping runs FIRST so it always completes. CompanioNita generation can now
                // take a long time (5 retries with backoff, up to ~30 minutes in the worst case)
                // and previously ran before this — so a slow or failing AI night could delay or
                // skip the cleanup entirely. RunDatabaseMaintenance never throws, but its result
                // was previously discarded, so a failure is now surfaced instead of silent.
                ResponseWrapper<bool> maintenance = await _database.RunDatabaseMaintenance();
                if (!maintenance.IsSuccess)
                {
                    housekeepingError = string.IsNullOrWhiteSpace(maintenance.Message)
                        ? "Database maintenance failed."
                        : maintenance.Message;
                    await ErrorLog.LogErrorMessage("DAILY MAINTENANCE: " + housekeepingError);
                }

                // Warm the AI provider before the batch. Report-only: a dead or cold
                // provider fails fast here (short ping timeouts) and only logs — the real
                // calls below still try the primary provider with its normal retries.
                // Best-effort: a ping failure never aborts the batch, and it deliberately does
                // not count as an ACTION REQUIRED condition either — the generation results
                // below are the source of truth for provider health.
                try
                {
                    ResponseWrapper<bool> warmup = await _companioNita.WarmupAsync(cancellationToken);
                    warmupStatus = warmup.IsSuccess
                        ? (warmup.Data ? "OK (provider reachable)" : "unexpected reply from provider")
                        : $"FAILED: {warmup.Message}";

                    if (!warmup.IsSuccess)
                        await ErrorLog.LogErrorMessage($"DAILY MAINTENANCE: Warmup ping failed: {warmup.Message}");
                }
                catch (Exception ex)
                {
                    warmupStatus = $"FAILED: {ex.Message}";
                    await ErrorLog.LogErrorException(ex, "DAILY MAINTENANCE: Warmup ping failed.");
                }

                reports = await GenerateAndStoreAllDailyAdviceAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Log and swallow so the BackgroundService loop continues to the next scheduled run.
                // Re-throwing here would terminate the hosted service entirely.
                fatalError = ex.Message;
                await ErrorLog.LogErrorException(ex, "Error during daily maintenance.");
            }
            finally
            {
                // Always report the night's outcome — success, partial, or total failure.
                // SendNightlyDailyAdviceReportAsync never throws, so this cannot mask an error.
                (SiteStats? siteStats, string? siteStatsError) = await TryGetSiteStatsAsync();
                await SendNightlyDailyAdviceReportAsync(
                    reports, fatalError, housekeepingError, warmupStatus, siteStats, siteStatsError);
            }
        }

        /// <summary>
        /// Fetches the site stats for the report. Best-effort: a stats failure is reported as a
        /// note in the email, but never treated as a maintenance problem and never allowed to
        /// prevent the report from being sent.
        /// </summary>
        private async Task<(SiteStats? Stats, string? Error)> TryGetSiteStatsAsync()
        {
            try
            {
                ResponseWrapper<SiteStats> result = await _database.GetSiteStatsForMaintenanceAsync();
                if (result.IsSuccess)
                    return (result.Data, null);

                string error = string.IsNullOrWhiteSpace(result.Message)
                    ? "Site statistics could not be generated."
                    : result.Message;
                return (null, error);
            }
            catch (Exception ex)
            {
                await ErrorLog.LogErrorException(ex, "Failed to gather site stats for the nightly report.");
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// Runs only the database housekeeping half of the nightly job: recomputes each user's
        /// rolling average rating and clears invalid negative rankings (see cn_maintenance).
        /// Backs the admin "Run Database Housekeeping" action.
        /// It deliberately does NOT generate or store any CompanioNita advice: that is what
        /// regenerate/recovery does, which can target a single language and updates the existing
        /// advice row instead of creating a duplicate day. Running the whole nightly job from the
        /// admin UI would also fire a nightly report email as a side effect.
        /// </summary>
        public async Task<ResponseWrapper<string>> RunDatabaseHousekeepingAsync()
        {
            ResponseWrapper<bool> maintenance = await _database.RunDatabaseMaintenance();
            if (!maintenance.IsSuccess)
            {
                string reason = string.IsNullOrWhiteSpace(maintenance.Message)
                    ? "Database housekeeping failed."
                    : maintenance.Message;
                await ErrorLog.LogErrorMessage("DAILY MAINTENANCE: " + reason);
                return ResponseWrapper<string>.Fail(maintenance.ErrorCode, reason);
            }

            return ResponseWrapper<string>.Success(string.Empty,
                "Database housekeeping completed: average user ratings recomputed and invalid negative rankings cleared. No advice content was changed.");
        }

        /// <summary>
        /// Generates today's English outline and expands it into every supported language,
        /// persisting the repetition-avoidance history, each per-language daily advice value,
        /// and the shared advice row. Returns the telemetry for every AI call made.
        /// Shared by the nightly job and the admin recovery path so both produce identical
        /// results; callers decide how to report the outcome. Database housekeeping is NOT part
        /// of this method — the nightly job runs it up front, and a manual recovery must not
        /// trigger it. Throws on unexpected failures.
        /// </summary>
        private async Task<List<DailyAdviceGenerationReport>> GenerateAndStoreAllDailyAdviceAsync(
            CancellationToken cancellationToken)
        {
            var reports = new List<DailyAdviceGenerationReport>();

            Settings? settings = await _database.GetAllSettingsAsync("en");
            if (settings == null)
                throw new InvalidOperationException("Could not fetch database settings during maintenance run.");

            string previousOutlines = settings.PreviousDailyAdvice ?? "";

            // Get the most recent user interactions for reference in creating an advice column
            string messages = await _database.GetRecentMessages();

            // 1) Generate a single English outline — the only call that carries the history + recent messages.
            ResponseWrapper<DailyAdviceGeneration> outlineResponse =
                await _companioNita.GenerateDailyAdviceOutlineWithReportAsync(previousOutlines, messages, cancellationToken);
            CollectReport(reports, outlineResponse);

            if (!outlineResponse.IsSuccess || string.IsNullOrWhiteSpace(outlineResponse.Data?.Text))
            {
                // A failed outline aborts the entire run: every language column is an expansion
                // of it, so continuing could only produce more failures. Nothing is stored, so
                // readers keep seeing the previous day's column.
                await ErrorLog.LogErrorMessage($"DAILY MAINTENANCE: Failed to generate advice outline: {outlineResponse.Message} (ErrorCode: {outlineResponse.ErrorCode})");
                return reports;
            }

            string outline = outlineResponse.Data.Text.Trim();

            // 2) Store only the outline in the repetition-avoidance history.
            string newPreviousOutlines = (outline + "\n" + previousOutlines).Trim();
            const int maxPreviousAdviceLength = 65535;
            if (newPreviousOutlines.Length > maxPreviousAdviceLength)
            {
                newPreviousOutlines = newPreviousOutlines[..maxPreviousAdviceLength];
            }

            // Persist the history + maintenance timestamp once (no daily-advice value here).
            await _database.SaveAllSettingsAsync(new Settings
            {
                PreviousDailyAdvice = newPreviousOutlines,
                LastMaintenanceRun = DateTime.UtcNow
            }, "en");

            // 3) Expand the outline into a full column for every supported language.
            int adviceId = 0;
            foreach (string languageCode in SupportedLanguages.Codes)
            {
                if (cancellationToken.IsCancellationRequested) break;

                ResponseWrapper<DailyAdviceGeneration> columnResponse =
                    await _companioNita.GenerateDailyAdviceFromOutlineWithReportAsync(outline, languageCode, cancellationToken);
                CollectReport(reports, columnResponse);

                if (!columnResponse.IsSuccess || string.IsNullOrWhiteSpace(columnResponse.Data?.Text))
                {
                    await ErrorLog.LogErrorMessage($"DAILY MAINTENANCE: Failed to generate daily advice for '{languageCode}': {columnResponse.Message} (ErrorCode: {columnResponse.ErrorCode})");
                    continue; // Missing language falls back to English on read.
                }

                string dailyAdvice = AppendUtcWatermark(columnResponse.Data.Text, DateTime.UtcNow);

                await _database.SaveAllSettingsAsync(new Settings { DailyAdvice = dailyAdvice }, languageCode);

                var saved = await _database.SaveCompanionitaAdvice(languageCode, dailyAdvice, outline, adviceId == 0 ? (int?)null : adviceId);
                if (saved.IsSuccess && saved.Data > 0)
                {
                    adviceId = saved.Data;
                }
            }

            return reports;
        }

        /// <summary>
        /// Captures the telemetry carried by a generation response. The report survives a
        /// failed call, so a language that produced no column is still fully described.
        /// </summary>
        private static void CollectReport(
            List<DailyAdviceGenerationReport> reports, ResponseWrapper<DailyAdviceGeneration> response)
        {
            if (response.Data?.Report is { } report)
                reports.Add(report);
        }

        /// <summary>
        /// Appends a faint, right-aligned UTC generation timestamp to the end of a daily
        /// advice column. The timestamp is intentionally low-emphasis so it reads as a
        /// watermark rather than part of the editorial copy.
        /// </summary>
        internal static string AppendUtcWatermark(string adviceHtml, DateTime generatedAtUtc)
        {
            string stamp = generatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
            return adviceHtml
                + $"<p class=\"companionita-note\" style=\"opacity:0.55;font-size:0.72rem;text-align:right;margin-top:1.2em;\">{stamp} UTC</p>";
        }

        private const string AdminReportRecipient = "errors@companionation.com";

        /// <summary>
        /// Admin recovery entry point. Accepts either the
        /// <see cref="DailyAdviceRecoveryTarget.Outline"/> sentinel — which regenerates the
        /// outline and every language, the recovery for a night where the outline failed and no
        /// columns exist at all — or a language code, which regenerates just that column from
        /// the stored outline.
        /// </summary>
        public async Task<ResponseWrapper<string>> RegenerateDailyAdviceAsync(
            string selection, CancellationToken cancellationToken)
        {
            string normalized = (selection ?? string.Empty).Trim().ToLowerInvariant();

            if (normalized == DailyAdviceRecoveryTarget.Outline)
                return await RegenerateOutlineAndAllLanguagesAsync(cancellationToken);

            return await RegenerateDailyAdviceForLanguageAsync(normalized, cancellationToken);
        }

        /// <summary>
        /// Regenerates the outline and every language column, mirroring the nightly job exactly.
        /// Used when the outline itself failed overnight, leaving no columns for the day.
        /// Note this writes a new advice row, so running it when today's columns already exist
        /// creates a duplicate day — the same caveat as the manual "Run Daily Maintenance" tool.
        /// </summary>
        private async Task<ResponseWrapper<string>> RegenerateOutlineAndAllLanguagesAsync(
            CancellationToken cancellationToken)
        {
            List<DailyAdviceGenerationReport> reports;
            try
            {
                reports = await GenerateAndStoreAllDailyAdviceAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await ErrorLog.LogErrorException(ex, "Error regenerating the daily advice outline.");
                return ResponseWrapper<string>.Fail(ErrorCodes.UnknownError,
                    "An error occurred while regenerating the outline and language columns.");
            }

            if (reports.Any(r => IsOutline(r) && !r.Succeeded))
            {
                return ResponseWrapper<string>.Fail(ErrorCodes.UnknownError,
                    "The outline could not be generated, so no language columns were produced. Nothing was changed — please try again.");
            }

            int total = SupportedLanguages.Codes.Length;
            int succeeded = reports.Count(r => !IsOutline(r) && r.Succeeded);
            int failed = reports.Count(r => !IsOutline(r) && !r.Succeeded);

            string message = $"Regenerated the outline and {succeeded}/{total} languages." +
                (failed > 0 ? $" {failed} language(s) still failed — check the admin log for details." : string.Empty);

            return ResponseWrapper<string>.Success(string.Empty, message);
        }

        /// <summary>
        /// Regenerates the daily advice column for a single language from the already-stored
        /// outline, overwriting only that language's row and per-language daily-advice setting.
        /// This is the admin recovery path for a language the nightly batch failed to produce,
        /// so it deliberately leaves every other language untouched.
        /// </summary>
        public async Task<ResponseWrapper<string>> RegenerateDailyAdviceForLanguageAsync(
            string languageCode, CancellationToken cancellationToken)
        {
            string code = (languageCode ?? string.Empty).Trim().ToLowerInvariant();
            if (!SupportedLanguages.Codes.Contains(code))
            {
                return ResponseWrapper<string>.Fail(ErrorCodes.InvalidInput,
                    $"'{languageCode}' is not a supported language. Valid values: {string.Join(", ", SupportedLanguages.Codes)}, or '{DailyAdviceRecoveryTarget.Outline}' for the outline and all languages.");
            }

            ResponseWrapper<(int AdviceId, DateTime DateCreated, string Outline)> context = await _database.GetLatestAdviceContextAsync();
            if (!context.IsSuccess)
                return ResponseWrapper<string>.Fail(context.ErrorCode, context.Message);

            int adviceId = context.Data.AdviceId;
            string outline = (context.Data.Outline ?? string.Empty).Trim();
            if (adviceId <= 0 || string.IsNullOrWhiteSpace(outline))
            {
                return ResponseWrapper<string>.Fail(ErrorCodes.ResourceNotFound,
                    "No stored outline was found to build the column from. Use 'Outline + all languages' to regenerate the whole day first.");
            }

            ResponseWrapper<DailyAdviceGeneration> columnResponse =
                await _companioNita.GenerateDailyAdviceFromOutlineWithReportAsync(outline, code, cancellationToken);
            if (!columnResponse.IsSuccess || string.IsNullOrWhiteSpace(columnResponse.Data?.Text))
                return ResponseWrapper<string>.Fail(columnResponse.ErrorCode, columnResponse.Message);

            string dailyAdvice = AppendUtcWatermark(columnResponse.Data.Text, DateTime.UtcNow);
            await _database.SaveAllSettingsAsync(new Settings { DailyAdvice = dailyAdvice }, code);

            // Reuse the existing advice_id so this language is updated in place. Passing null
            // here would make the save proc start a NEW day, leaving an empty English column
            // behind and duplicating today's advice on the site.
            ResponseWrapper<int> saved = await _database.SaveCompanionitaAdvice(code, dailyAdvice, outline, adviceId);
            if (!saved.IsSuccess || saved.Data <= 0)
            {
                return ResponseWrapper<string>.Fail(saved.ErrorCode,
                    string.IsNullOrWhiteSpace(saved.Message)
                        ? $"The column was generated but could not be saved for '{code}'."
                        : saved.Message);
            }

            DailyAdviceGenerationReport? report = columnResponse.Data.Report;
            string detail = report is null
                ? string.Empty
                : $" via {ProviderLabel(report)} using {DescribeTries(report)} in {report.TotalSeconds:0.0}s";

            // The column is built from whichever day still has a stored outline. Normally that
            // is today, but after a failed outline it is the PREVIOUS day — so say which day was
            // rewritten rather than letting the admin assume they fixed today.
            bool updatedToday = context.Data.DateCreated.Date == DateTime.UtcNow.Date;
            string dayNote = updatedToday
                ? $"Advice ID {saved.Data}."
                : $"Advice ID {saved.Data} — WARNING: this updated the advice row dated " +
                  $"{context.Data.DateCreated:yyyy-MM-dd} UTC, not today's. Today has no column yet; " +
                  $"use '{DailyAdviceRecoveryTarget.Outline}' to generate the whole day.";

            return ResponseWrapper<string>.Success(dailyAdvice,
                $"Regenerated '{code}' ({SupportedLanguages.NativeName(code)}){detail}. {dayNote}");
        }

        /// <summary>
        /// Sends the single consolidated nightly report. Always emailed (success or failure)
        /// and always names what actually happened: which provider answered each call, whether
        /// the provider's thinking mode had to be disabled, how many tries it took, and the
        /// timings. Any language that failed outright is called out in an unmissable
        /// ACTION REQUIRED block, and the subject carries the matching prefix, so a partial
        /// night cannot slip past unnoticed.
        /// Never throws — the report must not be able to break the maintenance batch. It also
        /// deliberately takes no cancellation token: the report still has to go out when the
        /// host is shutting down mid-run.
        /// </summary>
        private async Task SendNightlyDailyAdviceReportAsync(
            List<DailyAdviceGenerationReport> reports, string? fatalError, string? housekeepingError,
            string warmupStatus, SiteStats? siteStats, string? siteStatsError)
        {
            try
            {
                List<DailyAdviceGenerationReport> failedLanguages = reports
                    .Where(r => !IsOutline(r) && !r.Succeeded)
                    .ToList();
                bool outlineFailed = reports.Any(r => IsOutline(r) && !r.Succeeded);
                int languagesAttempted = reports.Count(r => !IsOutline(r));
                int languagesSucceeded = languagesAttempted - failedLanguages.Count;

                string subject = BuildNightlyReportSubject(
                    languagesSucceeded, failedLanguages.Count, outlineFailed, fatalError, housekeepingError);
                (string textBody, string htmlBody) = BuildNightlyReportBody(
                    reports, failedLanguages, outlineFailed, fatalError, housekeepingError, warmupStatus,
                    siteStats, siteStatsError, languagesSucceeded);

                await Email.SendEmailAsync(AdminReportRecipient, subject, textBody, htmlBody);

                // Durable log line too: the outcome must still be discoverable in the app logs
                // if the mail is filtered or lands in a spam folder.
                await ErrorLog.LogInfo("DAILY MAINTENANCE REPORT sent: " + subject);
            }
            catch (Exception ex)
            {
                await ErrorLog.LogErrorException(ex, "Failed to send nightly daily advice report.");
            }
        }

        private static bool IsOutline(DailyAdviceGenerationReport report)
            => DailyAdviceRecoveryTarget.IsOutline(report);

        /// <summary>
        /// Builds the subject line. The [ACTION REQUIRED] prefix is the only reliably rendered
        /// attention signal across mail clients — the Importance/X-Priority headers are
        /// advisory and are ignored by most webmail, so it is not relied upon here.
        /// The deployment tag leads the subject so production, staging and dev reports are
        /// distinguishable in a shared mailbox before the text is read.
        /// </summary>
        private static string BuildNightlyReportSubject(
            int languagesSucceeded, int languagesFailed, bool outlineFailed, string? fatalError,
            string? housekeepingError)
        {
            int total = SupportedLanguages.Codes.Length;
            bool needsAction = fatalError is not null || housekeepingError is not null
                || outlineFailed || languagesFailed > 0;
            string environment = GetServerEnvironmentTag();

            string status = outlineFailed || fatalError is not null
                ? "no columns generated"
                : $"{languagesSucceeded}/{total} languages OK";

            return needsAction
                ? $"📰 [{environment}] [ACTION REQUIRED] CompanioNita nightly advice report — {status}"
                : $"📰 [{environment}] CompanioNita nightly advice report — {status}";
        }

        /// <summary>
        /// Short tag naming the deployment that produced the report, so production, staging and
        /// local runs are distinguishable at a glance. SLOT_HOSTNAME is the slot-sticky Azure
        /// app setting (e.g. "production", "staging"); ASPNETCORE_ENVIRONMENT covers local runs.
        /// </summary>
        private static string GetServerEnvironmentTag()
        {
            string? slot = Environment.GetEnvironmentVariable("SLOT_HOSTNAME");
            if (!string.IsNullOrWhiteSpace(slot))
                return slot.Trim().ToUpperInvariant();

            string? aspnetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (!string.IsNullOrWhiteSpace(aspnetEnvironment))
                return aspnetEnvironment.Trim().ToUpperInvariant();

            return "UNKNOWN";
        }

        private static (string Text, string Html) BuildNightlyReportBody(
            List<DailyAdviceGenerationReport> reports,
            List<DailyAdviceGenerationReport> failedLanguages,
            bool outlineFailed,
            string? fatalError,
            string? housekeepingError,
            string warmupStatus,
            SiteStats? siteStats,
            string? siteStatsError,
            int languagesSucceeded)
        {
            int total = SupportedLanguages.Codes.Length;
            bool needsAction = fatalError is not null || housekeepingError is not null
                || outlineFailed || failedLanguages.Count > 0;
            string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            var text = new StringBuilder();
            text.AppendLine($"CompanioNita nightly daily advice — {stamp} UTC on {GetServerEnvironmentTag()}");
            text.AppendLine();

            if (needsAction)
            {
                text.AppendLine($"*** ACTION REQUIRED — {languagesSucceeded}/{total} languages generated. ***");
                text.AppendLine();
                if (fatalError is not null)
                    text.AppendLine($"Nightly maintenance aborted: {fatalError}");
                if (housekeepingError is not null)
                    text.AppendLine($"Database housekeeping FAILED: {housekeepingError}");
                if (outlineFailed)
                    text.AppendLine("English outline FAILED — no columns were generated for any language.");
                foreach (DailyAdviceGenerationReport report in failedLanguages)
                {
                    text.AppendLine($"{LanguageLabel(report)} FAILED completely" +
                        (string.IsNullOrWhiteSpace(report.Error) ? "" : $": {report.Error}") +
                        " — readers of this language fall back to English.");
                }
                text.AppendLine();
                text.AppendLine("Fix it: Admin -> Daily Advice Recovery -> choose the language -> Regenerate.");
                text.AppendLine();
            }
            else
            {
                text.AppendLine($"All {languagesSucceeded}/{total} languages generated successfully.");
                text.AppendLine();
            }

            // Other nightly operations, so the whole job is visible in one place.
            text.AppendLine("Other nightly operations:");
            text.AppendLine($"  Database housekeeping: {(housekeepingError is null ? "OK" : "FAILED: " + housekeepingError)}");
            text.AppendLine($"  Provider warmup ping: {warmupStatus}");
            text.AppendLine();

            text.AppendLine("Generation detail:");
            foreach (DailyAdviceGenerationReport report in reports)
            {
                text.AppendLine(
                    $"  {LanguageLabel(report)}: {(report.Succeeded ? "OK" : "FAILED")}" +
                    $" | provider: {ProviderLabel(report)}" +
                    $" | tries: {DescribeTries(report)}" +
                    $" | thinking: {ThinkingLabel(report)}" +
                    $" | per-attempt: {AttemptTimesLabel(report)}" +
                    $" | total: {report.TotalSeconds:0.0}s" +
                    (report.Succeeded ? "" : $" | error: {report.Error}"));
            }

            text.AppendLine();
            text.AppendLine("Site stats:");
            if (siteStats is null)
            {
                text.AppendLine($"  Unavailable: {siteStatsError}");
            }
            else
            {
                foreach ((string label, string value) in BuildSiteStatRows(siteStats))
                    text.AppendLine($"  {label}: {value}");
            }

            var html = new StringBuilder();
            html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222\">");
            html.Append($"<p style=\"margin:0 0 14px\">CompanioNita nightly daily advice — <strong>{stamp} UTC</strong> on <strong>{Encode(GetServerEnvironmentTag())}</strong></p>");

            if (needsAction)
            {
                html.Append("<div style=\"border:3px solid #c00000;background:#fff2f2;padding:14px;margin:0 0 18px 0\">");
                html.Append("<p style=\"margin:0 0 8px;font-size:16px;font-weight:bold;color:#c00000\">⚠️ ACTION REQUIRED — manual follow-up needed</p>");
                html.Append("<ul style=\"margin:0 0 10px 20px;padding:0;color:#c00000;font-weight:bold\">");
                if (fatalError is not null)
                    html.Append($"<li>Nightly maintenance aborted: {Encode(fatalError)}</li>");
                if (housekeepingError is not null)
                    html.Append($"<li>Database housekeeping FAILED: {Encode(housekeepingError)}</li>");
                if (outlineFailed)
                    html.Append("<li>English outline FAILED — NO columns were generated for any language. Readers still see the previous column.</li>");
                foreach (DailyAdviceGenerationReport report in failedLanguages)
                {
                    html.Append($"<li>{Encode(LanguageLabel(report))} FAILED completely" +
                        (string.IsNullOrWhiteSpace(report.Error) ? "" : $": {Encode(report.Error)}") +
                        " — readers of this language fall back to English.</li>");
                }
                html.Append("</ul>");
                html.Append("<p style=\"margin:0;color:#c00000\">Fix it: <strong>Admin → Daily Advice Recovery</strong> → choose the language → <strong>Regenerate</strong>.</p>");
                html.Append("</div>");
            }
            else
            {
                html.Append("<div style=\"border-left:4px solid #2e7d32;background:#f1f8f1;padding:12px;margin:0 0 18px 0\">");
                html.Append($"<p style=\"margin:0;font-weight:bold;color:#2e7d32\">✅ All {languagesSucceeded}/{total} languages generated successfully.</p>");
                html.Append("</div>");
            }

            html.Append("<h3 style=\"margin:0 0 8px\">Generation detail</h3>");
            html.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px\">");
            html.Append("<tr style=\"background:#f0f0f0\">");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Call</th>");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Result</th>");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Provider used</th>");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Tries</th>");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Thinking</th>");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Per-attempt</th>");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Total</th>");
            html.Append("</tr>");

            foreach (DailyAdviceGenerationReport report in reports)
            {
                string resultCell = report.Succeeded
                    ? "OK"
                    : "<strong style=\"color:#c00000\">FAILED</strong>";
                string rowBackground = report.Succeeded ? "#ffffff" : "#ffecec";

                html.Append($"<tr style=\"background:{rowBackground}\">");
                html.Append($"<td style=\"border:1px solid #ccc\">{Encode(LanguageLabel(report))}</td>");
                html.Append($"<td style=\"border:1px solid #ccc\">{resultCell}</td>");
                html.Append($"<td style=\"border:1px solid #ccc\">{Encode(ProviderLabel(report))}</td>");
                html.Append($"<td style=\"border:1px solid #ccc\">{Encode(DescribeTries(report))}</td>");
                html.Append($"<td style=\"border:1px solid #ccc\">{Encode(ThinkingLabel(report))}</td>");
                html.Append($"<td style=\"border:1px solid #ccc\">{Encode(AttemptTimesLabel(report))}</td>");
                html.Append($"<td style=\"border:1px solid #ccc\">{report.TotalSeconds:0.0}s</td>");
                html.Append("</tr>");
            }

            html.Append("</table>");

            // Other nightly operations.
            html.Append("<h3 style=\"margin:18px 0 8px\">Other nightly operations</h3>");
            html.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px\">");
            html.Append("<tr style=\"background:#f0f0f0\">");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Operation</th>");
            html.Append("<th align=\"left\" style=\"border:1px solid #ccc\">Result</th>");
            html.Append("</tr>");

            string housekeepingCell = housekeepingError is null
                ? "OK"
                : $"<strong style=\"color:#c00000\">FAILED</strong>: {Encode(housekeepingError)}";
            html.Append("<tr>");
            html.Append("<td style=\"border:1px solid #ccc\">Database housekeeping</td>");
            html.Append($"<td style=\"border:1px solid #ccc\">{housekeepingCell}</td>");
            html.Append("</tr>");

            html.Append("<tr>");
            html.Append("<td style=\"border:1px solid #ccc\">Provider warmup ping</td>");
            html.Append($"<td style=\"border:1px solid #ccc\">{Encode(warmupStatus)}</td>");
            html.Append("</tr>");
            html.Append("</table>");

            // Site stats — the same figures as the admin dashboard's Site Statistics view.
            html.Append("<h3 style=\"margin:18px 0 8px\">Site stats</h3>");
            if (siteStats is null)
            {
                html.Append($"<p style=\"margin:0;color:#a00000\">Unavailable: {Encode(siteStatsError ?? "unknown error")}</p>");
            }
            else
            {
                html.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px\">");
                foreach ((string label, string value) in BuildSiteStatRows(siteStats))
                {
                    html.Append("<tr>");
                    html.Append($"<td style=\"border:1px solid #ccc;background:#fafafa\">{Encode(label)}</td>");
                    html.Append($"<td style=\"border:1px solid #ccc\"><strong>{Encode(value)}</strong></td>");
                    html.Append("</tr>");
                }
                html.Append("</table>");
            }

            html.Append("</div>");

            return (text.ToString(), html.ToString());
        }

        /// <summary>
        /// Flattens the site stats into label/value rows so the plain-text and HTML bodies always
        /// present the same figures in the same order. Mirrors the headline-total and snapshot
        /// sections of the admin Site Statistics view; the full 30-day/month/year series stays in
        /// admin rather than bloating the daily email, except for a compact recent-signups trend.
        /// </summary>
        private static List<(string Label, string Value)> BuildSiteStatRows(SiteStats stats)
        {
            var rows = new List<(string, string)>
            {
                ("Total users", stats.TotalUsers.ToString()),
                ("Verified users", stats.VerifiedUsers.ToString()),
                ("Active subscribers", stats.UsersWithActiveSubscription.ToString()),
                ("Administrators", stats.Administrators.ToString()),
                ("Muted users", stats.MutedUsers.ToString()),
                ("Users with photos", stats.UsersWithPhotos.ToString()),
                ("Total photos", stats.TotalPhotos.ToString()),
                ("Total messages", stats.TotalMessages.ToString()),
                ("Total connections", stats.TotalConnections.ToString()),
                ("New signups yesterday", stats.SignupsYesterday.ToString()),
                ("New signups, previous 7 days", stats.SignupsLast7Days.ToString()),
                ("New signups, previous 30 days", stats.SignupsLast30Days.ToString()),
                ("Active users yesterday", stats.ActiveYesterday.ToString()),
                ("Active users, previous 7 days", stats.ActiveLast7Days.ToString()),
                ("Active users, previous 30 days", stats.ActiveLast30Days.ToString())
            };

            string trend = FormatRecentSignups(stats.SignupsByDay, 7);
            if (trend.Length > 0)
                rows.Add(("Signups by day, last 7 complete days", trend));

            return rows;
        }

        /// <summary>
        /// Renders the last few days of signups as a compact inline trend (MM-dd: n), so the
        /// email shows the shape of the week rather than only the totals.
        /// </summary>
        private static string FormatRecentSignups(List<StatBucket> signupsByDay, int days)
        {
            if (signupsByDay is null || signupsByDay.Count == 0)
                return string.Empty;

            return string.Join(" | ", signupsByDay
                .TakeLast(days)
                .Select(bucket =>
                {
                    string label = bucket.Label.Length >= 5 ? bucket.Label[^5..] : bucket.Label;
                    return $"{label}: {bucket.Count}";
                }));
        }

        private static string Encode(string value) => WebUtility.HtmlEncode(value);

        private static string LanguageLabel(DailyAdviceGenerationReport report)
            => IsOutline(report)
                ? "English outline"
                : $"{SupportedLanguages.NativeName(report.Kind)} ({report.Kind})";

        /// <summary>
        /// Names the provider that actually served the call: the fallback when it was used,
        /// and the primary when it recovered on its own.
        /// </summary>
        private static string ProviderLabel(DailyAdviceGenerationReport report)
        {
            string primary = string.IsNullOrWhiteSpace(report.PrimaryProviderName) ? "unknown" : report.PrimaryProviderName;
            string fallback = report.FallbackProviderName ?? "fallback";

            if (report.FallbackUsed)
                return report.PrimaryAttemptsMade > 0
                    ? $"{primary} → {fallback} (fallback)"
                    : $"{fallback} (circuit breaker open)";

            return primary;
        }

        private static string DescribeTries(DailyAdviceGenerationReport report)
        {
            if (report.PrimaryAttemptsMade <= 0)
                return report.FallbackUsed ? "fallback only" : "none";

            return report.PrimaryAttemptsMade == 1 ? "1 try" : $"{report.PrimaryAttemptsMade} tries";
        }

        private static string ThinkingLabel(DailyAdviceGenerationReport report)
            => report.ThinkingDisabledOnAttempt is int attempt
                ? $"disabled on try {attempt}"
                : "not needed";

        private static string AttemptTimesLabel(DailyAdviceGenerationReport report)
            => report.PrimaryAttemptSeconds.Count == 0
                ? "—"
                : string.Join(" → ", report.PrimaryAttemptSeconds.Select(seconds => seconds.ToString("0.0") + "s"));





    }
}
