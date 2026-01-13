CREATE PROCEDURE [dbo].[cn_get_cities]
    @country NVARCHAR(2),
    @search_term NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.geonameid, 
        ct.continent as continent_code,
        c.country_code, 
        ct.country as country_name,
        a.name as admin1_name, 
        c.name as city_name,
        CASE 
        WHEN CHARINDEX(@search_term, c.name) = 1 OR CHARINDEX(@search_term, c.alternatenames) = 1 THEN 1 -- Begins with search term
        ELSE 2
        END AS RelevanceLevel
    FROM cn_geonames_cities c, cn_geonames_admin1 a, cn_geonames_countries ct
    WHERE 
        ct.ISO = c.country_code
        
        AND c.country_code = a.country_code
        AND c.admin1_code = a.admin1_code
      
        AND c.country_code = @country
        
        AND (c.name LIKE '%' + @search_term + '%' OR c.alternatenames LIKE '%' + @search_term + '%')
    ORDER BY RelevanceLevel ASC, c.name ASC;
END
