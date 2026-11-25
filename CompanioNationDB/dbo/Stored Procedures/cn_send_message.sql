
CREATE PROCEDURE [dbo].[cn_send_message]
    @login_token UNIQUEIDENTIFIER,
    @user_id INT,
    @message_text NVARCHAR(MAX),
    @is_companionita BIT = 0,
    -- Characters of each unread message the notification summary needs. The caller passes
    -- PushNotificationText.MaxBodyLength; the default keeps older callers working.
    @push_body_length INT = 300
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

    -- Return the new message id along with the from user name and user id.
    -- push_token_user_id identifies the OWNER of the returned push_token (the
    -- recipient), so the server can clear a stale token for the right user.
    SELECT 
        m.message_id, u2.push_token, u2.user_id AS push_token_user_id,
        u_from.user_id, u_from.name, m.companionita, 
        i.date_created AS ignored_since,
        (SELECT COUNT(*) FROM cn_messages um
         WHERE um.to_user_id = @user_id AND um.isread = 0 AND um.companionita = 0) AS recipient_unread_count
    FROM cn_messages m
    INNER JOIN cn_users u_from ON u_from.user_id = m.from_user_id
    INNER JOIN cn_users u2 ON u2.user_id = m.to_user_id
    LEFT JOIN cn_ignore i ON i.user_id = m.to_user_id AND i.user_id_to_ignore = m.from_user_id
    WHERE m.message_id = SCOPE_IDENTITY();

    IF EXISTS (SELECT 1 FROM cn_users WHERE user_id = @user_id AND NULLIF(push_token, '') IS NOT NULL)
       AND NOT EXISTS (SELECT 1 FROM cn_ignore WHERE user_id = @user_id AND user_id_to_ignore = @current_user_id)
    BEGIN
        -- The notification shows the unread conversation top-down and, when it does not fit,
        -- only its LAST @push_body_length characters. So each row is clipped to its own tail
        -- right here: a multi-kilobyte CompanioNita reply never leaves the database.
        -- Only the newest 20 unread rows are read: a notification banner cannot show more,
        -- and a flood of one-character messages must not turn into a large read.
        SELECT TOP (20) RIGHT(m.message_text, @push_body_length) AS message_text
        FROM cn_messages m
        WHERE m.to_user_id = @user_id AND m.isread = 0 AND DATALENGTH(m.message_text) > 0
          AND NOT EXISTS (SELECT 1 FROM cn_ignore i WHERE i.user_id = @user_id AND i.user_id_to_ignore = m.from_user_id)
        ORDER BY m.message_id DESC;
    END
    ELSE
    BEGIN
        SELECT TOP (0) N'' AS message_text;
    END

END;
