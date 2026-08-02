-- Daily maintenance tasks.
-- NOTE: Karma (ranking) is no longer recalculated here. Use cn_recalculate_karma
-- (manual, admin-only) for full recalculation. The former LINK-karma formula has been
-- removed to avoid silently overwriting the new confirmation-gated karma model.
--
-- The admin-only cn_recalculate_karma still supports full recalculation and desync
-- detection — but be aware it does not model reject penalties (those leave no row),
-- so penalties may be wiped by a full recalc.

CREATE PROCEDURE [dbo].[cn_maintenance]

AS
	-- Update rolling average photo rating per user
	UPDATE cn_users
		SET average_rating = (SELECT COALESCE(AVG(rating), 0) FROM cn_images img WHERE cn_users.user_id = img.user_id);

	-- Safety clamp: ranking should never be negative
	UPDATE cn_users SET ranking = 0 WHERE ranking < 0;

RETURN 0
