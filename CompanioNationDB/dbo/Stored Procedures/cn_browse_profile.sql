CREATE PROCEDURE [dbo].[cn_browse_profile]
    @user_id INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Increment click count (only for searchable profiles with visible photos)
    UPDATE cn_users 
    SET seo_clicks = seo_clicks + 1 
    WHERE user_id = @user_id 
      AND searchable = 1;

    -- Return profile details
    SELECT 
        u.user_id,
        u.name,
        u.gender,
        u.description,
        u.ranking,
        u.seo_clicks,
        u.bday,
        COALESCE(c.name, '') AS city_name,
        COALESCE(a.name, '') AS admin1_name,
        COALESCE(ct.Country, '') AS country_name,
        (
            SELECT image_guid
            FROM cn_images
            WHERE user_id = u.user_id AND image_visible = 1
            ORDER BY image_id DESC
            FOR JSON PATH
        ) AS images,
        (
            SELECT review, date_created
            FROM cn_images
            WHERE user_id = u.user_id AND review_visible = 1
            ORDER BY image_id DESC
            FOR JSON PATH
        ) AS reviews
    FROM cn_users u
    LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    WHERE u.user_id = @user_id
      AND u.searchable = 1
      AND u.name <> ''
      AND EXISTS (
          SELECT 1 FROM cn_images i
          WHERE i.user_id = u.user_id AND i.image_visible = 1
      );
END
