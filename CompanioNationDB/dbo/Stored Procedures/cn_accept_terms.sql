CREATE PROCEDURE [dbo].[cn_accept_terms]
    @login_token UNIQUEIDENTIFIER,
    @version INT
AS
    DECLARE @user_id INT;
    SELECT @user_id = user_id FROM cn_users WHERE login_token = @login_token;
    IF @user_id IS NULL
    BEGIN; THROW 100000, 'Invalid Credentials', 1; END;

    UPDATE cn_users SET accepted_terms_version = @version WHERE user_id = @user_id;
RETURN 0
