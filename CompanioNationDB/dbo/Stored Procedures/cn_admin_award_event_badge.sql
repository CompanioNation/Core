-- Admin awards a badge to a user. Idempotent: re-awarding an existing badge is a no-op.
CREATE PROCEDURE [dbo].[cn_admin_award_event_badge]
	@login_token UNIQUEIDENTIFIER,
	@target_user_id INT,
	@badge_id INT
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @caller_user_id INT;
	SELECT @caller_user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token AND is_administrator = 1;

	IF (@caller_user_id IS NULL)
	BEGIN;
		THROW 400000, 'Unauthorized. Admin access required.', 1;
	END;

	IF NOT EXISTS (SELECT 1 FROM cn_event_badges WHERE badge_id = @badge_id)
	BEGIN;
		THROW 400005, 'Badge not found.', 1;
	END;

	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE user_id = @target_user_id)
	BEGIN;
		THROW 400001, 'User not found.', 1;
	END;

	IF NOT EXISTS (SELECT 1 FROM cn_user_badges WHERE user_id = @target_user_id AND badge_id = @badge_id)
	BEGIN
		INSERT INTO cn_user_badges (user_id, badge_id, awarded_by)
		VALUES (@target_user_id, @badge_id, @caller_user_id);
	END;
END
GO
