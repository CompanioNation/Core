-- Returns the badges awarded to a user. Any authenticated caller may read badges.
CREATE PROCEDURE [dbo].[cn_get_user_badges]
	@login_token UNIQUEIDENTIFIER,
	@target_user_id INT
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @caller_user_id INT;
	DECLARE @caller_is_admin BIT = 0;
	SELECT @caller_user_id = user_id, @caller_is_admin = is_administrator
	FROM cn_users
	WHERE login_token = @login_token;

	-- Validate the caller holds a valid session.
	IF (@caller_user_id IS NULL)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	-- Secret (is_visible = 0) badges are hidden from everyone except an admin
	-- or the badge owner (so the owner can still see what they hold).
	DECLARE @include_secret BIT =
		CASE WHEN @caller_is_admin = 1 OR @caller_user_id = @target_user_id THEN 1 ELSE 0 END;

	SELECT
		b.badge_id,
		b.name,
		b.description,
		b.icon,
		b.icon_type,
		b.icon_image_guid,
		b.transfer_mode,
		ub.received_from_user_id,
		ub.generation,
		ub.date_awarded
	FROM cn_user_badges ub
	INNER JOIN cn_event_badges b ON b.badge_id = ub.badge_id
	WHERE ub.user_id = @target_user_id
	  AND (@include_secret = 1 OR b.is_visible = 1)
	ORDER BY ub.date_awarded ASC;
END
GO
