CREATE PROCEDURE [dbo].[cn_maintenance]
	
AS
	-- SET THE RANKINGS TO BE WHAT THEY SHOULD BE
	-- add one for every guarantee the user has made, for every guarantee the user has received
	-- and add all of the review values up
	UPDATE cn_users 
		SET ranking = 
		(SELECT COUNT(*) + COALESCE(SUM(rating), 0) FROM cn_images WHERE cn_images.user_id = cn_users.user_id)
			+ (SELECT COUNT(*) FROM cn_images WHERE guarantor_user_id = cn_users.user_id),
			average_rating = (SELECT COALESCE(AVG(rating), 0) FROM cn_images img WHERE cn_users.user_id = img.user_id);


RETURN 0
