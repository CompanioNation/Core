CREATE PROCEDURE [dbo].[cn_admin_get_site_stats]
	@login_token UNIQUEIDENTIFIER
AS
BEGIN
	SET NOCOUNT ON;

	-- Validate admin
	DECLARE @user_id INT;
	SELECT @user_id = user_id FROM cn_users WHERE login_token = @login_token AND is_administrator = 1;
	IF @user_id IS NULL
	BEGIN; THROW 400000, 'Unauthorized', 1; END;

	DECLARE @today DATE = CAST(GETUTCDATE() AS DATE);

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
		(SELECT COUNT(*) FROM cn_users WHERE date_created >= @today)                                             AS signups_today,
		(SELECT COUNT(*) FROM cn_users WHERE date_created >= DATEADD(DAY, -7,  @today))                          AS signups_7,
		(SELECT COUNT(*) FROM cn_users WHERE date_created >= DATEADD(DAY, -30, @today))                          AS signups_30,
		(SELECT COUNT(*) FROM cn_users WHERE last_login   >= @today)                                             AS active_today,
		(SELECT COUNT(*) FROM cn_users WHERE last_login   >= DATEADD(DAY, -7,  @today))                          AS active_7,
		(SELECT COUNT(*) FROM cn_users WHERE last_login   >= DATEADD(DAY, -30, @today))                          AS active_30;

	-- Result set 2: signups by day (last 30 days, zero-filled)
	;WITH days AS (
		SELECT 0 AS n, @today AS d
		UNION ALL
		SELECT n + 1, DATEADD(DAY, -(n + 1), @today)
		FROM days WHERE n < 29
	)
	SELECT days.d AS bucket, ISNULL(c.cnt, 0) AS cnt
	FROM days
	LEFT JOIN (
		SELECT CAST(date_created AS DATE) AS d, COUNT(*) AS cnt
		FROM cn_users
		WHERE date_created >= DATEADD(DAY, -30, @today)
		GROUP BY CAST(date_created AS DATE)
	) c ON c.d = days.d
	ORDER BY days.d
	OPTION (MAXRECURSION 100);

	-- Result set 3: signups by month (last 12 months, zero-filled)
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

	-- Result set 5: active users by day (last 30 days) based on cn_users.last_login
	;WITH days AS (
		SELECT 0 AS n, @today AS d
		UNION ALL
		SELECT n + 1, DATEADD(DAY, -(n + 1), @today)
		FROM days WHERE n < 29
	)
	SELECT days.d AS bucket, ISNULL(c.cnt, 0) AS cnt
	FROM days
	LEFT JOIN (
		SELECT CAST(last_login AS DATE) AS d, COUNT(*) AS cnt
		FROM cn_users
		WHERE last_login IS NOT NULL
		  AND last_login >= DATEADD(DAY, -30, @today)
		GROUP BY CAST(last_login AS DATE)
	) c ON c.d = days.d
	ORDER BY days.d
	OPTION (MAXRECURSION 100);
END
RETURN 0
