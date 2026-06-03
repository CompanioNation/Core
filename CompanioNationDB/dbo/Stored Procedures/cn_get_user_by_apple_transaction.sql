CREATE PROCEDURE [dbo].[cn_get_user_by_apple_transaction]
    @apple_transaction_id NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    
    IF @apple_transaction_id IS NULL OR @apple_transaction_id = ''
        THROW 50000, 'Apple transaction ID is required', 1;
    
    SELECT 
        user_id,
        email,
        subscription_expiry,
        payment_system
    FROM cn_users 
    WHERE apple_original_transaction_id = @apple_transaction_id;
END
