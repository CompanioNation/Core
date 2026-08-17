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
    --
    -- ⚠️ ADMIN-ONLY / MANUAL OPERATION — this SP is NOT called by any automated
    --    maintenance job. It must be invoked explicitly by an administrator.
    --
    -- Formula per user:
    --   (COUNT of self-uploaded photos) + (SUM of received LINK ratings)
    --   + (COUNT of confirmed connections * 2) -- base LINK karma
    --   + (COUNT of LINK photos involving user * 2) -- photo karma (confirmed only)
    --   - (COUNT of unresolved reports * 5) -- report penalty
    --
    -- WARNING: The formula appears twice below (SELECT for desync detection, UPDATE for correction).
    -- Both CTEs MUST be kept identical — any change to one must be mirrored in the other.
    --
    -- KNOWN GAP — REJECT PENALTIES: cn_reject_link_photo deducts −1 karma from the
    -- uploader, but rejected photos are hard-deleted so no row remains to represent
    -- the penalty. Running this SP will RESTORE the −1 for every user who was ever
    -- penalized for a rejected photo. Until a penalty-tracking table is added, any
    -- admin invoking this SP must be aware that reject-history karma adjustments
    -- will be silently wiped.

    ;WITH karma AS (
        SELECT
            u.user_id,
            u.name,
            u.ranking AS StoredRanking,
            (
                (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
                + ISNULL((SELECT SUM(rating1) FROM cn_connections WHERE user2 = u.user_id AND confirmed = 1 AND rating1 IS NOT NULL), 0)
                + ISNULL((SELECT SUM(rating2) FROM cn_connections WHERE user1 = u.user_id AND confirmed = 1 AND rating2 IS NOT NULL), 0)
                + (SELECT COUNT(*) * 2 FROM cn_connections
                   WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
                + (SELECT COUNT(*) * 2 FROM cn_images img
                   INNER JOIN cn_connections c ON c.connection_id = img.connection_id
                   WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1 AND img.subject_confirmed = 1)
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

    -- Correct all rankings to calculated values (formula MUST match the SELECT CTE above)
    ;WITH karma AS (
        SELECT
            u.user_id,
            (
                (SELECT COUNT(*) FROM cn_images WHERE user_id = u.user_id AND connection_id IS NULL)
                + ISNULL((SELECT SUM(rating1) FROM cn_connections WHERE user2 = u.user_id AND confirmed = 1 AND rating1 IS NOT NULL), 0)
                + ISNULL((SELECT SUM(rating2) FROM cn_connections WHERE user1 = u.user_id AND confirmed = 1 AND rating2 IS NOT NULL), 0)
                + (SELECT COUNT(*) * 2 FROM cn_connections
                   WHERE (user1 = u.user_id OR user2 = u.user_id) AND confirmed = 1)
                + (SELECT COUNT(*) * 2 FROM cn_images img
                   INNER JOIN cn_connections c ON c.connection_id = img.connection_id
                   WHERE (c.user1 = u.user_id OR c.user2 = u.user_id) AND c.confirmed = 1 AND img.subject_confirmed = 1)
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
