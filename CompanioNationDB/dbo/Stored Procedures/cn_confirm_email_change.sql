CREATE PROCEDURE [dbo].[cn_confirm_email_change]
	@login_token UNIQUEIDENTIFIER,
	@verification_code VARCHAR(50)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @user_id INT;
	DECLARE @new_email NVARCHAR(255);
	DECLARE @code_valid BIT = 0;

	-- Validate the login token and load the staged email change, verifying the
	-- code is present and still within its 60-minute expiry window.
	SELECT @user_id = user_id,
		   @new_email = new_email,
		   @code_valid = CASE
			   WHEN verification_code IS NOT NULL
				AND verification_code = @verification_code
				AND DATEDIFF(MINUTE, verification_code_timestamp, GETUTCDATE()) <= 60
			   THEN 1 ELSE 0 END
	FROM cn_users
	WHERE login_token = @login_token
	  AND is_deleted = 0;

	IF (@user_id IS NULL)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	IF (@new_email IS NULL)
	BEGIN;
		THROW 50001, 'No pending email change.', 1;
	END;

	IF (@code_valid = 0)
	BEGIN;
		THROW 50001, 'Invalid or expired verification code.', 1;
	END;

	-- Re-check uniqueness just before swapping. The email column has a UNIQUE
	-- constraint, so any existing row (active or soft-deleted) blocks the change.
	IF EXISTS (SELECT 1 FROM cn_users WHERE email = @new_email AND user_id <> @user_id)
	BEGIN;
		THROW 100005, 'An account with this email address already exists.', 1;
	END;

	BEGIN TRY
		UPDATE cn_users
		SET email = @new_email,
			new_email = NULL,
			verification_code = NULL,
			verification_code_timestamp = NULL,
			verified = 1
		WHERE user_id = @user_id;
	END TRY
	BEGIN CATCH
		-- 2627 = unique constraint violation caused by a race between the check
		-- above and the UPDATE.
		IF (ERROR_NUMBER() = 2627)
		BEGIN;
			THROW 100005, 'An account with this email address already exists.', 1;
		END;
		THROW;
	END CATCH;
END
