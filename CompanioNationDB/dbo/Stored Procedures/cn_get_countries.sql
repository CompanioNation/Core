CREATE PROCEDURE [dbo].[cn_get_countries]
    @continent NVARCHAR(2)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ISO as country_code, Country as country_name
    FROM cn_geonames_countries
    WHERE Continent = @continent
    ORDER BY country_name;
END
