CREATE PROCEDURE [dbo].[cn_login_failed]
    @user_id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE cn_users
    SET failed_logins = failed_logins + 1
    WHERE user_id = @user_id;
END
