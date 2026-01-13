CREATE PROCEDURE [dbo].[cn_get_contest_leaderboard]
AS

SELECT TOP (10)
    u.*,
    ct.continent as continent_code,
    c.country_code,
    ct.country as country_name,
    a.name as admin1_name, 
    c.name as city_name,
    (
        SELECT TOP (10) image_guid
        FROM cn_images
        WHERE user_id = u.user_id
        AND image_visible = 1
        ORDER BY image_id DESC
        FOR JSON PATH
    ) AS images,

    referral_data.referrals
FROM 
    cn_users u
LEFT JOIN cn_geonames_cities c ON u.geonameid = c.geonameid
LEFT JOIN cn_geonames_countries ct ON c.country_code = ct.ISO
LEFT JOIN cn_geonames_admin1 a ON c.country_code = a.country_code AND c.admin1_code = a.admin1_code
LEFT JOIN 
    (SELECT 
         guarantor_user_id, 
         COUNT(DISTINCT u2.user_id) AS referrals,
         MAX(i.date_created) as latest_date
     FROM 
         cn_images i, cn_users u2
     WHERE 
         i.user_id = u2.user_id 
         AND u2.verified = 1
         AND u2.gender > 1 -- This indicates that the user has successfully completed their profile (selected gender, etc)
         AND u2.date_created < '2024-12-25'
     GROUP BY 
         guarantor_user_id) AS referral_data
ON 
    u.user_id = referral_data.guarantor_user_id
WHERE 
    u.ineligible_for_contest = 0
	AND referral_data.referrals is not null
ORDER BY 
    referral_data.referrals DESC, referral_data.latest_date DESC;

RETURN 0
