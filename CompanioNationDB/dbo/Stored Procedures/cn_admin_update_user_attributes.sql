-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2026
-- Description:	Admin-only update of account-level user attributes
--              (subscription expiry, administrator status, verification,
--              mute state, and optional password hash).
--              NULL @subscription_expiry clears the expiry date.
--              NULL @new_password_hash leaves the password unchanged.
-- =============================================
CREATE PROCEDURE [dbo].[cn_admin_update_user_attributes]
	@login_token UNIQUEIDENTIFIER,
	@target_user_id INT,
	@subscription_expiry DATETIME = NULL,
	@is_administrator BIT,
	@verified BIT,
	@is_muted BIT,
	@new_password_hash NVARCHAR(512) = NULL,
	@new_password_hash_version INT = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @caller_user_id INT;

	-- Validate the caller is an administrator
	SELECT @caller_user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token AND is_administrator = 1;

	IF (@caller_user_id IS NULL)
	BEGIN;
		THROW 400000, 'Unauthorized. Admin access required.', 1;
	END;

	-- Prevent an administrator from demoting themselves — removing your own admin
	-- flag can strand the site with no administrator. All other self-edits
	-- (subscription expiry, verified, mute, password) are allowed.
	IF (@caller_user_id = @target_user_id AND @is_administrator = 0)
	BEGIN;
		THROW 400004, 'Cannot remove your own administrator status.', 1;
	END;

	UPDATE cn_users
	SET
		subscription_expiry = @subscription_expiry,
		is_administrator = @is_administrator,
		verified = @verified,
		is_muted = @is_muted,
		password_hash = COALESCE(@new_password_hash, password_hash),
		password_hash_version = COALESCE(@new_password_hash_version, password_hash_version),
		-- Clear the legacy plaintext password when a new hash is set so the
		-- hash is the single source of truth going forward.
		password = CASE WHEN @new_password_hash IS NOT NULL THEN NULL ELSE password END
	WHERE user_id = @target_user_id;

	IF @@ROWCOUNT = 0
	BEGIN;
		THROW 400001, 'User not found.', 1;
	END;
END
GO

