CREATE PROCEDURE [dbo].[cn_get_linked_users]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;

    -- Validate login token
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF @user_id IS NULL
    BEGIN
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Return all confirmed links with the other user's details
    -- Result set 1: Linked users
    SELECT
        CASE WHEN c.user1 = @user_id THEN c.user2 ELSE c.user1 END AS UserId,
        u.name AS Name,
        c.connection_id AS ConnectionId,
        c.link_type AS LinkType,
        c.date_created AS DateLinked,
        ISNULL(
            (SELECT TOP 1 img.image_guid
             FROM cn_images img
             WHERE img.user_id = CASE WHEN c.user1 = @user_id THEN c.user2 ELSE c.user1 END
               AND img.image_visible = 1
             ORDER BY img.date_created DESC),
            CAST(0x0 AS UNIQUEIDENTIFIER)
        ) AS Thumbnail,
        2 + (SELECT COUNT(*) * 2
             FROM cn_images img
             WHERE img.connection_id = c.connection_id) AS KarmaEarned
    FROM cn_connections c
    INNER JOIN cn_users u ON u.user_id = CASE WHEN c.user1 = @user_id THEN c.user2 ELSE c.user1 END
    WHERE (c.user1 = @user_id OR c.user2 = @user_id)
      AND c.confirmed = 1
    ORDER BY c.date_created DESC;

    -- Result set 2: All photos for user's connections
    SELECT
        img.image_id AS ImageId,
        img.image_guid AS ImageGuid,
        img.user_id AS SubjectUserId,
        img.image_visible AS ImageVisible,
        CAST(CASE
            WHEN img.user_id = CASE WHEN c.user1 = @user_id THEN c.user2 ELSE c.user1 END
            THEN 1  -- current user is the uploader (subject is the other user)
            ELSE 0
        END AS BIT) AS IsUploader,
        img.date_created AS DateCreated,
        c.connection_id AS ConnectionId
    FROM cn_images img
    INNER JOIN cn_connections c ON c.connection_id = img.connection_id
    WHERE (c.user1 = @user_id OR c.user2 = @user_id)
      AND c.confirmed = 1
    ORDER BY img.date_created DESC;
END
