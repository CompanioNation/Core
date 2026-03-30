-- DEPRECATED: This SP is replaced by cn_get_linked_users.
-- Kept for backward compatibility during transition.
CREATE PROCEDURE [dbo].[cn_get_guaranteed_users]
    @login_token UNIQUEIDENTIFIER
AS
BEGIN
    -- This procedure is deprecated. Use cn_get_linked_users instead.
    THROW 50003, 'cn_get_guaranteed_users is deprecated. Use cn_get_linked_users instead.', 1;
END;
