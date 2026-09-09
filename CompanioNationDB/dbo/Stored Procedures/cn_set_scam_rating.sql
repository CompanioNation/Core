-- =============================================
-- Description:	Server-side upsert of a user's AI scam classification (rating 0-5,
--              short rationale, timestamp). This SP NEVER gates or refuses a write:
--              the once-per-24h reclassification cap is enforced upstream as an
--              LLM-CALL gate (see the hub's IsClassifiedRecently) because the LLM API
--              is the expensive resource. A computed result is always persisted here.
--              NULL @rating clears the classification. skipped_due_to_limit is always
--              0 from this SP; the flag is only ever set true by the C# fast path when
--              the AI call was skipped because the user was classified within 24h.
-- =============================================
CREATE PROCEDURE [dbo].[cn_set_scam_rating]
	@target_user_id INT,
	@rating INT = NULL,
	@rationale NVARCHAR(2000) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE user_id = @target_user_id)
	BEGIN;
		THROW 400001, 'User not found.', 1;
	END;

	IF (@rating IS NULL)
	BEGIN
		UPDATE cn_users
		SET scam_rating = NULL,
			scam_rating_rationale = NULL,
			scam_rating_timestamp = NULL
		WHERE user_id = @target_user_id;

		SELECT NULL AS scam_rating, NULL AS scam_rating_rationale, NULL AS scam_rating_timestamp,
			   CONVERT(BIT, 0) AS skipped_due_to_limit;
		RETURN;
	END

	-- Clamp to the documented 0-5 scale
	IF (@rating < 0) SET @rating = 0;
	IF (@rating > 5) SET @rating = 5;

	UPDATE cn_users
	SET scam_rating = @rating,
		scam_rating_rationale = @rationale,
		scam_rating_timestamp = GETUTCDATE()
	WHERE user_id = @target_user_id;

	SELECT @rating AS scam_rating,
		   @rationale AS scam_rating_rationale,
		   GETUTCDATE() AS scam_rating_timestamp,
		   CONVERT(BIT, 0) AS skipped_due_to_limit;
END
GO
