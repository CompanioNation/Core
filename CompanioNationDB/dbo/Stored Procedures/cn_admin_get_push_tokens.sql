-- Returns non-empty push tokens for an admin broadcast or targeted send.
-- When @target_email is supplied, returns only that user's token.
CREATE PROCEDURE [dbo].[cn_admin_get_push_tokens]
	@login_token UNIQUEIDENTIFIER,
	@target_email NVARCHAR(255) = NULL
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
		user_id,
		push_token
	FROM cn_users
	WHERE push_token IS NOT NULL
	  AND LTRIM(RTRIM(push_token)) <> ''
	  AND (@target_email IS NULL OR email = @target_email)
	  AND is_deleted = 0
	ORDER BY user_id ASC;
END
GO
