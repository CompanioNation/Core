CREATE PROCEDURE [dbo].[cn_browse_provinces]
    @country_code NVARCHAR(2)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        a.admin1_code,
        a.name AS admin1_name,
        COUNT(DISTINCT u.user_id) AS profile_count
    FROM cn_users u
    INNER JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    INNER JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    WHERE c.country_code = @country_code
      AND u.searchable = 1
      AND u.name <> ''
      AND EXISTS (
          SELECT 1 FROM cn_images i
          WHERE i.user_id = u.user_id AND i.image_visible = 1
      )
    GROUP BY a.admin1_code, a.name
    ORDER BY a.name;
END
