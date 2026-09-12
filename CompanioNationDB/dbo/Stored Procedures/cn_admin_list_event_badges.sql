-- Returns all event badge definitions for the admin badge editor.
CREATE PROCEDURE [dbo].[cn_admin_list_event_badges]
	@login_token UNIQUEIDENTIFIER
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

	SELECT
		badge_id,
		name,
		description,
		icon,
		icon_type,
		icon_image_guid,
		is_active,
		is_visible,
		search_weight,
		is_search_filter,
		transfer_mode,
		-- Live award counts so the admin UI can warn before a destructive delete.
		(SELECT COUNT(*) FROM cn_user_badges ub WHERE ub.badge_id = b.badge_id) AS award_count
	FROM cn_event_badges b
	ORDER BY name ASC;
END
GO
