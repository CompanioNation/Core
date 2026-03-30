-- DEPRECATED: This SP is replaced by cn_delete_link_photo.
-- Kept for backward compatibility during transition.
CREATE PROCEDURE cn_remove_guarantee
    @login_token UNIQUEIDENTIFIER,
    @image_id INT,
    @image_guid UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    -- This procedure is deprecated. Use cn_delete_link_photo instead.
    THROW 50003, 'cn_remove_guarantee is deprecated. Use cn_delete_link_photo instead.', 1;
END
GO
