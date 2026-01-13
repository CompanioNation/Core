CREATE PROCEDURE [dbo].[cn_find_companion]
    @login_token UNIQUEIDENTIFIER,
    @cismale BIT,
    @cisfemale BIT,
    @other BIT,
    @transmale BIT,
    @transfemale BIT,
    @cities dbo.cn_cities_type READONLY,
    @agemin INT = 18,  -- Provide a default value for minimum age
    @agemax INT = 99,   -- Provide a default value for maximum age
    @include_ignored_users BIT = 0
AS
BEGIN
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

    -- Fetch companions with their latest 10 guaranteed images, and location
    SELECT TOP (100)
        u.*,
        COALESCE(ct.Continent, '') AS continent_code,
        COALESCE(ct.ISO, '') AS country_code,
        COALESCE(c.name, '') AS city_name,
        COALESCE(a.name, '') AS admin1_name,
        COALESCE(ct.Country, '') AS country_name,
        (
            SELECT TOP (10) image_guid
            FROM cn_images
            WHERE user_id = u.user_id
            AND image_visible = 1
            ORDER BY image_id DESC
            FOR JSON PATH
        ) AS images,
        (
            SELECT TOP (10) review, date_created
            FROM cn_images
            WHERE user_id = u.user_id
            AND review_visible = 1
            ORDER BY image_id DESC
            FOR JSON PATH
        ) AS reviews,
        CASE 
            WHEN EXISTS (
                SELECT 1 
                FROM cn_ignore 
                WHERE user_id = @user_id AND u.user_id = user_id_to_ignore
            ) THEN CONVERT(BIT, 1)
            ELSE CONVERT(BIT, 0)
        END AS is_ignored  -- Flag indicating if the user is ignored
        
    FROM cn_users u
    LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    WHERE 
        ((@cismale = 1 AND u.gender = 2) OR
         (@cisfemale = 1 AND u.gender = 4) OR
         (@other = 1 AND u.gender = 8) OR
         (@transmale = 1 AND u.gender = 16) OR
         (@transfemale = 1 AND u.gender = 32))
        AND u.searchable = 1
        AND (@include_ignored_users = 1 
             OR NOT EXISTS (
                 SELECT 1
                 FROM cn_ignore 
                 WHERE user_id = @user_id AND u.user_id = user_id_to_ignore 
             )
        )
        AND u.user_id NOT IN (
            SELECT to_user_id 
            FROM cn_messages 
            WHERE from_user_id = @user_id
        )
        AND (u.bday IS NULL 
            OR (
                u.bday > DATEADD(YEAR, -@agemax, GETUTCDATE())
                AND u.bday < DATEADD(YEAR, -@agemin, GETUTCDATE())
            )
        )
        AND (NOT EXISTS (SELECT 1 FROM @cities)  -- TVP is empty
            OR u.geonameid IN (SELECT geonameid FROM @cities))


        -- Filter by group_id to ensure that only verified users are returned
        -- TODO TODO TODO TODO ** * re-enable this once group verification is implemented ... 
        -- OR ... use a verification percentage float from 0 to 1 to order the users, perhaps rounding to the nearest 0.1
        --AND group_id = (SELECT group_id FROM cn_users WHERE user_id = @user_id) 

    ORDER BY u.ranking DESC;
END;
