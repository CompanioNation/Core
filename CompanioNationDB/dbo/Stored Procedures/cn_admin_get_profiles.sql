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

    -- Return paginated profiles sorted by unresolved report count (most reports first), then ranking ASC
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
        COALESCE(c.name, '') AS city_name,
        COALESCE(a.name, '') AS admin1_name,
        COALESCE(ct.Country, '') AS country_name,
        (SELECT TOP 1 image_guid FROM cn_images WHERE cn_images.user_id = u.user_id ORDER BY image_id DESC) AS thumbnail,
        (SELECT COUNT(*) FROM cn_images i WHERE i.user_id = u.user_id) AS photo_count,
        (SELECT COUNT(*) FROM cn_reports r WHERE r.reported_user_id = u.user_id AND r.status = 0) AS pending_reports
    FROM cn_users u
    LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    WHERE (@search_term IS NULL OR @search_term = ''
        OR u.name LIKE '%' + @search_term + '%'
        OR u.email LIKE '%' + @search_term + '%'
        OR CAST(u.user_id AS NVARCHAR(20)) = @search_term)
    ORDER BY 
        (SELECT COUNT(*) FROM cn_reports r WHERE r.reported_user_id = u.user_id AND r.status = 0) DESC,
        u.ranking ASC,
        u.date_created DESC
    OFFSET @offset ROWS FETCH NEXT @count ROWS ONLY;
END
