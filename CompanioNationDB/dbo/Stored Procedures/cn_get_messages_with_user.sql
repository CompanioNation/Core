CREATE PROCEDURE [dbo].[cn_get_messages_with_user]
    @login_token UNIQUEIDENTIFIER,
    @user_id INT
AS
BEGIN
    DECLARE @current_user_id INT;

    -- Validate the login token and get the current user ID
    SELECT @current_user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF @current_user_id IS NULL
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END

    -- Fetch the message history with the specified user
    SELECT m.message_id, m.from_user_id, m.to_user_id, m.message_text, m.isread, m.date_created, from_u.name as from_user_name, to_u.name as to_user_name, m.companionita
    FROM cn_messages m, cn_users from_u, cn_users to_u
    WHERE m.from_user_id = from_u.user_id AND m.to_user_id = to_u.user_id AND
        (
            (m.from_user_id = @current_user_id AND m.to_user_id = @user_id)
            OR (m.from_user_id = @user_id AND m.to_user_id = @current_user_id)
        )
    ORDER BY message_id; -- order by message_id rather than date_created because it is indexed and proportional

    -- Update the IsRead flag for these messages
    UPDATE cn_messages SET isread = 1 WHERE from_user_id = @user_id AND to_user_id = @current_user_id
END;
