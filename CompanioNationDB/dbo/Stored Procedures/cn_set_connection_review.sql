-- Sets the caller's rating/review of the OTHER party in a confirmed LINK.
--
-- Two-way convention on cn_connections:
--   rating1 / review1 / review1_visible  = user1's review OF user2
--   rating2 / review2 / review2_visible  = user2's review OF user1
--
-- Visibility is controlled by the SUBJECT of the review (the person reviewed),
-- not the reviewer — see cn_set_connection_review_visibility. Ratings feed the
-- subject's ranking and rolling average_rating.
CREATE PROCEDURE [dbo].[cn_set_connection_review]
	@login_token UNIQUEIDENTIFIER,
	@connection_id INT,
	@rating INT,
	@review NVARCHAR(MAX)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @user_id INT;

	SELECT @user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token;

	IF @user_id IS NULL
	BEGIN
		THROW 100000, 'Invalid Credentials', 1;
	END;

	DECLARE @u1 INT, @u2 INT, @confirmed BIT;
	SELECT @u1 = user1, @u2 = user2, @confirmed = confirmed
	FROM cn_connections
	WHERE connection_id = @connection_id;

	IF @u1 IS NULL
	BEGIN
		THROW 500007, 'Link not found', 1;
	END;

	IF @confirmed IS NULL OR @confirmed = 0
	BEGIN
		THROW 500007, 'Link not confirmed', 1;
	END;

	IF @user_id != @u1 AND @user_id != @u2
	BEGIN
		THROW 50003, 'You are not part of this link', 1;
	END;

	-- Rating range matches the client slider (-1 = negative review, 0..5 = rating).
	-- Enforced here so a crafted client can't inflate another user's ranking.
	IF @rating < -1 OR @rating > 5
	BEGIN
		THROW 50001, 'Invalid rating. Rating must be between -1 and 5.', 1;
	END;

	-- Normalize blank reviews to NULL so empty text never surfaces on profiles.
	SET @review = NULLIF(LTRIM(RTRIM(@review)), '');

	-- Defense-in-depth length cap (mirrors the hub's 2000-char limit) so a direct
	-- DB caller can't bypass the hub and write an unbounded review.
	SET @review = LEFT(@review, 2000);

	DECLARE @target_user_id INT;
	DECLARE @old_rating INT;

	IF @user_id = @u1
	BEGIN
		SELECT @old_rating = rating1 FROM cn_connections WHERE connection_id = @connection_id;
		SET @target_user_id = @u2;

		UPDATE cn_connections
		SET rating1 = @rating,
			review1 = @review,
			review1_date = GETUTCDATE()
		WHERE connection_id = @connection_id;
	END
	ELSE
	BEGIN
		SELECT @old_rating = rating2 FROM cn_connections WHERE connection_id = @connection_id;
		SET @target_user_id = @u1;

		UPDATE cn_connections
		SET rating2 = @rating,
			review2 = @review,
			review2_date = GETUTCDATE()
		WHERE connection_id = @connection_id;
	END;

	-- Reputation: apply the rating delta to the subject's ranking, then refresh
	-- their rolling average across all reviews they have received.
	UPDATE cn_users
	SET ranking = COALESCE(ranking, 0) + @rating - COALESCE(@old_rating, 0)
	WHERE user_id = @target_user_id;

	UPDATE cn_users
	SET average_rating = (
		SELECT COALESCE(AVG(CAST(r AS FLOAT)), 0)
		FROM (
			SELECT rating1 AS r FROM cn_connections
			WHERE user2 = @target_user_id AND confirmed = 1 AND rating1 IS NOT NULL
			UNION ALL
			SELECT rating2 AS r FROM cn_connections
			WHERE user1 = @target_user_id AND confirmed = 1 AND rating2 IS NOT NULL
		) AS received_ratings
	)
	WHERE user_id = @target_user_id;

	SELECT 0 AS ErrorCode, 'Review updated successfully.' AS Message;
END
