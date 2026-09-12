-- Site statistics, WITHOUT the admin check.
--
-- Called by the admin wrapper (cn_admin_get_site_stats), which performs the login-token
-- validation before delegating here, and by the server-side nightly maintenance report,
-- which has no admin session.
--
-- SECURITY: this procedure has no authorization of its own. Never expose it through a hub
-- method or any other client-reachable path — go through cn_admin_get_site_stats for that.

CREATE PROCEDURE [dbo].[cn_get_site_stats]
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @today DATE = CAST(GETUTCDATE() AS DATE);

	-- Every window below covers COMPLETE days only and ends at YESTERDAY. A partial "today"
	-- is a poor daily indicator: it keeps growing all day, and it is near-zero when the
	-- nightly report is generated at 08:00 UTC. Windows are also exclusive of today so that
	-- "7 days" really means 7 complete days rather than 8 calendar days including today.
	DECLARE @yesterday DATE = DATEADD(DAY, -1, @today);

	-- Result set 1: headline totals
	SELECT
		(SELECT COUNT(*) FROM cn_users)                                                                          AS total_users,
		(SELECT COUNT(*) FROM cn_users WHERE verified = 1)                                                       AS verified_users,
		(SELECT COUNT(*) FROM cn_users WHERE subscription_expiry IS NOT NULL
			AND subscription_expiry > GETUTCDATE())                                                              AS subscribers,
		(SELECT COUNT(*) FROM cn_users WHERE is_administrator = 1)                                               AS administrators,
		(SELECT COUNT(*) FROM cn_users WHERE is_muted = 1)                                                       AS muted_users,
		(SELECT COUNT(DISTINCT user_id) FROM cn_images WHERE image_visible = 1)                                  AS users_with_photos,
		(SELECT COUNT(*) FROM cn_images)                                                                         AS total_photos,
		(SELECT COUNT(*) FROM cn_messages)                                                                       AS total_messages,
		(SELECT COUNT(*) FROM cn_connections)                                                                    AS total_connections,
		(SELECT COUNT(*) FROM cn_users WHERE date_created >= @yesterday)                                          AS signups_yesterday,
		(SELECT COUNT(*) FROM cn_users WHERE date_created >= DATEADD(DAY, -7,  @today)
			AND date_created < @today)                                                                           AS signups_7,
		(SELECT COUNT(*) FROM cn_users WHERE date_created >= DATEADD(DAY, -30, @today)
			AND date_created < @today)                                                                           AS signups_30,
		(SELECT COUNT(*) FROM cn_users WHERE last_login   >= @yesterday)                                          AS active_yesterday,
		(SELECT COUNT(*) FROM cn_users WHERE last_login   >= DATEADD(DAY, -7,  @today)
			AND last_login < @today)                                                                             AS active_7,
		(SELECT COUNT(*) FROM cn_users WHERE last_login   >= DATEADD(DAY, -30, @today)
			AND last_login < @today)                                                                             AS active_30;

	-- Result set 2: signups by day (30 complete days ending yesterday, zero-filled)
	;WITH days AS (
		SELECT 0 AS n, @yesterday AS d
		UNION ALL
		SELECT n + 1, DATEADD(DAY, -(n + 1), @yesterday)
		FROM days WHERE n < 29
	)
	SELECT days.d AS bucket, ISNULL(c.cnt, 0) AS cnt
	FROM days
	LEFT JOIN (
		SELECT CAST(date_created AS DATE) AS d, COUNT(*) AS cnt
		FROM cn_users
		WHERE date_created >= DATEADD(DAY, -30, @today)
		  AND date_created < @today
		GROUP BY CAST(date_created AS DATE)
	) c ON c.d = days.d
	ORDER BY days.d
	OPTION (MAXRECURSION 100);

	-- Result set 3: signups by month (last 12 months, zero-filled)
	-- Deliberately still includes the current (partial) month: unlike a day, a month-to-date
	-- figure is the conventional reading, and excluding it would leave the newest month blank
	-- and make the chart look stale.
	;WITH months AS (
		SELECT 0 AS n, DATEFROMPARTS(YEAR(@today), MONTH(@today), 1) AS m
		UNION ALL
		SELECT n + 1, DATEADD(MONTH, -(n + 1), DATEFROMPARTS(YEAR(@today), MONTH(@today), 1))
		FROM months WHERE n < 11
	)
	SELECT months.m AS bucket, ISNULL(c.cnt, 0) AS cnt
	FROM months
	LEFT JOIN (
		SELECT DATEFROMPARTS(YEAR(date_created), MONTH(date_created), 1) AS m, COUNT(*) AS cnt
		FROM cn_users
		WHERE date_created >= DATEADD(MONTH, -12, DATEFROMPARTS(YEAR(@today), MONTH(@today), 1))
		GROUP BY DATEFROMPARTS(YEAR(date_created), MONTH(date_created), 1)
	) c ON c.m = months.m
	ORDER BY months.m
	OPTION (MAXRECURSION 100);

	-- Result set 4: signups by year (all-time)
	SELECT YEAR(date_created) AS bucket, COUNT(*) AS cnt
	FROM cn_users
	GROUP BY YEAR(date_created)
	ORDER BY YEAR(date_created);

	-- Result set 5: active users by day (30 complete days ending yesterday) based on cn_users.last_login
	;WITH days AS (
		SELECT 0 AS n, @yesterday AS d
		UNION ALL
		SELECT n + 1, DATEADD(DAY, -(n + 1), @yesterday)
		FROM days WHERE n < 29
	)
	SELECT days.d AS bucket, ISNULL(c.cnt, 0) AS cnt
	FROM days
	LEFT JOIN (
		SELECT CAST(last_login AS DATE) AS d, COUNT(*) AS cnt
		FROM cn_users
		WHERE last_login IS NOT NULL
		  AND last_login >= DATEADD(DAY, -30, @today)
		  AND last_login < @today
		GROUP BY CAST(last_login AS DATE)
	) c ON c.d = days.d
	ORDER BY days.d
	OPTION (MAXRECURSION 100);
END
RETURN 0
