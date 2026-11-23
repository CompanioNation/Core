-- =============================================
-- Adds one fully rendered email to the outbound queue.
--
-- This is the ONLY write the request thread performs for an email. The bodies are
-- already final (unsubscribe footer, one-click headers, absolute links), so nothing
-- downstream needs host-only state and the send can happen in any process.
--
-- Deliberately returns no row id: no caller has ever needed it, and the queue's contract
-- is "the email is now durable", which a caller learns from the absence of an error.
-- =============================================
CREATE PROCEDURE [dbo].[cn_email_enqueue]
	@recipient NVARCHAR(255),
	@subject   NVARCHAR(512),
	@body_text NVARCHAR(MAX),
	@body_html NVARCHAR(MAX) = NULL,
	@headers   NVARCHAR(MAX) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF @recipient IS NULL OR LTRIM(RTRIM(@recipient)) = ''
		THROW 50001, 'Recipient address is required', 1;

	IF @subject IS NULL OR LTRIM(RTRIM(@subject)) = ''
		THROW 50001, 'Subject is required', 1;

	-- An empty HTML part means "text only", not "empty HTML body".
	IF @body_html IS NOT NULL AND LTRIM(RTRIM(@body_html)) = ''
		SET @body_html = NULL;

	IF @headers IS NOT NULL AND LTRIM(RTRIM(@headers)) = ''
		SET @headers = NULL;

	INSERT INTO cn_email_queue (recipient, subject, body_text, body_html, headers, status, next_attempt_at)
	VALUES (LTRIM(RTRIM(@recipient)), @subject, @body_text, @body_html, @headers, 0, GETUTCDATE());
END;
