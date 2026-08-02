CREATE PROCEDURE [dbo].[cn_reject_link_photo]
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
		DECLARE @image_guid UNIQUEIDENTIFIER;

		SELECT @subject_user_id = user_id,
			   @connection_id = connection_id,
			   @subject_confirmed = subject_confirmed,
			   @image_guid = image_guid
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

		-- Only the subject (person depicted) can reject
		IF @user_id != @subject_user_id
		BEGIN
			THROW 500008, 'Only the person in the photo can reject it', 1;
		END;

		IF @subject_confirmed = 1
		BEGIN
			THROW 500007, 'Photo is already confirmed', 1;
		END;

		-- Determine the uploader (the OTHER user in the connection)
		DECLARE @u1 INT;
		DECLARE @u2 INT;

		SELECT @u1 = user1, @u2 = user2
		FROM cn_connections
		WHERE connection_id = @connection_id;

		IF @u1 IS NULL
		BEGIN
			THROW 500007, 'Link connection no longer exists', 1;
		END;

		DECLARE @uploader_id INT = CASE WHEN @subject_user_id = @u1 THEN @u2 ELSE @u1 END;

		-- Deduct 1 karma from uploader as penalty
		UPDATE cn_users
		SET ranking = CASE WHEN ranking >= 1 THEN ranking - 1 ELSE 0 END
		WHERE user_id = @uploader_id;

		-- Delete the image record (conditional for race safety — a concurrent
		-- confirm would have set subject_confirmed = 1, making this a no-op)
		DELETE FROM cn_images WHERE image_id = @image_id AND subject_confirmed = 0;

		IF @@ROWCOUNT = 0
		BEGIN
			-- Photo was confirmed by a concurrent caller or already deleted
			THROW 500007, 'Photo is already confirmed or no longer exists', 1;
		END;

		-- Return image_guid for blob cleanup
		SELECT @image_guid AS image_guid;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH;
END
