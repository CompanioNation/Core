CREATE PROCEDURE [dbo].[cn_login_complete]
    @user_id INT,
    @ip_address VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @guid UNIQUEIDENTIFIER = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER)

    -- Also clear push_token: a fresh login invalidates any prior session on another
    -- device, and that prior device's push token MUST stop receiving notifications
    -- immediately (security: no notifications should go to a device with a stale
    -- login). The newly logged-in device will re-upload its own push_token via
    -- cn_update_push_token as soon as its client resubscribes.
    UPDATE cn_users
    SET login_token = @guid,
        failed_logins = 0,
        last_login = GETUTCDATE(),
        last_login_ip = @ip_address,
        push_token = ''
    WHERE user_id = @user_id;

    SELECT @guid AS login_token;
END
