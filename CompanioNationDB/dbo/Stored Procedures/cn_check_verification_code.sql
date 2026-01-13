CREATE PROCEDURE [dbo].[cn_check_verification_code]
    @verification_code VARCHAR(50)
AS
	
	
    SET NOCOUNT ON;

    SELECT * FROM dbo.cn_users 
    WHERE verification_code = @verification_code
      AND DATEDIFF(MINUTE, verification_code_timestamp, GETUTCDATE()) <= 55;
      -- give a 5 minute grace period. 
      -- the code actually expires in 60 minutes, but they will need a few minutes to pick a password.

    -- Check if the update was successful
    IF @@ROWCOUNT = 0
    BEGIN;
        -- If no rows were updated, the code might be invalid or expired
        THROW 50001, 'Invalid or expired verification code.', 1;
    END

RETURN 0
