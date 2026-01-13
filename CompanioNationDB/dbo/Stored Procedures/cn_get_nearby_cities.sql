CREATE PROCEDURE [dbo].[cn_get_nearby_cities]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;  -- Prevent extra result sets from interfering with the output

    DECLARE @degrees_to_search DECIMAL(9,6) = 0.5;

    DECLARE @user_id INT;
    DECLARE @latitude DECIMAL(9,6);
    DECLARE @longitude DECIMAL(9,6);

    -- Validate the login token against the users table
    SELECT @latitude = c.latitude, @longitude = c.longitude
    FROM cn_users u
    LEFT OUTER JOIN cn_geonames_cities c ON c.geonameid = u.geonameid
    WHERE u.login_token = @login_token;

    -- Throw error if credentials are invalid
    IF (@@ROWCOUNT = 0) 
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;

    IF @latitude is NULL OR @longitude is NULL RETURN 0;

    -- Scale the longitude to match the same number of kilometers as the latitude
    -- Convert latitude from degrees to radians
    DECLARE @latitude_radians DECIMAL(9,6) = @latitude * PI() / 180.0;
    -- Calculate the scaling factor for longitude
    DECLARE @longitude_scaling_factor DECIMAL(9,6) = 1/COS(@latitude_radians);

    -- Fetch the list of searchable cities
    SELECT 
        c.geonameid, 
        ct.continent as continent_code,
        c.country_code,
        ct.country as country_name,
        a.name as admin1_name, 
        c.name as city_name
    FROM cn_geonames_cities c, cn_geonames_admin1 a, cn_geonames_countries ct
    WHERE 
        c.country_code = ct.ISO

        AND c.country_code = a.country_code
        AND c.admin1_code = a.admin1_code

        AND c.latitude < @latitude + @degrees_to_search AND c.latitude > @latitude - @degrees_to_search
        AND c.longitude < @longitude + @longitude_scaling_factor * @degrees_to_search AND c.longitude > @longitude - @longitude_scaling_factor * @degrees_to_search
    
    ORDER BY country_name, admin1_name, city_name;

    RETURN 0;
END
