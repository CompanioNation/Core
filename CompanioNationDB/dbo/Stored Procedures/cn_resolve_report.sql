CREATE PROCEDURE [dbo].[cn_resolve_report]
    @login_token UNIQUEIDENTIFIER,
    @report_id INT,
    @status INT  -- 1=Reviewed, 2=ActionTaken, 3=Dismissed
AS
    -- Validate admin
    DECLARE @user_id INT;
    SELECT @user_id = user_id FROM cn_users WHERE login_token = @login_token AND is_administrator = 1;
    IF @user_id IS NULL
    BEGIN; THROW 400000, 'Unauthorized', 1; END;

    UPDATE cn_reports
    SET status = @status, reviewed_at = GETUTCDATE()
    WHERE report_id = @report_id;
RETURN 0
