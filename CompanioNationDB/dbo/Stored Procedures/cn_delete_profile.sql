CREATE PROCEDURE [dbo].[cn_delete_profile]
    @login_token UNIQUEIDENTIFIER,
    @target_user_id INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @caller_user_id INT;
        DECLARE @caller_is_admin BIT;

        -- Validate login token and capture the caller's admin flag
        SELECT @caller_user_id = user_id, @caller_is_admin = is_administrator
        FROM cn_users
        WHERE login_token = @login_token;

        IF (@caller_user_id IS NULL)
        BEGIN;
            THROW 100000, 'Invalid Credentials', 1;
        END;

        DECLARE @target INT = COALESCE(@target_user_id, @caller_user_id);

        -- A caller may delete their own account; anyone else requires admin.
        IF (@target != @caller_user_id AND @caller_is_admin = 0)
        BEGIN;
            THROW 400000, 'Unauthorized. Admin access required.', 1;
        END;

        IF NOT EXISTS (SELECT 1 FROM cn_users WHERE user_id = @target)
        BEGIN;
            THROW 400001, 'Profile not found.', 1;
        END;

        -- Delete every photo whose subject is the target user (regardless of
        -- uploader), reversing karma for confirmed LINK photos exactly as the
        -- single-photo delete path does. Deleted blob guids are collected and
        -- returned in a single result set for Azure cleanup by the caller.
        DECLARE @photos TABLE (
            image_id INT PRIMARY KEY,
            image_guid UNIQUEIDENTIFIER,
            connection_id INT NULL,
            subject_confirmed BIT
        );
        DECLARE @deleted_guids TABLE (image_guid UNIQUEIDENTIFIER);

        INSERT INTO @photos (image_id, image_guid, connection_id, subject_confirmed)
        SELECT image_id, image_guid, connection_id, subject_confirmed
        FROM cn_images
        WHERE user_id = @target;

        DECLARE @image_id INT;
        DECLARE @image_guid UNIQUEIDENTIFIER;
        DECLARE @connection_id INT;
        DECLARE @subject_confirmed BIT;
        DECLARE @u1 INT;
        DECLARE @u2 INT;

        WHILE EXISTS (SELECT 1 FROM @photos)
        BEGIN
            SELECT TOP (1)
                @image_id = image_id,
                @image_guid = image_guid,
                @connection_id = connection_id,
                @subject_confirmed = subject_confirmed
            FROM @photos;

            IF (@connection_id IS NOT NULL AND @subject_confirmed = 1)
            BEGIN
                SELECT @u1 = user1, @u2 = user2
                FROM cn_connections
                WHERE connection_id = @connection_id;

                IF (@u1 IS NOT NULL)
                BEGIN
                    UPDATE cn_users
                    SET ranking = CASE WHEN ranking >= 2 THEN ranking - 2 ELSE 0 END
                    WHERE user_id IN (@u1, @u2);
                END
            END

            DELETE FROM cn_images WHERE image_id = @image_id;
            INSERT INTO @deleted_guids (image_guid) VALUES (@image_guid);
            DELETE FROM @photos WHERE image_id = @image_id;
        END

        -- Return every deleted blob guid in one result set.
        SELECT image_guid FROM @deleted_guids;

        -- Scrub personal data, invalidate the session, mark deleted, and stamp
        -- last_login with the deletion time. ip_address is intentionally kept for
        -- abuse tracing; email/password are kept so the same person can reclaim
        -- the account via login (cn_login reactivates) or re-registration.
        UPDATE cn_users
        SET [name]                    = '',
            description               = '',
            gender                    = 1,
            bday                      = NULL,
            average_rating            = 0,
            geonameid                 = NULL,
            new_email                 = NULL,
            old_email                 = NULL,
            verification_code         = NULL,
            verification_code_timestamp = NULL,
            login_token               = NULL,
            push_token                = '',
            is_deleted                = 1,
            last_login                = GETUTCDATE()
        WHERE user_id = @target;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END

