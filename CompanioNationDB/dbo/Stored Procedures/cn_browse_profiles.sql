CREATE PROCEDURE [dbo].[cn_browse_profiles]
    @geonameid INT,
    @offset INT = 0,
    @page_size INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    -- Return total count for pagination
    SELECT COUNT(*) AS total_count
    FROM cn_users u
    WHERE u.geonameid = @geonameid
      AND u.searchable = 1
      AND u.name <> ''
      AND EXISTS (
          SELECT 1 FROM cn_images i
          WHERE i.user_id = u.user_id AND i.image_visible = 1
      );

    -- Return paginated profiles sorted by combined ranking + seo popularity
    SELECT 
        u.user_id,
        u.name,
        u.gender,
        u.description,
        u.ranking,
        u.seo_clicks,
        u.bday,
        (
            SELECT TOP (1) image_guid
            FROM cn_images
            WHERE user_id = u.user_id AND image_visible = 1
            ORDER BY image_id DESC
        ) AS thumbnail,
        COALESCE(c.name, '') AS city_name,
        COALESCE(a.name, '') AS admin1_name,
        COALESCE(ct.Country, '') AS country_name
    FROM cn_users u
    LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    WHERE u.geonameid = @geonameid
      AND u.searchable = 1
      AND u.name <> ''
      AND EXISTS (
          SELECT 1 FROM cn_images i
          WHERE i.user_id = u.user_id AND i.image_visible = 1
      )
    ORDER BY (u.ranking + u.seo_clicks) DESC
    OFFSET @offset ROWS FETCH NEXT @page_size ROWS ONLY;
END
