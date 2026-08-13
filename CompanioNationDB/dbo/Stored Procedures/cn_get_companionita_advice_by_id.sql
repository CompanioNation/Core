CREATE PROCEDURE [dbo].[cn_get_companionita_advice_by_id]
	@advice_id int = 0,
	@language_code nvarchar(10) = 'en'
AS
	SELECT h.advice_id, h.date_created,
		COALESCE(t.advice_text, h.advice_text) AS advice_text
		FROM cn_companionita h
		LEFT JOIN cn_companionita_translations t ON t.advice_id = h.advice_id AND t.language_code = @language_code
		WHERE h.advice_id = @advice_id

RETURN 0
