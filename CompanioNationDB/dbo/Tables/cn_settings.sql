CREATE TABLE [dbo].[cn_settings]
(
    [daily_advice] NVARCHAR(MAX) NOT NULL DEFAULT '',  -- Use NVARCHAR(MAX) for better Unicode support
    [last_maintenance_run] DATETIME NULL  -- Stores the timestamp of the last maintenance run
, 
    [previous_daily_advice] NVARCHAR(MAX) NOT NULL DEFAULT '');
