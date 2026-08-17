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
	-- Update rolling average rating per user (derived from LINK reviews)
	UPDATE cn_users
		SET average_rating = (
			SELECT COALESCE(AVG(CAST(r AS FLOAT)), 0)
			FROM (
				SELECT rating1 AS r FROM cn_connections WHERE user2 = cn_users.user_id AND confirmed = 1 AND rating1 IS NOT NULL
				UNION ALL
				SELECT rating2 AS r FROM cn_connections WHERE user1 = cn_users.user_id AND confirmed = 1 AND rating2 IS NOT NULL
			) AS received_ratings
		);

	-- Safety clamp: ranking should never be negative
	UPDATE cn_users SET ranking = 0 WHERE ranking < 0;

RETURN 0
