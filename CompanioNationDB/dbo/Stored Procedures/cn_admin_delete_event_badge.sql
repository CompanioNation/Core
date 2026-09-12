-- Admin hard-deletes an event badge AND every award of it (cascade). Returns the
-- badge's icon_image_guid so the caller can delete the orphaned icon blob.
-- Admin authorization is enforced here.
CREATE PROCEDURE [dbo].[cn_admin_delete_event_badge]
	@login_token UNIQUEIDENTIFIER,
	@badge_id    INT
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

	DECLARE @icon_image_guid UNIQUEIDENTIFIER;
	SELECT @icon_image_guid = icon_image_guid
	FROM cn_event_badges
	WHERE badge_id = @badge_id;

	DELETE FROM cn_user_badges WHERE badge_id = @badge_id;
	DELETE FROM cn_event_badges WHERE badge_id = @badge_id;

	SELECT @icon_image_guid AS icon_image_guid;
END
GO
