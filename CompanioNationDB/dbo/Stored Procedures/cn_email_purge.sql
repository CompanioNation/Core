-- =============================================
-- Retention: removes old resolved queue rows so the table stays small.
--
-- Sent rows are kept for @sent_days so a late delivery report can still be matched;
-- dead-lettered rows are kept longer for diagnosis. Pending rows are never touched —
-- an email that has not resolved is never deleted.
-- =============================================
CREATE PROCEDURE [dbo].[cn_email_purge]
	@sent_days        INT = 30,
	@dead_letter_days INT = 90
AS
BEGIN
	SET NOCOUNT ON;

	IF @sent_days IS NULL OR @sent_days < 1
		SET @sent_days = 30;

	IF @dead_letter_days IS NULL OR @dead_letter_days < 1
		SET @dead_letter_days = 90;

	DELETE FROM cn_email_queue
	WHERE (status = 1 AND date_sent IS NOT NULL
		   AND date_sent < DATEADD(DAY, -@sent_days, GETUTCDATE()))
	   OR (status = 2
		   AND date_created < DATEADD(DAY, -@dead_letter_days, GETUTCDATE()));

	SELECT @@ROWCOUNT AS deleted;
END;
