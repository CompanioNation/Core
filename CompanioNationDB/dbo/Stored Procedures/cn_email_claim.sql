-- =============================================
-- Claims the next due email for sending, or returns nothing when the queue is idle.
--
-- Concurrency-safe across instances: UPDLOCK holds the row for the duration of the
-- UPDATE, ROWLOCK avoids escalating to a page/table lock that would serialise the
-- whole queue, and READPAST lets a competing worker skip a row already being claimed
-- instead of blocking behind it. The single UPDATE..OUTPUT is atomic. If a claim
-- expires, a later claim gets a different deadline; completion checks that deadline.
--
-- The claim is a VISIBILITY TIMEOUT: the row stays status 0 and its next_attempt_at
-- is pushed forward by @lease_seconds. If the worker dies mid-send the row simply
-- becomes due again when that window passes, so no reclaim sweep is needed.
--
-- A claim deliberately does NOT count an attempt. Only cn_email_complete does, when
-- an outcome is actually known, so a crash-looping host can never burn an email's
-- retry budget without ever attempting a send.
-- =============================================
CREATE PROCEDURE [dbo].[cn_email_claim]
	@lease_seconds INT = 120
AS
BEGIN
	SET NOCOUNT ON;

	IF @lease_seconds IS NULL OR @lease_seconds < 30
		SET @lease_seconds = 30;

	DECLARE @now DATETIME = GETUTCDATE();

	;WITH next_email AS (
		SELECT TOP (1) *
		FROM cn_email_queue WITH (UPDLOCK, ROWLOCK, READPAST)
		WHERE status = 0
		  AND next_attempt_at <= @now
		ORDER BY next_attempt_at, email_id
	)
	UPDATE next_email
	SET next_attempt_at = DATEADD(SECOND, @lease_seconds, @now)
	OUTPUT inserted.email_id,
		   inserted.recipient,
		   inserted.subject,
		   inserted.body_text,
		   inserted.body_html,
		   inserted.headers,
		   inserted.attempt_count,
		   inserted.next_attempt_at;
END;