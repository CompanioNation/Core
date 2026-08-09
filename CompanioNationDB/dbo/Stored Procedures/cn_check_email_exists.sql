CREATE PROCEDURE cn_check_email_exists
    @email nvarchar(1024)
AS
BEGIN
    SET NOCOUNT ON;

    -- Check if the email exists on an active (non-deleted) account and return
    -- whether oauth login is required. Deleted accounts are excluded so the
    -- email can be re-registered by a new person.
    SELECT oauth_login
    FROM cn_users
    WHERE email = @email
      AND is_deleted = 0;

END;
