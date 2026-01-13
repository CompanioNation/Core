CREATE PROCEDURE [dbo].[cn_start_user_conversation]
    @login_token UNIQUEIDENTIFIER,
    @user_id INT
AS
BEGIN
    SET NOCOUNT ON;

     -- Check if the user exists
    DECLARE @login_user_id INT;
    SELECT @login_user_id = user_id 
    FROM cn_users 
    WHERE login_token = @login_token;

    IF (@login_user_id IS NULL) 
    BEGIN;
        THROW 100000, 'Invalid Credentials', 1;
    END;


    DELETE FROM cn_ignore WHERE user_id = @login_user_id AND user_id_to_ignore = @user_id;

    -- Fetch user details by user ID
    SELECT 
        u.*, 
        ct.Continent as continent_code, ct.ISO as country_code, c.name as city_name, a.name as admin1_name, ct.Country as country_name,
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
        CAST(IIF(i_is_ignored.ignore_id IS NULL, 0, 1) AS BIT) AS is_ignored,
        CAST(IIF(i_ignored_by_me.ignore_id IS NULL, 0, 1) AS BIT) AS ignored_by_me

    FROM cn_users u
    LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
    LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
    LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
    LEFT JOIN cn_ignore i_is_ignored ON i_is_ignored.user_id = u.user_id AND i_is_ignored.user_id_to_ignore = @login_user_id
    LEFT JOIN cn_ignore i_ignored_by_me ON i_ignored_by_me.user_id = @login_user_id AND i_ignored_by_me.user_id_to_ignore = u.user_id
        
    WHERE u.user_id = @user_id

END;
