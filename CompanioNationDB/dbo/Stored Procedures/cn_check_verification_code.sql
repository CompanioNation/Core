CREATE PROCEDURE [dbo].[cn_check_verification_code]
    @verification_code VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    -- Validating the emailed code is the moment the account becomes verified.
    -- Clearing the code makes the link single-use.
    UPDATE dbo.cn_users
    SET verified = 1,
        verification_code = NULL,
        verification_code_timestamp = NULL
    WHERE verification_code = @verification_code
      AND DATEDIFF(MINUTE, verification_code_timestamp, GETUTCDATE()) <= 60;

    IF @@ROWCOUNT = 0
    BEGIN;
        THROW 50001, 'Invalid or expired verification code.', 1;
    END
END
