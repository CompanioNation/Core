CREATE PROCEDURE cn_generate_verification_code
    @Email NVARCHAR(1024)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewCode NVARCHAR(50);
    SET @NewCode = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER); -- Generate a new unique verification code

    -- Update the user's verification code and timestamp if the email exists.
    -- Also clear any staged email change: this proc issues a SIGNUP or PASSWORD-RESET
    -- code (never an email-change code), so a pending new_email would otherwise leave the
    -- new signup code rejected by cn_check_verification_code's `new_email IS NULL` guard.
    UPDATE [dbo].[cn_users]
    SET 
        [verification_code] = @NewCode,
        [verification_code_timestamp] = GETUTCDATE(),
        [new_email] = NULL
    WHERE [email] = @Email;

    -- Check if the row was affected (i.e., if the user exists)
    IF @@ROWCOUNT = 0
    BEGIN;
        -- If no row was affected, raise an error (or handle as needed)
        THROW 50002, 'User not found with the specified email address.', 1;
    END

    -- Return the new verification code
    SELECT @NewCode AS VerificationCode;
END
GO
