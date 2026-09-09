CREATE PROCEDURE [dbo].[cn_admin_get_profiles]
    @login_token UNIQUEIDENTIFIER,
    @offset INT = 0,
    @count INT = 20,
    @search_term NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @caller_user_id INT;
    DECLARE @is_admin BIT;

    -- Validate login token and verify admin
    SELECT @caller_user_id = user_id, @is_admin = is_administrator
    FROM cn_users
    WHERE login_token = @login_token;

    IF (@caller_user_id IS NULL)
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    IF (@is_admin = 0)
    BEGIN;
        THROW 400000, 'Unauthorized. Admin access required.', 1;
    END;

    -- Trim the search term once and pre-parse numeric terms up front so a user-id
    -- search becomes a sargable equality against the clustered primary key instead of
    -- casting every row's key column to text inside the WHERE clause.
    SET @search_term = NULLIF(LTRIM(RTRIM(@search_term)), '');
    DECLARE @search_user_id INT = TRY_CAST(@search_term AS INT);

    -- Moderation / scam-scan queue ordering:
    --   1) unresolved-report profiles first (most reported on top),
    --   2) then everyone else ordered by most recent login (never-logged-in last).
    -- The 24-hour scam-check window only applies to the unfiltered queue, so a bulk scan
    -- never re-rates a profile inside its once-per-24h cooldown. Deleted accounts are
    -- excluded from the queue too — deletion stamps last_login, which would otherwise
    -- push scrubbed profiles to the front of a "most recently active" sort. An explicit
    -- search bypasses both filters so any specific profile stays findable.
    ;WITH report_counts AS
    (
        -- Aggregated once per query instead of as a correlated subquery re-evaluated for
        -- every candidate row in both the SELECT list and the ORDER BY.
        SELECT reported_user_id, COUNT(*) AS pending_reports
        FROM cn_reports
        WHERE status = 0
        GROUP BY reported_user_id
    )
    SELECT 
        u.user_id,
        u.name,
        u.email,
        u.description,
        u.gender,
        u.bday,
        u.ranking,
        u.searchable,
        u.date_created,
        u.last_login,
        u.is_muted,
        u.is_deleted,
        u.payment_system,
        u.scam_rating,
        u.scam_rating_rationale,
        COALESCE(c.name, '') AS city_name,
        COALESCE(a.name, '') AS admin1_name,
        COALESCE(ct.Country, '') AS country_name,
        (SELECT TOP 1 image_guid FROM cn_images WHERE cn_images.user_id = u.user_id AND image_visible = 1 ORDER BY image_id DESC) AS thumbnail,
        (SELECT COUNT(*) FROM cn_images i WHERE i.user_id = u.user_id) AS photo_count,
        ISNULL(rc.pending_reports, 0) AS pending_reports
    FROM cn_users u
    LEFT JOIN report_counts rc ON rc.reported_user_id = u.user_id
    LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    WHERE (@search_term IS NULL
            AND u.is_deleted = 0
            AND (u.scam_rating_timestamp IS NULL
                 OR u.scam_rating_timestamp < DATEADD(HOUR, -24, GETUTCDATE())))
       OR (@search_term IS NOT NULL
            AND (u.user_id = @search_user_id
                 OR u.name LIKE '%' + @search_term + '%'
                 OR u.email LIKE '%' + @search_term + '%'))
    ORDER BY 
        ISNULL(rc.pending_reports, 0) DESC,
        u.last_login DESC,  -- DESC places NULL (never logged in) last
        u.user_id DESC
    OFFSET @offset ROWS FETCH NEXT @count ROWS ONLY;
END
