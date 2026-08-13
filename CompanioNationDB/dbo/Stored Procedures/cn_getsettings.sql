CREATE PROCEDURE [dbo].[cn_getsettings]
	@language_code nvarchar(10) = 'en'
AS
	SELECT
		COALESCE(d.daily_advice, s.daily_advice) AS daily_advice,
		s.previous_daily_advice AS previous_daily_advice,
		s.last_maintenance_run AS last_maintenance_run
	FROM cn_settings s
	LEFT JOIN cn_daily_advice d ON d.language_code = @language_code

RETURN 0
