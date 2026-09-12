-- Admin updates an event badge definition. Returns the PREVIOUS icon_image_guid so the
-- caller can delete a replaced icon blob. Admin authorization is enforced here.
CREATE PROCEDURE [dbo].[cn_admin_update_event_badge]
	@login_token       UNIQUEIDENTIFIER,
	@badge_id          INT,
	@name              NVARCHAR(100),
	@description       NVARCHAR(500) = '',
	@icon              NVARCHAR(50) = N'🏅',
	@icon_type         NVARCHAR(10) = 'emoji',
	@icon_image_guid   UNIQUEIDENTIFIER = NULL,
	@is_active         BIT = 1,
	@is_visible        BIT = 1,
	@search_weight     INT = 0,
	@is_search_filter  BIT = 0,
	@transfer_mode     TINYINT = 0
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

	IF (@name IS NULL OR LTRIM(RTRIM(@name)) = '')
	BEGIN;
		THROW 50001, 'A badge name is required.', 1;
	END;

	DECLARE @previous_icon_image_guid UNIQUEIDENTIFIER;
	SELECT @previous_icon_image_guid = icon_image_guid
	FROM cn_event_badges
	WHERE badge_id = @badge_id;

	UPDATE cn_event_badges
	SET name             = LTRIM(RTRIM(@name)),
		description      = ISNULL(@description, ''),
		icon             = ISNULL(@icon, N'🏅'),
		icon_type        = ISNULL(@icon_type, 'emoji'),
		icon_image_guid  = @icon_image_guid,
		is_active        = @is_active,
		is_visible       = @is_visible,
		search_weight    = @search_weight,
		is_search_filter = @is_search_filter,
		transfer_mode    = @transfer_mode
	WHERE badge_id = @badge_id;

	SELECT @previous_icon_image_guid AS previous_icon_image_guid;
END
GO
