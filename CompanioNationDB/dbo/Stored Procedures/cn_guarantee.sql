-- DEPRECATED: This SP is replaced by cn_create_qr_link and cn_link_email.
-- Kept for backward compatibility during transition.
CREATE PROCEDURE [dbo].[cn_guarantee]
    @login_token UNIQUEIDENTIFIER,
    @email VARCHAR(1024)
AS
BEGIN
    -- This procedure is deprecated. Use cn_create_qr_link or cn_link_email instead.
    THROW 50003, 'cn_guarantee is deprecated. Use LINK methods instead.', 1;
END;
