CREATE PROCEDURE [dbo].[cn_login_get_credentials]
    @email NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT = (SELECT user_id FROM cn_users WHERE email = @email)

    IF @user_id IS NULL
        RETURN;

    -- Return full user details including password/hash columns for C# verification.
    -- Uses the same JOIN pattern as cn_get_user so ReadUserDetails can parse the result.
    SELECT u.*,
           ct.Continent AS continent_code,
           ct.ISO AS country_code,
           c.name AS city_name,
           a.name AS admin1_name,
           ct.Country AS country_name,
           (SELECT COUNT(*)
            FROM cn_messages m
            LEFT JOIN cn_ignore i
                ON m.from_user_id = i.user_id_to_ignore
                AND i.user_id = @user_id
            WHERE m.to_user_id = u.user_id
              AND m.isread = 0
              AND i.user_id_to_ignore IS NULL) AS unread_messages_count,
           (SELECT TOP 1 image_guid
                FROM cn_images
                WHERE cn_images.user_id = @user_id
                ORDER BY image_id DESC) AS thumbnail
    FROM cn_users u
    LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    WHERE u.user_id = @user_id;
END
