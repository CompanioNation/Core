CREATE PROCEDURE [dbo].[cn_browse_countries]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        ct.ISO AS country_code,
        ct.Country AS country_name,
        ct.Continent AS continent_code,
        COUNT(DISTINCT u.user_id) AS profile_count
    FROM cn_users u
    INNER JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    INNER JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    WHERE u.searchable = 1
      AND u.name <> ''
      AND EXISTS (
          SELECT 1 FROM cn_images i
          WHERE i.user_id = u.user_id AND i.image_visible = 1
      )
    GROUP BY ct.ISO, ct.Country, ct.Continent
    ORDER BY ct.Country;
END
