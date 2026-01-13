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
	@oauth_login bit = 0
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

    -- Insert statements for procedure here
	declare @user_id int

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

	UPDATE cn_users SET login_token = @guid, failed_logins = 0, last_login = GETUTCDATE(), last_login_ip = @ip_address WHERE user_id = @user_id

	-- Return the user details
	EXEC cn_get_user @user_id

END
