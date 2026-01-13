CREATE PROCEDURE cn_check_email_exists
    @email nvarchar(1024)
AS
BEGIN
    SET NOCOUNT ON;

    -- Check if the email exists in the Users table and return whether oauth login is required 
    SELECT oauth_login
    FROM cn_users
    WHERE email = @email;

END;
