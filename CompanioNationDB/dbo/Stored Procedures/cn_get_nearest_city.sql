CREATE PROCEDURE [dbo].[cn_get_nearest_city]
	@login_token UNIQUEIDENTIFIER,
	@latitude DECIMAL(9,6),
	@longitude DECIMAL(9,6)
AS
BEGIN
	SET NOCOUNT ON;  -- Prevent extra result sets from interfering with the output

	DECLARE @user_id INT;

	-- Validate the login token against the users table
	SELECT @user_id = u.user_id
	FROM cn_users u
	WHERE u.login_token = @login_token;

	-- Throw error if credentials are invalid
	IF (@@ROWCOUNT = 0)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	IF @latitude IS NULL OR @longitude IS NULL RETURN 0;

	-- Limit the candidate set to a small bounding box (~0.25 deg latitude is
	-- roughly a 28 km radius) so the index can be used, then return the five
	-- closest cities ordered by squared planar distance.
	DECLARE @degrees_to_search DECIMAL(9,6) = 0.25;

	-- Scale the longitude to match the same number of kilometers as the latitude.
	-- Convert latitude from degrees to radians.
	DECLARE @latitude_radians DECIMAL(9,6) = @latitude * PI() / 180.0;
	-- Calculate the scaling factor for longitude.
	DECLARE @longitude_scaling_factor DECIMAL(9,6) = 1/COS(@latitude_radians);

	SELECT TOP 5
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

	-- Squared distance with longitude scaled so a degree of longitude is
	-- weighted the same as a degree of latitude at this point on the globe.
	ORDER BY
		(c.latitude - @latitude) * (c.latitude - @latitude)
		+ ((c.longitude - @longitude) / @longitude_scaling_factor) * ((c.longitude - @longitude) / @longitude_scaling_factor);

	RETURN 0;
END
