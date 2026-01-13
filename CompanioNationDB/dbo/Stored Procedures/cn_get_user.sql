CREATE PROCEDURE [dbo].[cn_get_user]
	@user_id int
AS

	SELECT u.*, 
		   ct.Continent as continent_code,
		   ct.ISO as country_code,
		   c.name as city_name,
		   a.name as admin1_name,
		   ct.Country as country_name,
		   (SELECT COUNT(*) 
			FROM cn_messages m 
			LEFT JOIN cn_ignore i 
				ON m.from_user_id = i.user_id_to_ignore 
				AND i.user_id = @user_id
			WHERE m.to_user_id = u.user_id 
			  AND m.isread = 0
			  AND i.user_id_to_ignore IS NULL) AS unread_messages_count,
			(SELECT TOP 1 image_guid 
				FROM cn_images 
				WHERE cn_images.user_id = @user_id
				ORDER BY image_id DESC) as thumbnail
	FROM cn_users u
	LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
	LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
	LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
	WHERE u.user_id = @user_id;

RETURN 0
