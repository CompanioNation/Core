CREATE PROCEDURE [dbo].[cn_sitemap_urls]
	@max_count INT = 5000
AS
BEGIN
	SET NOCOUNT ON;

	-- Result set 1: countries with at least one searchable profile
	SELECT DISTINCT c.country_code
	FROM cn_users u
	INNER JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
	WHERE u.searchable = 1
	  AND u.is_deleted = 0
	  AND u.name <> ''
	  AND EXISTS (
		  SELECT 1 FROM cn_images i
		  WHERE i.user_id = u.user_id AND i.image_visible = 1
	  )
	ORDER BY c.country_code;

	-- Result set 2: provinces with at least one searchable profile
	SELECT DISTINCT c.country_code, c.admin1_code
	FROM cn_users u
	INNER JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
	WHERE u.searchable = 1
	  AND u.is_deleted = 0
	  AND u.name <> ''
	  AND EXISTS (
		  SELECT 1 FROM cn_images i
		  WHERE i.user_id = u.user_id AND i.image_visible = 1
	  )
	ORDER BY c.country_code, c.admin1_code;

	-- Result set 3: cities with at least one searchable profile
	SELECT DISTINCT c.country_code, c.admin1_code, c.geonameid
	FROM cn_users u
	INNER JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
	WHERE u.searchable = 1
	  AND u.is_deleted = 0
	  AND u.name <> ''
	  AND EXISTS (
		  SELECT 1 FROM cn_images i
		  WHERE i.user_id = u.user_id AND i.image_visible = 1
	  )
	ORDER BY c.country_code, c.admin1_code, c.geonameid;

	-- Result set 4: most recently registered searchable profile user ids.
	-- user_id is IDENTITY, so reverse PK order avoids an index on last_login.
	SELECT TOP (@max_count) u.user_id
	FROM cn_users u
	WHERE u.searchable = 1
	  AND u.is_deleted = 0
	  AND u.name <> ''
	  AND EXISTS (
		  SELECT 1 FROM cn_images i
		  WHERE i.user_id = u.user_id AND i.image_visible = 1
	  )
	ORDER BY u.user_id DESC;

	-- Result set 5: most recent CompanioNita advice articles.
	-- advice_id is assigned as MAX(advice_id) + 1, so reverse PK order = newest first.
	SELECT TOP (@max_count) advice_id, date_created
	FROM cn_companionita
	ORDER BY advice_id DESC;
END
