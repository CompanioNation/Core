CREATE PROCEDURE [dbo].[cn_get_companionita_advice]
	@start int = 0,
	@count int = 30
AS
	SELECT TOP(@count) * 
		FROM cn_companionita 
		WHERE advice_id <= (SELECT MAX(advice_id) FROM cn_companionita) - @start
		ORDER BY advice_id DESC

RETURN 0
