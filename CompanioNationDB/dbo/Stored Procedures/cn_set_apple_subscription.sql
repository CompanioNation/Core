CREATE PROCEDURE [dbo].[cn_set_apple_subscription]
    @email NVARCHAR(255),
    @expiry_date DATETIME,
    @apple_transaction_id NVARCHAR(255),
    @payment_system NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Validate parameters
    IF @email IS NULL OR @email = ''
        THROW 50001, 'Email is required', 1;
    
    IF @apple_transaction_id IS NULL OR @apple_transaction_id = ''
        THROW 50001, 'Apple transaction ID is required', 1;
    
    IF @payment_system IS NULL OR @payment_system = ''
        THROW 50001, 'Payment system is required', 1;
    
    -- Create user if they don't exist (OAuth Apple Sign In flow).
    -- Named parameters are REQUIRED: cn_create_new_user's leading parameters are
    -- @N/@T, so positional arguments would bind the email to @N and fail.
    IF NOT EXISTS (SELECT 1 FROM cn_users WHERE email = @email)
    BEGIN
        EXEC cn_create_new_user
            @email = @email,
            @password = '',
            @ip_address = '0.0.0.0',
            @oauth_login = 1;
    END
    
    -- Capture the PREVIOUS expiry so the caller can tell a real change from a replay.
    DECLARE @previous_expiry DATETIME;
    SELECT @previous_expiry = subscription_expiry FROM cn_users WHERE email = @email;

    -- Update subscription expiry, payment system, and Apple transaction ID
    UPDATE cn_users 
    SET 
        subscription_expiry = @expiry_date,
        payment_system = @payment_system,
        apple_original_transaction_id = @apple_transaction_id
    WHERE email = @email;

    IF @@ROWCOUNT = 0
        THROW 50002, 'User not found or update failed', 1;

    SELECT @previous_expiry AS previous_expiry;
END