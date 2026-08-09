CREATE PROCEDURE cn_create_new_user
    @N int = 5,
    @T int = 10,
    @email nvarchar(1024),
    @password nvarchar(1024) = NULL,
    @password_hash nvarchar(512) = NULL,
    @password_hash_version int = NULL,
    @ip_address varchar(50),
    @oauth_login bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @existing_user_id INT;
    DECLARE @is_deleted BIT = 0;

    -- Does an account with this email already exist?
    SELECT @existing_user_id = user_id,
           @is_deleted = is_deleted
    FROM cn_users
    WHERE email = @email;

    -- Active account with this email → reject
    IF @existing_user_id IS NOT NULL AND @is_deleted = 0
    BEGIN
        ;THROW 100005, 'An account with this email address already exists.', 1;
    END

    -- Check if the IP address has created more than N accounts within the last T minutes
    IF (SELECT COUNT(*) FROM cn_users WHERE ip_address = @ip_address AND date_created > DATEADD(MINUTE, -@T, GETUTCDATE())) >= @N
    BEGIN
        ;THROW 100004, 'Too many accounts created from this IP address. Please try again later.', 1;
    END

    IF @existing_user_id IS NOT NULL AND @is_deleted = 1
    BEGIN
        -- Reactivate a previously deleted account so the email can be reused
        -- by a new person. We UPDATE in place because FK relationships
        -- (cn_messages, cn_images, cn_reports, cn_connections, etc.)
        -- prevent deleting the old row.
        UPDATE cn_users
        SET email                       = @email,
            password                    = @password,
            password_hash               = @password_hash,
            password_hash_version       = @password_hash_version,
            is_administrator            = 0,
            login_token                 = NULL,
            date_created                = GETUTCDATE(),
            last_login                  = NULL,
            description                 = '',
            gender                      = 1,
            searchable                  = 1,
            verification_code           = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER),
            verified                    = 0,
            verification_code_timestamp = GETUTCDATE(),
            ranking                     = 0,
            ip_address                  = @ip_address,
            failed_logins               = 0,
            [name]                      = '',
            bday                        = NULL,
            new_email                   = NULL,
            old_email                   = NULL,
            average_rating              = 0,
            push_token                  = '',
            ineligible_for_contest      = 0,
            group_id                    = NULL,
            geonameid                   = NULL,
            oauth_login                 = @oauth_login,
            last_login_ip               = NULL,
            subscription_expiry         = NULL,
            seo_clicks                  = 0,
            link_complaints             = 0,
            accepted_terms_version      = NULL,
            is_muted                    = 0,
            is_deleted                  = 0,
            payment_system              = NULL,
            apple_original_transaction_id = NULL,
            google_purchase_token       = NULL,
            microsoft_transaction_id    = NULL
        WHERE user_id = @existing_user_id;

        -- Reset group_id to the user's own id
        UPDATE cn_users SET group_id = @existing_user_id WHERE user_id = @existing_user_id;

        IF @oauth_login = 0
        BEGIN
            SELECT [verification_code] FROM cn_users WHERE user_id = @existing_user_id;
        END;

        RETURN;
    END

    -- Insert the new user into the cn_users table
    INSERT INTO cn_users (email, password, password_hash, password_hash_version, ip_address, oauth_login)
    VALUES (@email, @password, @password_hash, @password_hash_version, @ip_address, @oauth_login);

    -- Set the initial group id to match the user id, so that the user exists in his own little island until verified by someone else
    UPDATE cn_users SET group_id = SCOPE_IDENTITY() WHERE user_id = SCOPE_IDENTITY();

    IF @oauth_login = 0 
    BEGIN
        -- Return the ID of the newly created user, but only if we aren't using OAUTH
        --  because the OAUTH creation comes from cn_login which has to return the user data, not the verification_code
        SELECT [verification_code] FROM cn_users WHERE user_id = SCOPE_IDENTITY();
    END;

END;
