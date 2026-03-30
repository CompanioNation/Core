CREATE PROCEDURE [dbo].[cn_reject_link]
    @verification_code UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @user1 INT;
        DECLARE @user2 INT;
        DECLARE @confirmed BIT;
        DECLARE @expires_at DATETIME;
        DECLARE @complaint BIT;

        -- Look up the connection
        SELECT @user1 = user1, @user2 = user2, @confirmed = confirmed,
               @expires_at = expires_at, @complaint = complaint
        FROM cn_connections
        WHERE verification_code = @verification_code;

        IF @user1 IS NULL
        BEGIN
            THROW 500000, 'Link invitation not found', 1;
        END;

        -- Check if already rejected
        IF @complaint = 1
        BEGIN
            SELECT 'already_rejected' AS status;
            COMMIT TRANSACTION;
            RETURN;
        END;

        -- Check expiry
        IF @expires_at IS NOT NULL AND @expires_at < GETUTCDATE()
        BEGIN
            THROW 500000, 'Link invitation has expired', 1;
        END;

        -- Delete the connection
        DELETE FROM cn_connections
        WHERE verification_code = @verification_code;

        -- Increment link_complaints on the initiator (user1)
        UPDATE cn_users
        SET link_complaints = link_complaints + 1
        WHERE user_id = @user1;

        -- Decrement initiator ranking by 1
        UPDATE cn_users
        SET ranking = CASE WHEN ranking > 0 THEN ranking - 1 ELSE 0 END
        WHERE user_id = @user1;

        SELECT 'rejected' AS status;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
