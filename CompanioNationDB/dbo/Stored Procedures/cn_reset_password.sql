CREATE PROCEDURE cn_reset_password
    @verification_code VARCHAR(50),
    @new_password VARCHAR(1024)
AS
BEGIN
    SET NOCOUNT ON;

    -- Ensure the verification code is still valid (e.g., within 1 hour)
    UPDATE dbo.cn_users
    SET password = @new_password,
        verification_code = NULL, -- Clear the verification code
        verified = 1, -- Mark the user as verified
        verification_code_timestamp = NULL,
        oauth_login = 0 -- Indicate that the user is no longer required to use OAuth login, because they now have a password
    WHERE verification_code = @verification_code
      AND DATEDIFF(MINUTE, verification_code_timestamp, GETUTCDATE()) <= 60;

    -- Check if the update was successful
    IF @@ROWCOUNT = 0
    BEGIN;
        -- If no rows were updated, the code might be invalid or expired
        THROW 50001, 'Invalid or expired verification code.', 1;
    END
END
