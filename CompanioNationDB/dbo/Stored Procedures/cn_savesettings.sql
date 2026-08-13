CREATE PROCEDURE [dbo].[cn_savesettings]
    @language_code nvarchar(10) = 'en',
    @daily_advice nvarchar(max) = null,  -- Change to nvarchar(max) for proper Unicode support
    @last_maintenance_run datetime = null,
    @previous_daily_advice nvarchar(max) = null
AS
BEGIN
    SET NOCOUNT ON;

    -- Upsert the per-language daily advice
    IF (@daily_advice IS NOT NULL)
    BEGIN
        IF EXISTS (SELECT 1 FROM cn_daily_advice WHERE language_code = @language_code)
            UPDATE cn_daily_advice SET daily_advice = @daily_advice WHERE language_code = @language_code;
        ELSE
            INSERT INTO cn_daily_advice (language_code, daily_advice) VALUES (@language_code, @daily_advice);
    END

    -- Keep the English column in cn_settings as the fallback source
    IF (@language_code = 'en' AND @daily_advice IS NOT NULL)
        UPDATE cn_settings SET daily_advice = @daily_advice;

    -- Singleton metadata (passed only once by the caller)
    IF (@previous_daily_advice IS NOT NULL OR @last_maintenance_run IS NOT NULL)
        UPDATE cn_settings
        SET
            previous_daily_advice = COALESCE(@previous_daily_advice, previous_daily_advice),
            last_maintenance_run = COALESCE(@last_maintenance_run, last_maintenance_run);

    RETURN 0;
END
