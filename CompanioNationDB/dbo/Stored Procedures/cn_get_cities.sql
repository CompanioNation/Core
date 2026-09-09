CREATE PROCEDURE [dbo].[cn_get_cities]
	@country NVARCHAR(2),
	@search_term NVARCHAR(MAX)
AS
BEGIN
	SET NOCOUNT ON;

	-- City-picker autocomplete ("select your city"). Every result set is hard-capped
	-- so a short/broad term can never again force a full-country scan + sort that
	-- outlives the client's command timeout.
	DECLARE @cap INT = 50;

	-- Normalize the term and escape LIKE wildcards so user input is treated literally.
	DECLARE @term NVARCHAR(200) = LTRIM(RTRIM(@search_term));
	DECLARE @term_len INT = LEN(@term);
	SET @term = REPLACE(REPLACE(REPLACE(@term, '\', '\\'), '%', '\%'), '_', '\_');

	-- Nothing typed -> no suggestions. Never return the whole country.
	IF @term_len = 0
	BEGIN
		SELECT TOP (0)
			c.geonameid,
			ct.continent AS continent_code,
			c.country_code,
			ct.country AS country_name,
			a.name AS admin1_name,
			c.name AS city_name
		FROM cn_geonames_cities c
		INNER JOIN cn_geonames_admin1 a
			ON a.country_code = c.country_code AND a.admin1_code = c.admin1_code
		INNER JOIN cn_geonames_countries ct
			ON ct.ISO = c.country_code
		ORDER BY c.population DESC, c.name ASC;
		RETURN;
	END;

	-- 1-2 characters: prefix match against the display name only. This stays on the
	-- index path and stays fast even in large countries, at the cost of not yet
	-- consulting ascii/alternate names (those become relevant once the user types
	-- enough to make them selective).
	IF @term_len <= 2
	BEGIN
		SELECT TOP (@cap)
			c.geonameid,
			ct.continent AS continent_code,
			c.country_code,
			ct.country AS country_name,
			a.name AS admin1_name,
			c.name AS city_name
		FROM cn_geonames_cities c
		INNER JOIN cn_geonames_admin1 a
			ON a.country_code = c.country_code AND a.admin1_code = c.admin1_code
		INNER JOIN cn_geonames_countries ct
			ON ct.ISO = c.country_code
		WHERE c.country_code = @country
		  AND c.name LIKE @term + N'%' ESCAPE N'\'
		ORDER BY c.population DESC, c.name ASC;
		RETURN;
	END;

	-- 3+ characters: prefix-first search with a contains fallback plus a province
	-- fill tier. Later tiers run only when earlier tiers could not fill the cap,
	-- so the common case (a prefix that matches plenty of cities) is one indexed
	-- probe instead of repeated full-country scans.
	CREATE TABLE #cities
	(
		geonameid INT PRIMARY KEY,
		rank INT NOT NULL
	);

	DECLARE @room INT;

	-- Tier 1: display name starts with the term (index-assisted).
	INSERT INTO #cities (geonameid, rank)
	SELECT TOP (@cap) c.geonameid, 1
	FROM cn_geonames_cities c
	WHERE c.country_code = @country
	  AND c.name LIKE @term + N'%' ESCAPE N'\'
	ORDER BY c.population DESC;

	-- Tier 2: ascii name starts with the term. This is what makes the search
	-- accent-insensitive: a plain "evora"/"sao" search still finds Évora / São
	-- Paulo via their unaccented ascii name.
	SET @room = @cap - (SELECT COUNT(*) FROM #cities);
	IF @room > 0
	BEGIN
		INSERT INTO #cities (geonameid, rank)
		SELECT TOP (@room) c.geonameid, 2
		FROM cn_geonames_cities c
		WHERE c.country_code = @country
		  AND c.asciiname IS NOT NULL
		  AND c.asciiname LIKE @term + N'%' ESCAPE N'\'
		  AND NOT EXISTS (SELECT 1 FROM #cities h WHERE h.geonameid = c.geonameid)
		ORDER BY c.population DESC;
	END;

	-- Tier 3: an alternate name (the comma-separated alias list) starts with the
	-- term, either as the first alias or right after a comma.
	SET @room = @cap - (SELECT COUNT(*) FROM #cities);
	IF @room > 0
	BEGIN
		INSERT INTO #cities (geonameid, rank)
		SELECT TOP (@room) c.geonameid, 3
		FROM cn_geonames_cities c
		WHERE c.country_code = @country
		  AND c.alternatenames IS NOT NULL
		  AND (c.alternatenames LIKE @term + N'%' ESCAPE N'\'
			   OR c.alternatenames LIKE N',' + @term + N'%' ESCAPE N'\'
			   OR c.alternatenames LIKE N', ' + @term + N'%' ESCAPE N'\')
		  AND NOT EXISTS (SELECT 1 FROM #cities h WHERE h.geonameid = c.geonameid)
		ORDER BY c.population DESC;
	END;

	-- Tier 4: a province/state (admin1) name starts with the term -> its cities.
	-- Deliberately a lower tier so region matches never crowd out direct city-name
	-- matches for the same term.
	SET @room = @cap - (SELECT COUNT(*) FROM #cities);
	IF @room > 0
	BEGIN
		INSERT INTO #cities (geonameid, rank)
		SELECT TOP (@room) c.geonameid, 4
		FROM cn_geonames_admin1 a
		INNER JOIN cn_geonames_cities c
			ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
		WHERE a.country_code = @country
		  AND (a.name LIKE @term + N'%' ESCAPE N'\'
			   OR a.name_ascii LIKE @term + N'%' ESCAPE N'\')
		  AND NOT EXISTS (SELECT 1 FROM #cities h WHERE h.geonameid = c.geonameid)
		ORDER BY c.population DESC;
	END;

	-- Tier 5 (fallback): mid-name "contains" match, only when the prefix tiers were
	-- scarce. This preserves the old any-substring behavior without paying for it
	-- on every keystroke.
	SET @room = @cap - (SELECT COUNT(*) FROM #cities);
	IF @room > 0
	BEGIN
		INSERT INTO #cities (geonameid, rank)
		SELECT TOP (@room) c.geonameid, 5
		FROM cn_geonames_cities c
		WHERE c.country_code = @country
		  AND (c.name LIKE N'%' + @term + N'%' ESCAPE N'\'
			   OR (c.asciiname IS NOT NULL AND c.asciiname LIKE N'%' + @term + N'%' ESCAPE N'\')
			   OR (c.alternatenames IS NOT NULL AND c.alternatenames LIKE N'%' + @term + N'%' ESCAPE N'\'))
		  AND NOT EXISTS (SELECT 1 FROM #cities h WHERE h.geonameid = c.geonameid)
		ORDER BY c.population DESC;
	END;

	SELECT
		c.geonameid,
		ct.continent AS continent_code,
		c.country_code,
		ct.country AS country_name,
		a.name AS admin1_name,
		c.name AS city_name
	FROM #cities h
	INNER JOIN cn_geonames_cities c ON c.geonameid = h.geonameid
	INNER JOIN cn_geonames_admin1 a
		ON a.country_code = c.country_code AND a.admin1_code = c.admin1_code
	INNER JOIN cn_geonames_countries ct ON ct.ISO = c.country_code
	ORDER BY h.rank ASC, c.population DESC, c.name ASC, c.geonameid ASC;

	DROP TABLE #cities;
END
