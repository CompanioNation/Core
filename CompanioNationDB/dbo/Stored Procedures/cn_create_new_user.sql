CREATE PROCEDURE cn_create_new_user
    @N int = 5,
    @T int = 10,
    @email nvarchar(1024),
    @password nvarchar(1024),
    @ip_address varchar(50),
    @oauth_login bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF (SELECT COUNT(*) FROM cn_users WHERE email = @email) > 0
    BEGIN
        RAISERROR('An account with this email address already exists.', 16, 1);
        RETURN;
    END

    -- Check if the IP address has created more than N accounts within the last T minutes
    IF (SELECT COUNT(*) FROM cn_users WHERE ip_address = @ip_address AND date_created > DATEADD(MINUTE, -@T, GETUTCDATE())) >= @N
    BEGIN
        -- If the limit is exceeded, raise an error and exit
        RAISERROR('Too many accounts created from this IP address in the last %d minutes.', 16, 1, @T);
        -- TODO *** I should also add this IP to a blacklist table and have a job that clears it after a certain time
        RETURN;
    END

    -- Insert the new user into the cn_users table
    INSERT INTO cn_users (email, password, ip_address, oauth_login)
    VALUES (@email, @password, @ip_address, @oauth_login);

    -- Set the initial group id to match the user id, so that the user exists in his own little island until verified by someone else
    UPDATE cn_users SET group_id = SCOPE_IDENTITY() WHERE user_id = SCOPE_IDENTITY();

    IF @oauth_login = 0 
    BEGIN
        -- Return the ID of the newly created user, but only if we aren't using OAUTH
        --  because the OAUTH creation comes from cn_login which has to return the user data, not the verification_code
        SELECT [verification_code] FROM cn_users WHERE user_id = SCOPE_IDENTITY();
    END;

END;
