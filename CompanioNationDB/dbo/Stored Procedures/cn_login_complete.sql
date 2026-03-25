CREATE PROCEDURE [dbo].[cn_login_complete]
    @user_id INT,
    @ip_address VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @guid UNIQUEIDENTIFIER = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER)

    UPDATE cn_users
    SET login_token = @guid,
        failed_logins = 0,
        last_login = GETUTCDATE(),
        last_login_ip = @ip_address
    WHERE user_id = @user_id;

    SELECT @guid AS login_token;
END
