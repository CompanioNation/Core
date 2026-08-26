using CompanioNation.Shared;


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
            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                Settings? settings = await _database.GetAllSettingsAsync("en");
                if (settings == null)
                {
                    await ErrorLog.LogErrorMessage("DAILY MAINTENANCE: Could not fetch database settings during maintenance run.");
                    return;
                }

                string previousOutlines = settings.PreviousDailyAdvice ?? "";

                // Get the most recent user interactions for reference in creating an advice column
                string messages = await _database.GetRecentMessages();

                // Warm the AI provider before the batch. Report-only: a dead or cold
                // provider fails fast here (short ping timeouts) and only logs — the real
                // calls below still try the primary provider with its normal retries.
                // Best-effort: a ping failure never aborts the batch.
                try
                {
                    await _companioNita.WarmupAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await ErrorLog.LogErrorException(ex, "DAILY MAINTENANCE: Warmup ping failed.");
                }

                // 1) Generate a single English outline — the only call that carries the history + recent messages.
                ResponseWrapper<string> outlineResponse = await _companioNita.GenerateDailyAdviceOutlineAsync(previousOutlines, messages, cancellationToken);
                if (!outlineResponse.IsSuccess || string.IsNullOrWhiteSpace(outlineResponse.Data))
                {
                    await ErrorLog.LogErrorMessage($"DAILY MAINTENANCE: Failed to generate advice outline: {outlineResponse.Message} (ErrorCode: {outlineResponse.ErrorCode})");
                    await NotifyAdminOfAdviceFailure("outline", outlineResponse.Message);
                    return;
                }

                string outline = outlineResponse.Data.Trim();

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

                    ResponseWrapper<string> columnResponse = await _companioNita.GenerateDailyAdviceFromOutlineAsync(outline, languageCode, cancellationToken);
                    if (!columnResponse.IsSuccess || string.IsNullOrWhiteSpace(columnResponse.Data))
                    {
                        await ErrorLog.LogErrorMessage($"DAILY MAINTENANCE: Failed to generate daily advice for '{languageCode}': {columnResponse.Message} (ErrorCode: {columnResponse.ErrorCode})");
                        await NotifyAdminOfAdviceFailure(languageCode, columnResponse.Message);
                        continue; // Missing language falls back to English on read.
                    }

                    string dailyAdvice = columnResponse.Data;

                    await _database.SaveAllSettingsAsync(new Settings { DailyAdvice = dailyAdvice }, languageCode);

                    var saved = await _database.SaveCompanionitaAdvice(languageCode, dailyAdvice, outline, adviceId == 0 ? (int?)null : adviceId);
                    if (saved.IsSuccess && saved.Data > 0)
                    {
                        adviceId = saved.Data;
                    }
                }

                // Run the database maintenance function
                await _database.RunDatabaseMaintenance();
            }
            catch (Exception ex)
            {
                // Log and swallow so the BackgroundService loop continues to the next scheduled run.
                // Re-throwing here would terminate the hosted service entirely.
                await ErrorLog.LogErrorException(ex, "Error during daily maintenance.");
            }
        }

        private async Task NotifyAdminOfAdviceFailure(string languageCode, string errorMessage)
        {
            try
            {
                string subject = $"⚠️ CompanioNita daily advice failure ({languageCode})";
                string body = $"CompanioNita failed to generate daily advice for language '{languageCode}'.\n\nError: {errorMessage}\n\nTime (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
                await Email.SendEmailAsync("errors@companionation.com", subject, body, body);
            }
            catch (Exception ex)
            {
                await ErrorLog.LogErrorException(ex, "Failed to send daily advice failure notification.");
            }
        }





    }
}
