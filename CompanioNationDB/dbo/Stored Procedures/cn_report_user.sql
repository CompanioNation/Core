CREATE PROCEDURE [dbo].[cn_report_user]
    @login_token UNIQUEIDENTIFIER,
    @reported_user_id INT,
    @report_type INT,
    @report_reason INT,
    @report_detail NVARCHAR(500) = NULL,
    @reference_id INT = NULL
AS
    DECLARE @reporter_user_id INT;
    SELECT @reporter_user_id = user_id FROM cn_users WHERE login_token = @login_token;
    IF @reporter_user_id IS NULL
    BEGIN; THROW 100000, 'Invalid Credentials', 1; END;

    -- Prevent self-reporting
    IF @reporter_user_id = @reported_user_id
    BEGIN; THROW 50008, 'You cannot report yourself', 1; END;

    -- Prevent duplicate reports (same reporter + reported + type + reference within 24 hours)
    IF EXISTS (
        SELECT 1 FROM cn_reports
        WHERE reporter_user_id = @reporter_user_id
          AND reported_user_id = @reported_user_id
          AND report_type = @report_type
          AND ((@reference_id IS NULL AND reference_id IS NULL) OR reference_id = @reference_id)
          AND created_at > DATEADD(HOUR, -24, GETUTCDATE())
    )
    BEGIN; THROW 50007, 'You have already reported this content', 1; END;

    INSERT INTO cn_reports (reporter_user_id, reported_user_id, report_type, report_reason, report_detail, reference_id)
    VALUES (@reporter_user_id, @reported_user_id, @report_type, @report_reason, @report_detail, @reference_id);

    -- Immediate karma penalty: each report costs -5 ranking
    UPDATE cn_users
    SET ranking = CASE WHEN ranking >= 5 THEN ranking - 5 ELSE 0 END
    WHERE user_id = @reported_user_id;

    SELECT SCOPE_IDENTITY() AS report_id;
RETURN 0
