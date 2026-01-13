CREATE PROCEDURE [dbo].[cn_savesettings]
    @daily_advice nvarchar(max) = null,  -- Change to nvarchar(max) for proper Unicode support
    @last_maintenance_run datetime = null,
    @previous_daily_advice nvarchar(max) = null
AS
BEGIN
    SET NOCOUNT ON;

    -- Update the settings table with provided values, keeping the current values if there's a null passed in
    UPDATE cn_settings 
    SET 
        daily_advice = COALESCE(@daily_advice, daily_advice), 
        last_maintenance_run = COALESCE(@last_maintenance_run, last_maintenance_run),
        previous_daily_advice = COALESCE(@previous_daily_advice, previous_daily_advice);

    RETURN 0;
END
