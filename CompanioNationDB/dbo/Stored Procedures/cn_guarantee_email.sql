CREATE PROCEDURE [dbo].[cn_guarantee_email]
    @login_token UNIQUEIDENTIFIER,
    @email VARCHAR(1024)
AS


        DECLARE @user_id INT;
        DECLARE @is_admin BIT;
        -- Check if the user exists
        SELECT @user_id = user_id, @is_admin = is_administrator
        FROM cn_users 
        WHERE login_token = @login_token;

        IF (@user_id IS NULL) 
        BEGIN;
            THROW 100000, 'Invalid Credentials', 1;
        END;


        DECLARE @verification_code UNIQUEIDENTIFIER = NULL;
        DECLARE @target_user INT;
        DECLARE @name VARCHAR(255);

        -- Extract the name from the email before the '@' symbol
        IF CHARINDEX('@', @email) = 0 THROW 50000, 'Invalid Email Format', 1;
        SET @name = SUBSTRING(@email, 1, CHARINDEX('@', @email) - 1);

        -- Check if the target user exists by email
        SET @target_user = (SELECT user_id FROM cn_users WHERE email = @email);

        IF @target_user IS NULL 
        BEGIN
            -- Insert a new user with the extracted name
            INSERT INTO cn_users (email, name, verification_code, searchable, gender)
            VALUES (@email, @name, CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER), 1, 1);  -- Modify columns and values as per your table schema

            SET @target_user = SCOPE_IDENTITY();
        END;

        -- TODO check this logic, make sure the ranking is only increased upon LINK confirmation and that the flow works, etc

        IF NOT EXISTS (SELECT 1 FROM cn_connections WHERE user1 = @user_id AND user2 = @target_user)
        BEGIN
            INSERT INTO cn_connections (user1, user2, confirmed)
            VALUES (@user_id, @target_user, @is_admin); -- If the user is an admin, the connection is automatically confirmed TODO ?? do I really want it like this?
            SET @verification_code = (SELECT verification_code FROM cn_connections WHERE user1 = @user_id AND user2 = @target_user);

            -- Increase the ranking for both the guarantor and the principal
            UPDATE cn_users 
            SET ranking = COALESCE(ranking, 0) + 1 
            WHERE user_id = @target_user OR user_id = @user_id;
        END;

        -- Return results
        SELECT @verification_code AS verification_code;

RETURN 0
