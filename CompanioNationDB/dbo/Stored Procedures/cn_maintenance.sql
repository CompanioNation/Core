-- Rewritten for LINK karma model (replaces legacy guarantor-based ranking)
CREATE PROCEDURE [dbo].[cn_maintenance]

AS
	-- Recalculate rankings using LINK karma formula:
	--   (count of self-uploaded photos)
	-- + (sum of photo ratings)
	-- + (count of confirmed connections × 2)  -- base LINK karma
	-- + (count of LINK photos involving user × 2)  -- every LINK photo earns +2
	-- - (count of unresolved reports × 5)  -- report penalty
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
			   WHERE c.user1 = cn_users.user_id OR c.user2 = cn_users.user_id)
			-- Report penalty: -5 per unresolved report against this user
			- (SELECT COUNT(*) * 5 FROM cn_reports WHERE reported_user_id = cn_users.user_id AND status = 0),
			average_rating = (SELECT COALESCE(AVG(rating), 0) FROM cn_images img WHERE cn_users.user_id = img.user_id);

	-- Ensure ranking never goes below zero
	UPDATE cn_users SET ranking = 0 WHERE ranking < 0;

RETURN 0
