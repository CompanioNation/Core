-- Returns the propagation tree of a badge (admin only). Nodes are joined through
-- received_from_user_id, which records who each holder received the badge from.
--   @root_user_id NULL -> roots are rows with no received_from_user_id (origin awards).
--   @root_user_id set  -> roots are that user's award(s) of the badge.
CREATE PROCEDURE [dbo].[cn_get_badge_tree]
	@login_token  UNIQUEIDENTIFIER,
	@badge_id     INT,
	@root_user_id INT = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @caller_user_id INT;
	SELECT @caller_user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token AND is_administrator = 1;

	IF (@caller_user_id IS NULL)
	BEGIN;
		THROW 400000, 'Unauthorized. Admin access required.', 1;
	END;

	;WITH tree AS
	(
		SELECT ub.user_badge_id, ub.user_id, ub.badge_id, ub.received_from_user_id,
			   ub.generation, ub.date_awarded, 0 AS depth
		FROM cn_user_badges ub
		WHERE ub.badge_id = @badge_id
		  AND (
				(@root_user_id IS NOT NULL AND ub.user_id = @root_user_id)
			 OR (@root_user_id IS NULL AND ub.received_from_user_id IS NULL)
			  )

		UNION ALL

		SELECT c.user_badge_id, c.user_id, c.badge_id, c.received_from_user_id,
			   c.generation, c.date_awarded, t.depth + 1
		FROM cn_user_badges c
		INNER JOIN tree t
			ON c.received_from_user_id = t.user_id
		   AND c.badge_id = t.badge_id
		WHERE c.user_id <> t.user_id
	)
	SELECT
		t.user_badge_id,
		t.user_id,
		u.name AS user_name,
		t.received_from_user_id,
		t.generation,
		t.depth,
		t.date_awarded
	FROM tree t
	INNER JOIN cn_users u ON u.user_id = t.user_id
	ORDER BY t.depth ASC, t.date_awarded ASC
	OPTION (MAXRECURSION 100);
END
GO
