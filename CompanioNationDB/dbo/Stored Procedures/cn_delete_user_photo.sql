CREATE PROCEDURE [dbo].[cn_delete_user_photo]
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
		DECLARE @image_guid UNIQUEIDENTIFIER;
		DECLARE @connection_id INT;
		DECLARE @subject_user_id INT;
		DECLARE @subject_confirmed BIT;

		SELECT @image_guid = image_guid,
			   @connection_id = connection_id,
			   @subject_user_id = user_id,
			   @subject_confirmed = subject_confirmed
		FROM cn_images
		WHERE image_id = @image_id;

		IF @image_guid IS NULL
		BEGIN
			THROW 500007, 'Photo not found', 1;
		END;

		-- Only the subject (person in the photo) or the uploader can delete
		IF @user_id != @subject_user_id
		BEGIN
			-- Check if the caller is the uploader of a LINK photo
			IF @connection_id IS NULL
			BEGIN
				THROW 500008, 'You can only delete your own photos', 1;
			END;

			DECLARE @u1 INT, @u2 INT;
			SELECT @u1 = user1, @u2 = user2
			FROM cn_connections
			WHERE connection_id = @connection_id;

			IF @u1 IS NULL
			BEGIN
				THROW 500007, 'Link connection no longer exists', 1;
			END;

			DECLARE @uploader_id INT = CASE WHEN @subject_user_id = @u1 THEN @u2 ELSE @u1 END;

			IF @user_id != @uploader_id
			BEGIN
				THROW 500008, 'You can only delete photos of yourself or photos you uploaded', 1;
			END;
		END;

		-- Delete the image record
		DELETE FROM cn_images WHERE image_id = @image_id;

		IF @@ROWCOUNT = 0
		BEGIN
			THROW 500007, 'Photo no longer exists', 1;
		END;

		-- Reverse karma for LINK photos if confirmed (karma was applied symmetrically)
		IF @connection_id IS NOT NULL AND @subject_confirmed = 1
		BEGIN
			-- Re-derive uploader for the karma reversal
			DECLARE @up_id INT;
			SELECT @u1 = user1, @u2 = user2
			FROM cn_connections
			WHERE connection_id = @connection_id;
			SET @up_id = CASE WHEN @subject_user_id = @u1 THEN @u2 ELSE @u1 END;

			UPDATE cn_users
			SET ranking = CASE WHEN ranking >= 2 THEN ranking - 2 ELSE 0 END
			WHERE user_id IN (@up_id, @subject_user_id);
		END;

		-- Return the image GUID for blob cleanup
		SELECT @image_guid AS image_guid;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH;
END
