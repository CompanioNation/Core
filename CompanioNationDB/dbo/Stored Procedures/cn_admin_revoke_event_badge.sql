-- Admin revokes a badge from a user. Idempotent: revoking a badge the user does
-- not have is a no-op.
CREATE PROCEDURE [dbo].[cn_admin_revoke_event_badge]
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

	DELETE FROM cn_user_badges
	WHERE user_id = @target_user_id AND badge_id = @badge_id;
END
GO
