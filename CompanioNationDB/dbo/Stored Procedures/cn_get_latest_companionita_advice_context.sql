CREATE PROCEDURE [dbo].[cn_get_latest_companionita_advice_context]
AS
	SELECT TOP(1) advice_id, date_created, outline_text
		FROM cn_companionita
		WHERE outline_text IS NOT NULL AND outline_text <> N''
		ORDER BY advice_id DESC

RETURN 0
