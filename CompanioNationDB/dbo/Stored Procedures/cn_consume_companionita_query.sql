-- =============================================
-- Description: Atomically reserves one premium CompanioNita query for the caller in the
--              current UTC day. Returns 1 when the query was reserved, 0 when the caller
--              has already used @daily_limit queries today.
--
--              The reservation is a single UPDATE, so the exclusive lock it takes on the
--              user row serialises concurrent callers: the daily cap can never be exceeded
--              no matter how many requests arrive at once. It is also O(1) — no COUNT over
--              the usage tables — and the day rolls over implicitly because a stored
--              companionita_query_date other than today restarts the counter at 1.
-- =============================================
CREATE PROCEDURE cn_consume_companionita_query
	@login_token UNIQUEIDENTIFIER,
	@daily_limit INT
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @user_id INT;

	SELECT @user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token;

	IF @user_id IS NULL
	BEGIN;
		THROW 100000, 'Login token has expired or is invalid.', 1;
	END

	DECLARE @today DATE = CAST(GETUTCDATE() AS DATE);

	UPDATE cn_users
	SET companionita_queries_today =
			CASE WHEN companionita_query_date = @today THEN companionita_queries_today + 1 ELSE 1 END,
		companionita_query_date = @today
	WHERE user_id = @user_id
	  AND (companionita_query_date IS NULL
		   OR companionita_query_date <> @today
		   OR companionita_queries_today < @daily_limit);

	DECLARE @consumed BIT = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;

	SELECT @consumed AS consumed;

	RETURN 0;
END
