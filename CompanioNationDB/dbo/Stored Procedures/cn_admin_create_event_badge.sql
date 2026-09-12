-- Admin creates a new event badge definition. Returns the new badge_id.
-- Admin authorization is enforced here; callers that are not administrators get 400000.
CREATE PROCEDURE [dbo].[cn_admin_create_event_badge]
	@login_token       UNIQUEIDENTIFIER,
	@name              NVARCHAR(100),
	@description       NVARCHAR(500) = '',
	@icon              NVARCHAR(50) = N'🏅',
	@icon_type         NVARCHAR(10) = 'emoji',
	@icon_image_guid   UNIQUEIDENTIFIER = NULL,
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

	IF (@name IS NULL OR LTRIM(RTRIM(@name)) = '')
	BEGIN;
		THROW 50001, 'A badge name is required.', 1;
	END;

	INSERT INTO cn_event_badges
		(name, description, icon, icon_type, icon_image_guid, is_active, is_visible, search_weight, is_search_filter, transfer_mode)
	VALUES
		(LTRIM(RTRIM(@name)), ISNULL(@description, ''), ISNULL(@icon, N'🏅'), ISNULL(@icon_type, 'emoji'),
		 @icon_image_guid, 1, @is_visible, @search_weight, @is_search_filter, @transfer_mode);

	SELECT CAST(SCOPE_IDENTITY() AS INT) AS badge_id;
END
GO
