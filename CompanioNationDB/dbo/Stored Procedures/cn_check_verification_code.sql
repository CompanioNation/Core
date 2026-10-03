CREATE PROCEDURE [dbo].[cn_check_verification_code]
    @verification_code VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    -- Validating the emailed code is the moment the account becomes verified.
    -- Clearing the code makes the link single-use.
    --
    -- SECURITY: the same verification_code column is used by BOTH the signup
    -- verification flow and the email-change flow (cn_request_email_change stages a
    -- new_email and re-uses this column). A code that belongs to a staged email change
    -- must NOT verify the account here — that would let an unverified user point at any
    -- address, request a change to a mailbox they control, and then verify their ORIGINAL
    -- (unproven) account using the change code. So only verify when no email change is
    -- pending (new_email IS NULL). Email-change codes are confirmed via cn_confirm_email_change.
    UPDATE dbo.cn_users
    SET verified = 1,
        verification_code = NULL,
        verification_code_timestamp = NULL
    WHERE verification_code = @verification_code
      AND new_email IS NULL
      AND DATEDIFF(MINUTE, verification_code_timestamp, GETUTCDATE()) <= 60;

    IF @@ROWCOUNT = 0
    BEGIN;
        THROW 50001, 'Invalid or expired verification code.', 1;
    END
END
