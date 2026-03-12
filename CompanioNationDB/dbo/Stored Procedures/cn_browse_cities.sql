CREATE PROCEDURE [dbo].[cn_browse_cities]
    @country_code NVARCHAR(2),
    @admin1_code NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.geonameid,
        c.name AS city_name,
        COUNT(DISTINCT u.user_id) AS profile_count
    FROM cn_users u
    INNER JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    WHERE c.country_code = @country_code
      AND c.admin1_code = @admin1_code
      AND u.searchable = 1
      AND u.name <> ''
      AND EXISTS (
          SELECT 1 FROM cn_images i
          WHERE i.user_id = u.user_id AND i.image_visible = 1
      )
    GROUP BY c.geonameid, c.name
    ORDER BY c.name;
END
