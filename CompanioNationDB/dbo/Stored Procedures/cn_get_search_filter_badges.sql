-- Returns the badges offered as "must have" filters in find-companion search.
-- Any authenticated caller may list them. Secret (is_visible = 0) badges are excluded.
CREATE PROCEDURE [dbo].[cn_get_search_filter_badges]
	@login_token UNIQUEIDENTIFIER
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE login_token = @login_token)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	SELECT badge_id, name, icon, icon_type, icon_image_guid
	FROM cn_event_badges
	WHERE is_search_filter = 1
	  AND is_active = 1
	  AND is_visible = 1
	ORDER BY name ASC;
END
GO
