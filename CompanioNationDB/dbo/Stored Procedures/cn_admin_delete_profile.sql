CREATE PROCEDURE [dbo].[cn_admin_delete_profile]
	@login_token UNIQUEIDENTIFIER,
	@target_user_id INT
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @caller_user_id INT;
	DECLARE @is_admin BIT;

	-- Validate login token and verify admin
	SELECT @caller_user_id = user_id, @is_admin = is_administrator
	FROM cn_users
	WHERE login_token = @login_token;

	IF (@caller_user_id IS NULL)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	IF (@is_admin = 0)
	BEGIN;
		THROW 400000, 'Unauthorized. Admin access required.', 1;
	END;

	-- Ensure the target user exists
	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE user_id = @target_user_id)
	BEGIN;
		THROW 400001, 'Profile not found.', 1;
	END;

	-- Hide all of the target user's images
	UPDATE cn_images
	SET image_visible = 0
	WHERE user_id = @target_user_id;

	-- Clear personal profile fields, hide from search, and invalidate login
	UPDATE cn_users
	SET searchable     = 0,
		name           = 'Deleted User',
		description    = '',
		login_token    = NULL,
		push_token     = ''
	WHERE user_id = @target_user_id;
END
