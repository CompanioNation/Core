-- =============================================
-- Author:      <Author,,Name>
-- Create date: <Create Date,,>
-- Description: Validates the login token and updates the last login timestamp
-- =============================================
CREATE PROCEDURE cn_validate_login_token
    @login_token VARCHAR(1024)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;

    -- Attempt to update the last login time and retrieve the user details in a single query
    UPDATE cn_users
    SET last_login = GETUTCDATE(), @user_id = user_id
    WHERE login_token = @login_token;

    -- If no rows were updated, raise an error for token timeout or invalid token
    IF @user_id IS NULL
    BEGIN;
        THROW 100000, 'Login token has expired or is invalid.', 1;
    END

	-- Return the user details
	EXEC cn_get_user @user_id

END
