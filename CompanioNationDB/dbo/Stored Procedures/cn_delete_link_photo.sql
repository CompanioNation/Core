CREATE PROCEDURE [dbo].[cn_delete_link_photo]
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
        DECLARE @u1 INT;
        DECLARE @u2 INT;

        SELECT @image_guid = image_guid, @connection_id = connection_id,
               @subject_user_id = user_id, @subject_confirmed = subject_confirmed
        FROM cn_images
        WHERE image_id = @image_id;

        IF @image_guid IS NULL
        BEGIN
            THROW 500007, 'Photo not found', 1;
        END;

        IF @connection_id IS NULL
        BEGIN
            THROW 500007, 'This is not a LINK photo', 1;
        END;

        -- Get connection users to verify uploader
        SELECT @u1 = user1, @u2 = user2
        FROM cn_connections
        WHERE connection_id = @connection_id;

        IF @u1 IS NULL
        BEGIN
            THROW 500007, 'Link connection no longer exists', 1;
        END;

        -- The uploader is the one who is NOT the subject
        -- subject = user_id in cn_images, so uploader is the other user in the connection
        DECLARE @uploader_id INT = CASE WHEN @subject_user_id = @u1 THEN @u2 ELSE @u1 END;

        IF @user_id != @uploader_id
        BEGIN
            THROW 500008, 'You can only delete photos you uploaded', 1;
        END;

        -- Delete the image record (conditional guard prevents silent no-op on concurrent deletion)
        DELETE FROM cn_images WHERE image_id = @image_id;

        IF @@ROWCOUNT = 0
        BEGIN
            THROW 500007, 'Photo no longer exists', 1;
        END;

        -- Reverse karma only if photo was confirmed (karma was applied)
        IF @subject_confirmed = 1
        BEGIN
            UPDATE cn_users
            SET ranking = CASE WHEN ranking >= 2 THEN ranking - 2 ELSE 0 END
            WHERE user_id IN (@uploader_id, @subject_user_id);
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
