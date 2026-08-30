-- Returns the badges awarded to a user. Any authenticated caller may read badges.
CREATE PROCEDURE [dbo].[cn_get_user_badges]
	@login_token UNIQUEIDENTIFIER,
	@target_user_id INT
AS
BEGIN
	SET NOCOUNT ON;

	-- Validate the caller holds a valid session.
	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE login_token = @login_token)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	SELECT
		b.badge_id,
		b.name,
		b.description,
		b.icon,
		ub.date_awarded
	FROM cn_user_badges ub
	INNER JOIN cn_event_badges b ON b.badge_id = ub.badge_id
	WHERE ub.user_id = @target_user_id
	ORDER BY ub.date_awarded ASC;
END
GO
