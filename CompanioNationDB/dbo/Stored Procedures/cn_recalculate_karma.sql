CREATE PROCEDURE [dbo].[cn_recalculate_karma]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;
    DECLARE @is_admin BIT;

    -- Validate login token and admin status
    SELECT @user_id = user_id, @is_admin = is_administrator
    FROM cn_users
    WHERE login_token = @login_token;

    IF @user_id IS NULL
    BEGIN
        THROW 100000, 'Invalid Credentials', 1;
    END;

    IF @is_admin = 0
    BEGIN
        THROW 400000, 'Admin access required', 1;
    END;

    -- Recalculate ranking for all users and detect desync
    -- Formula per user:
    --   (COUNT of self-uploaded photos) + (SUM of photo ratings)
    --   + (COUNT of confirmed connections * 2) -- base LINK karma
    --   + (COUNT of LINK photos involving user * 2) -- photo karma

    SELECT
        u.user_id AS UserId,
        u.name AS Name,
        u.ranking AS StoredRanking,
        (
            -- Self-uploaded photos count
            (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
            -- Sum of all photo ratings
            + ISNULL((SELECT SUM(rating) FROM cn_images WHERE user_id = u.user_id), 0)
            -- Base LINK karma: confirmed connections * 2
            + (SELECT COUNT(*) * 2 FROM cn_connections
               WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
            -- LINK photo karma: every LINK photo involving user * 2
            + (SELECT COUNT(*) * 2 FROM cn_images img
               INNER JOIN cn_connections c ON c.connection_id = img.connection_id
               WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1)
        ) AS CalculatedRanking,
        (
            (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
            + ISNULL((SELECT SUM(rating) FROM cn_images WHERE user_id = u.user_id), 0)
            + (SELECT COUNT(*) * 2 FROM cn_connections
               WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
            + (SELECT COUNT(*) * 2 FROM cn_images img
               INNER JOIN cn_connections c ON c.connection_id = img.connection_id
               WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1)
        ) - u.ranking AS Delta
    FROM cn_users u
    WHERE (
        (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
        + ISNULL((SELECT SUM(rating) FROM cn_images WHERE user_id = u.user_id), 0)
        + (SELECT COUNT(*) * 2 FROM cn_connections
           WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
        + (SELECT COUNT(*) * 2 FROM cn_images img
           INNER JOIN cn_connections c ON c.connection_id = img.connection_id
           WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1)
    ) != u.ranking;

    -- Correct all rankings to calculated values
    UPDATE u
    SET u.ranking = (
        (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
        + ISNULL((SELECT SUM(rating) FROM cn_images WHERE user_id = u.user_id), 0)
        + (SELECT COUNT(*) * 2 FROM cn_connections
           WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
        + (SELECT COUNT(*) * 2 FROM cn_images img
           INNER JOIN cn_connections c ON c.connection_id = img.connection_id
           WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1)
    )
    FROM cn_users u;
END
