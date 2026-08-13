CREATE PROCEDURE [dbo].[cn_add_companionita_advice]
	@advice_id INT = NULL,
	@outline_text NVARCHAR(MAX) = NULL,
	@language_code NVARCHAR(10) = 'en',
	@advice_text NVARCHAR(MAX)
AS
BEGIN
	SET NOCOUNT ON;

	-- First language call creates the header row and stores the English fallback text
	IF (@advice_id IS NULL OR @advice_id = 0)
	BEGIN
		SET @advice_id = (SELECT COALESCE(MAX(advice_id), 0) + 1 FROM cn_companionita);
		INSERT INTO cn_companionita (advice_id, advice_text, outline_text)
		VALUES (@advice_id, CASE WHEN @language_code = 'en' THEN @advice_text ELSE N'' END, @outline_text);
	END

	-- English stays on the header (legacy fallback); other languages go in the translations table
	IF (@language_code = 'en')
	BEGIN
		UPDATE cn_companionita SET advice_text = @advice_text WHERE advice_id = @advice_id;
	END
	ELSE
	BEGIN
		IF EXISTS (SELECT 1 FROM cn_companionita_translations WHERE advice_id = @advice_id AND language_code = @language_code)
			UPDATE cn_companionita_translations SET advice_text = @advice_text WHERE advice_id = @advice_id AND language_code = @language_code;
		ELSE
			INSERT INTO cn_companionita_translations (advice_id, language_code, advice_text) VALUES (@advice_id, @language_code, @advice_text);
	END

	SELECT @advice_id AS advice_id;
END

RETURN 0
