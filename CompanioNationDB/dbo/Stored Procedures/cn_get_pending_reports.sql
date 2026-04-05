CREATE PROCEDURE [dbo].[cn_get_pending_reports]
    @login_token UNIQUEIDENTIFIER
AS
    -- Validate admin
    DECLARE @user_id INT;
    SELECT @user_id = user_id FROM cn_users WHERE login_token = @login_token AND is_administrator = 1;
    IF @user_id IS NULL
    BEGIN; THROW 400000, 'Unauthorized', 1; END;

    SELECT
        r.report_id,
        r.reporter_user_id,
        reporter.name AS reporter_name,
        r.reported_user_id,
        reported.name AS reported_name,
        r.report_type,
        r.report_reason,
        r.report_detail,
        r.reference_id,
        r.status,
        r.created_at
    FROM cn_reports r
    INNER JOIN cn_users reporter ON reporter.user_id = r.reporter_user_id
    INNER JOIN cn_users reported ON reported.user_id = r.reported_user_id
    WHERE r.status = 0
    ORDER BY r.created_at ASC;
RETURN 0
