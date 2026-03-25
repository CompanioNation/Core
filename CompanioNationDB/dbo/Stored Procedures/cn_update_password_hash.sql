CREATE PROCEDURE [dbo].[cn_update_password_hash]
    @user_id INT,
    @password_hash NVARCHAR(512),
    @password_hash_version INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE cn_users
    SET password_hash = @password_hash,
        password_hash_version = @password_hash_version,
        password = NULL
    WHERE user_id = @user_id;
END
