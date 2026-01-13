CREATE PROCEDURE [dbo].[cn_add_companionita_advice]
	@advice_text NVARCHAR(MAX)
AS

	DECLARE @advice_id int;
	SET @advice_id = (SELECT COALESCE(MAX(advice_id), 0) + 1 FROM cn_companionita)
	INSERT INTO cn_companionita (advice_id, advice_text) VALUES (@advice_id, @advice_text);

RETURN 0
