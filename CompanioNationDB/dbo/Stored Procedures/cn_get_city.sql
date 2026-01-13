CREATE PROCEDURE [dbo].[cn_get_city]
	@login_token UNIQUEIDENTIFIER,
	@geonameid INT
AS
	
    SET NOCOUNT ON;  -- Prevent extra result sets from interfering with the output

    DECLARE @user_id INT;

    -- Validate the login token against the users table
    SELECT @user_id = user_id 
    FROM cn_users 
    WHERE login_token = @login_token;

    -- Throw error if credentials are invalid
    IF (@user_id IS NULL) 
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    SELECT 
        c.geonameid, 
        ct.continent as continent_code,
        c.country_code, 
        ct.country as country_name,
        a.name as admin1_name, 
        c.name as city_name
        FROM cn_geonames_cities c, cn_geonames_admin1 a, cn_geonames_countries ct
        WHERE 
            ct.ISO = c.country_code
            
            AND c.country_code = a.country_code
            AND c.admin1_code = a.admin1_code
          
            AND c.geonameid = @geonameid;



RETURN 0
