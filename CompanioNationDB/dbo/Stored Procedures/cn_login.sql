-- =============================================
-- Author:		<Author,,Name>
-- Create date: <Create Date,,>
-- Description:	<Description,,>
-- =============================================
CREATE PROCEDURE [dbo].[cn_login]
	-- Add the parameters for the stored procedure here
	@email varchar(1024),
	@password varchar(1024),
	@ip_address varchar(50),
	@oauth_login bit = 0,
	@email_verified bit = 0
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

	-- Insert statements for procedure here
	declare @user_id int
	declare @existing_oauth_login bit

	IF @oauth_login = 1
	BEGIN
		SET @user_id = (SELECT user_id FROM cn_users WHERE email = @email)
		IF @user_id is null BEGIN
			-- Create a new user
			EXEC cn_create_new_user 
				@email = @email,
				@password = @password,
				@ip_address = @ip_address,
				@oauth_login = 1;
			SET @user_id = (SELECT user_id FROM cn_users WHERE email = @email)
		END
		ELSE BEGIN
			-- An OAuth sign-in may only claim an existing account that was itself
			-- created through OAuth, unless the provider verified the email address.
			-- Otherwise a password account could be silently taken over by whoever
			-- controls the provider address.
			SET @existing_oauth_login = (SELECT oauth_login FROM cn_users WHERE user_id = @user_id)
			IF @existing_oauth_login = 0 AND @email_verified = 0
			BEGIN;
				THROW 100006, 'Email could not be verified by your sign-in provider. Sign in with your password instead.', 1;
			END
		END
	END
	ELSE
	BEGIN
		SET @user_id = (SELECT user_id FROM cn_users WHERE email = @email and password = @password)
	END

	IF @user_id is null BEGIN
		UPDATE cn_users SET failed_logins = failed_logins + 1 WHERE email = @email;
		THROW 100000, 'Invalid Credentials', 1;
	END
	
	-- Insert a GUID to keep track of the login state
	DECLARE @guid uniqueidentifier  
	SET @guid = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER)

	-- Also clear push_token: a fresh login invalidates any prior session on another
	-- device, and that prior device's push token MUST stop receiving notifications
	-- immediately (security: no notifications should go to a device with a stale
	-- login). The newly logged-in device will re-upload its own push_token via
	-- cn_update_push_token as soon as its client resubscribes.
	UPDATE cn_users SET login_token = @guid, failed_logins = 0, last_login = GETUTCDATE(), last_login_ip = @ip_address, push_token = '' WHERE user_id = @user_id

	-- Return the user details
	EXEC cn_get_user @user_id

END
