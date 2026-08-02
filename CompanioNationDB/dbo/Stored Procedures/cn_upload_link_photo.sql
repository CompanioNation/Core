
CREATE PROCEDURE [dbo].[cn_upload_link_photo]
    @login_token UNIQUEIDENTIFIER,
    @connection_id INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id INT;
    DECLARE @u1 INT;
    DECLARE @u2 INT;
    DECLARE @confirmed BIT;
    DECLARE @subject_user_id INT;

    -- Validate login token
    SELECT @user_id = user_id
    FROM cn_users
    WHERE login_token = @login_token;

    IF @user_id IS NULL
    BEGIN
        THROW 100000, 'Invalid Credentials', 1;
    END;

    -- Validate connection exists and is confirmed
    SELECT @u1 = user1, @u2 = user2, @confirmed = confirmed
    FROM cn_connections
    WHERE connection_id = @connection_id;

    IF @u1 IS NULL
    BEGIN
        THROW 500007, 'Link not found', 1;
    END;

    IF @confirmed IS NULL OR @confirmed = 0
    BEGIN
        THROW 500007, 'Link not confirmed', 1;
    END;

    -- Verify the user is part of this connection
    IF @user_id != @u1 AND @user_id != @u2
    BEGIN
        THROW 500007, 'You are not part of this link', 1;
    END;

    -- The subject is the OTHER user (person being photographed)
    SET @subject_user_id = CASE WHEN @user_id = @u1 THEN @u2 ELSE @u1 END;

    -- Generate a new image GUID
    DECLARE @image_guid UNIQUEIDENTIFIER = CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER);

    -- Insert image record with connection_id, image_visible = 0 for LINK photos, subject_confirmed = 0
    INSERT INTO cn_images (user_id, image_guid, connection_id, image_visible, subject_confirmed)
    VALUES (@subject_user_id, @image_guid, @connection_id, 0, 0);

    -- Return the image GUID and subject info for blob upload and notification
    SELECT @image_guid AS image_guid, SCOPE_IDENTITY() AS image_id, @subject_user_id AS subject_user_id,
           (SELECT email FROM cn_users WHERE user_id = @subject_user_id) AS subject_email,
           (SELECT name FROM cn_users WHERE user_id = @subject_user_id) AS subject_name;
END
