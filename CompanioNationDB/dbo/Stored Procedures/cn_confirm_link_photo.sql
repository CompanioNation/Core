CREATE PROCEDURE [dbo].[cn_confirm_link_photo]
	@login_token UNIQUEIDENTIFIER,
	@image_id INT
AS
BEGIN
	SET NOCOUNT ON;
	BEGIN TRY
		BEGIN TRANSACTION;

		DECLARE @user_id INT;

		-- Validate login token
		SELECT @user_id = user_id
		FROM cn_users
		WHERE login_token = @login_token;

		IF @user_id IS NULL
		BEGIN
			THROW 100000, 'Invalid Credentials', 1;
		END;

		-- Get image details
		DECLARE @subject_user_id INT;
		DECLARE @connection_id INT;
		DECLARE @subject_confirmed BIT;

		SELECT @subject_user_id = user_id,
			   @connection_id = connection_id,
			   @subject_confirmed = subject_confirmed
		FROM cn_images
		WHERE image_id = @image_id;

		IF @subject_user_id IS NULL
		BEGIN
			THROW 500007, 'Photo not found', 1;
		END;

		IF @connection_id IS NULL
		BEGIN
			THROW 500007, 'This is not a LINK photo', 1;
		END;

		-- Only the subject (person depicted) can confirm
		IF @user_id != @subject_user_id
		BEGIN
			THROW 500008, 'Only the person in the photo can confirm it', 1;
		END;

		IF @subject_confirmed = 1
		BEGIN
			THROW 500007, 'Photo is already confirmed', 1;
		END;

		-- Mark as confirmed (conditional for race safety: two concurrent
		-- confirms, or a confirm racing a reject/delete, resolves to one winner)
		UPDATE cn_images
		SET subject_confirmed = 1
		WHERE image_id = @image_id AND subject_confirmed = 0;

		IF @@ROWCOUNT = 0
		BEGIN
			-- Row was deleted or already confirmed by a concurrent caller
			THROW 500007, 'Photo is already confirmed or no longer exists', 1;
		END;

		-- Apply +2 karma to both users in the connection
		DECLARE @u1 INT;
		DECLARE @u2 INT;

		SELECT @u1 = user1, @u2 = user2
		FROM cn_connections
		WHERE connection_id = @connection_id;

		IF @u1 IS NULL
		BEGIN
			THROW 500007, 'Link connection no longer exists', 1;
		END;

		UPDATE cn_users
		SET ranking = ranking + 2
		WHERE user_id IN (@u1, @u2);

		-- Return success
		SELECT 1 AS Confirmed;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH;
END
