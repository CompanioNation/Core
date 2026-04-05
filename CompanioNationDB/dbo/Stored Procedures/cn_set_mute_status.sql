CREATE PROCEDURE [dbo].[cn_set_mute_status]
    @login_token UNIQUEIDENTIFIER,
    @target_user_id INT,
    @is_muted BIT
AS
    -- Validate admin
    DECLARE @admin_id INT;
    SELECT @admin_id = user_id FROM cn_users WHERE login_token = @login_token AND is_administrator = 1;
    IF @admin_id IS NULL
    BEGIN; THROW 400000, 'Unauthorized', 1; END;

    -- Cannot mute yourself
    IF @admin_id = @target_user_id
    BEGIN; THROW 400002, 'Cannot mute yourself.', 1; END;

    -- Set the mute status
    UPDATE cn_users
    SET is_muted = @is_muted
    WHERE user_id = @target_user_id;

    IF @@ROWCOUNT = 0
    BEGIN; THROW 400001, 'User not found.', 1; END;
RETURN 0
