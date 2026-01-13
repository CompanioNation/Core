CREATE PROCEDURE [dbo].[cn_get_ignored_messages]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;

    -- Validate login token and get user_id
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Retrieve ignored messages
    SELECT m.message_id, m.from_user_id, m.to_user_id, m.message_text, m.isread, m.date_created
    FROM cn_messages m
    INNER JOIN cn_ignore i ON m.from_user_id = i.user_id_to_ignore
    WHERE i.user_id = @user_id
    ORDER BY m.date_created DESC;
END;
GO
