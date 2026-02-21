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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Set next run to be at 8am GMT which will be around midnight Pacific Time
            DateTime now = DateTime.UtcNow;
            DateTime nextRun = now.Date.AddHours(8);
            if (nextRun <= now)
            {
                nextRun = nextRun.AddDays(1);
            }

            Settings settings = await _database.GetAllSettingsAsync();
            if (settings == null)
            {
                await ErrorLog.LogErrorMessage("DAILY MAINTENANCE: FATAL ERROR!!! Could not fetch database settings.");
                return;
            }
            if (settings.LastMaintenanceRun < now.AddDays(-1))
            {
                await ErrorLog.LogInfo("Last Daily Maintenance Was over 24 hours ago. Running now...");
                // The last maintenance run was over 24 hours ago, so run it now
                await RunDailyMaintenanceAsync(stoppingToken);
                await ErrorLog.LogInfo("Daily Maintenance Successfully Completed!");
                // Advance nextRun by a day so the scheduled slot later today doesn't fire again
                nextRun = nextRun.AddDays(1);
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
                    await Task.Delay(delay, stoppingToken); // Wait until the next run time
                }
                if (stoppingToken.IsCancellationRequested) break; // Check for cancellation after delay

                await RunDailyMaintenanceAsync(stoppingToken);
                nextRun = nextRun.AddDays(1); // Advance to the next day's slot

                // Delay by three hours so that we don't have duplicate events on daylight savings time change days
                // Plus on regular days we don't want to spin through the loop too fast and get duplicate events triggering
                await Task.Delay(new TimeSpan(3, 0, 0), stoppingToken);
            }
        }

        // Method for generating and storing daily advice
        public async Task RunDailyMaintenanceAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                Settings settings = new Settings();
                settings = await _database.GetAllSettingsAsync();
                settings.PreviousDailyAdvice = settings.DailyAdvice + settings.PreviousDailyAdvice;
                // Truncate the string to a reasonable amount
                if (settings.PreviousDailyAdvice.Length > 66636)
                {
                    settings.PreviousDailyAdvice = settings.PreviousDailyAdvice.Substring(0, 65535);
                }
                // Get the most recent user interactions for reference in creating an advice column
                string messages = await _database.GetRecentMessages();

                ResponseWrapper<string> dailyAdviceResponse = await _companioNita.GenerateDailyAdviceAsync(settings.PreviousDailyAdvice, messages);
                
                // For maintenance tasks, if subscription is required, we should log it but continue
                // (or handle it differently based on your business logic)
                string dailyAdvice;
                if (!dailyAdviceResponse.IsSuccess)
                {
                    await ErrorLog.LogErrorMessage($"Failed to generate daily advice: {dailyAdviceResponse.Message} (ErrorCode: {dailyAdviceResponse.ErrorCode})");
                    dailyAdvice = $"<!-- Daily advice generation failed: {dailyAdviceResponse.Message} -->";
                }
                else
                {
                    dailyAdvice = dailyAdviceResponse.Data;
                }
                
                //Console.WriteLine(dailyAdvice);

                settings.DailyAdvice = dailyAdvice;
                settings.LastMaintenanceRun = DateTime.UtcNow;
                await _database.SaveAllSettingsAsync(settings);


                // Save the daily advice so that we can list all of them which should improve SEO, and be useful and interesting to users
                await _database.SaveCompanionitaAdvice(dailyAdvice);


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





    }
}
