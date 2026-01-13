CREATE PROCEDURE [dbo].[cn_send_message]
    @login_token UNIQUEIDENTIFIER,
    @user_id INT,
    @message_text NVARCHAR(MAX),
    @is_companionita BIT = 0
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

    -- Insert the new message into the messages table
    INSERT INTO cn_messages (from_user_id, to_user_id, message_text, isread, companionita)
    VALUES (@current_user_id, @user_id, @message_text, 0, @is_companionita);

    -- Return the new message id along with the from user name and user id
    SELECT 
        m.message_id, u2.push_token,
        u_from.user_id, u_from.name, m.companionita, 
        i.date_created AS ignored_since
    FROM cn_messages m
    INNER JOIN cn_users u_from ON u_from.user_id = m.from_user_id
    INNER JOIN cn_users u2 ON u2.user_id = m.to_user_id
    LEFT JOIN cn_ignore i ON i.user_id = m.to_user_id AND i.user_id_to_ignore = m.from_user_id
    WHERE m.message_id = SCOPE_IDENTITY();

END;
