-- Lets the SUBJECT of a review control whether it is publicly visible on their
-- profile. Only the person the review is about can flip this flag — the reviewer
-- cannot (see cn_set_connection_review).
--
-- Two-way convention on cn_connections:
--   review1_visible = visibility of user1's review OF user2 (controlled by user2)
--   review2_visible = visibility of user2's review OF user1 (controlled by user1)
CREATE PROCEDURE [dbo].[cn_set_connection_review_visibility]
	@login_token UNIQUEIDENTIFIER,
	@connection_id INT,
	@is_visible BIT
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

	-- Caller is user1 → the review about them is user2's review (review2_visible).
	-- Caller is user2 → the review about them is user1's review (review1_visible).
	IF @user_id = @u1
	BEGIN
		UPDATE cn_connections
		SET review2_visible = @is_visible
		WHERE connection_id = @connection_id;
	END
	ELSE
	BEGIN
		UPDATE cn_connections
		SET review1_visible = @is_visible
		WHERE connection_id = @connection_id;
	END;

	SELECT 0 AS ErrorCode, 'Visibility updated successfully.' AS Message;
END
