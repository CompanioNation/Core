-- =============================================
-- Records the outcome of one send attempt: sent, retry later, or dead-letter.
--
-- This single proc replaces the old mark-sent / mark-failed / release trio. It
-- reports whether the row was dead-lettered so the daemon can alert exactly once,
-- at the moment the email is given up on.
--
-- The attempt is counted HERE, not at claim time, so an attempt only costs the email
-- part of its retry budget once its outcome is known. The retry limit therefore lives
-- here and nowhere else. Retries back off exponentially (60s, 120s, 240s, ...) with
-- jitter so a poison email can never block the queue and a run of failures does not
-- retry in lockstep.
-- =============================================
CREATE PROCEDURE [dbo].[cn_email_complete]
	@email_id BIGINT,
	@claim_until DATETIME,
	@sent     BIT,
	@error    NVARCHAR(512) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @updated TABLE (status TINYINT);

	-- The deadline is the claim's generation: a worker whose claim was replaced
	-- must not overwrite the newer worker's outcome. The check and update are atomic.
	UPDATE cn_email_queue
	SET status = CASE WHEN @sent = 1 THEN 1 WHEN attempt_count + 1 >= 5 THEN 2 ELSE 0 END,
		attempt_count = attempt_count + 1,
		date_sent = CASE WHEN @sent = 1 THEN GETUTCDATE() ELSE date_sent END,
		last_error = CASE WHEN @sent = 1 THEN NULL ELSE @error END,
		next_attempt_at = CASE WHEN @sent = 0 AND attempt_count + 1 < 5
			THEN DATEADD(SECOND, 60 * CAST(POWER(2, attempt_count) AS INT)
				+ (CHECKSUM(NEWID()) & 2147483647) % 31, GETUTCDATE())
			ELSE next_attempt_at END
	OUTPUT inserted.status INTO @updated
	WHERE email_id = @email_id AND status = 0 AND next_attempt_at = @claim_until;

	-- -1 = stale/already resolved, 0 = sent or retry scheduled, 1 = dead-lettered.
	SELECT COALESCE((SELECT CASE WHEN status = 2 THEN 1 ELSE 0 END FROM @updated), -1);
END;