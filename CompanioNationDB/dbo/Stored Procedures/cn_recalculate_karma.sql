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
    --   - (COUNT of unresolved reports * 5) -- report penalty

    ;WITH karma AS (
        SELECT
            u.user_id,
            u.name,
            u.ranking AS StoredRanking,
            (
                (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
                + ISNULL((SELECT SUM(rating) FROM cn_images WHERE user_id = u.user_id), 0)
                + (SELECT COUNT(*) * 2 FROM cn_connections
                   WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
                + (SELECT COUNT(*) * 2 FROM cn_images img
                   INNER JOIN cn_connections c ON c.connection_id = img.connection_id
                   WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1)
                - (SELECT COUNT(*) * 5 FROM cn_reports
                   WHERE reported_user_id = u.user_id AND status = 0)
            ) AS RawRanking
        FROM cn_users u
    )

    SELECT
        user_id AS UserId,
        name AS Name,
        StoredRanking,
        CASE WHEN RawRanking < 0 THEN 0 ELSE RawRanking END AS CalculatedRanking,
        CASE WHEN RawRanking < 0 THEN 0 ELSE RawRanking END - StoredRanking AS Delta
    FROM karma
    WHERE CASE WHEN RawRanking < 0 THEN 0 ELSE RawRanking END != StoredRanking;

    -- Correct all rankings to calculated values
    ;WITH karma AS (
        SELECT
            u.user_id,
            (
                (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
                + ISNULL((SELECT SUM(rating) FROM cn_images WHERE user_id = u.user_id), 0)
                + (SELECT COUNT(*) * 2 FROM cn_connections
                   WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
                + (SELECT COUNT(*) * 2 FROM cn_images img
                   INNER JOIN cn_connections c ON c.connection_id = img.connection_id
                   WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1)
                - (SELECT COUNT(*) * 5 FROM cn_reports
                   WHERE reported_user_id = u.user_id AND status = 0)
            ) AS RawRanking
        FROM cn_users u
    )
    UPDATE u
    SET u.ranking = CASE WHEN k.RawRanking < 0 THEN 0 ELSE k.RawRanking END
    FROM cn_users u
    INNER JOIN karma k ON u.user_id = k.user_id;
END
