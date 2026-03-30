-- Rewritten for LINK karma model (replaces legacy guarantor-based ranking)
CREATE PROCEDURE [dbo].[cn_maintenance]

AS
	-- Recalculate rankings using LINK karma formula:
	--   (count of self-uploaded photos)
	-- + (sum of photo ratings)
	-- + (count of confirmed connections × 2)  -- base LINK karma
	-- + (count of LINK photos involving user × 2)  -- every LINK photo earns +2
	UPDATE cn_users 
		SET ranking = 
			-- Self-uploaded photos (connection_id IS NULL)
			(SELECT COUNT(*) FROM cn_images WHERE cn_images.user_id = cn_users.user_id AND connection_id IS NULL)
			-- Sum of photo ratings
			+ (SELECT COALESCE(SUM(rating), 0) FROM cn_images WHERE cn_images.user_id = cn_users.user_id)
			-- Base LINK karma: confirmed connections × 2
			+ (SELECT COUNT(*) * 2 FROM cn_connections WHERE confirmed = 1 AND (user1 = cn_users.user_id OR user2 = cn_users.user_id))
			-- LINK photo karma: every LINK photo involving user × 2
			+ (SELECT COUNT(*) * 2 FROM cn_images i
			   INNER JOIN cn_connections c ON i.connection_id = c.connection_id
			   WHERE c.user1 = cn_users.user_id OR c.user2 = cn_users.user_id),
			average_rating = (SELECT COALESCE(AVG(rating), 0) FROM cn_images img WHERE cn_users.user_id = img.user_id);

RETURN 0
