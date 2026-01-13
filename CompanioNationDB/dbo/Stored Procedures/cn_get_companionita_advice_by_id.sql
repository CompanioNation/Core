CREATE PROCEDURE [dbo].[cn_get_companionita_advice_by_id]
	@advice_id int = 0
AS
	SELECT *
		FROM cn_companionita 
		WHERE advice_id = @advice_id

RETURN 0
