CREATE TABLE [dbo].[cn_reports]
(
    [report_id] INT NOT NULL PRIMARY KEY IDENTITY,
    [reporter_user_id] INT NOT NULL,
    [reported_user_id] INT NOT NULL,
    [report_type] INT NOT NULL,
    [report_reason] INT NOT NULL,
    [report_detail] NVARCHAR(500) NULL,
    [reference_id] INT NULL,
    [status] INT NOT NULL DEFAULT 0,
    [created_at] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    [reviewed_at] DATETIME NULL,
    CONSTRAINT [FK_cn_reports_reporter] FOREIGN KEY ([reporter_user_id]) REFERENCES [cn_users]([user_id]),
    CONSTRAINT [FK_cn_reports_reported] FOREIGN KEY ([reported_user_id]) REFERENCES [cn_users]([user_id])
)

GO
-- Speeds the unresolved-report counts used by the admin moderation/scan queue
-- (cn_admin_get_profiles aggregates over reported_user_id filtered by status = 0).
CREATE NONCLUSTERED INDEX [IX_cn_reports_reported_status]
    ON [dbo].[cn_reports] ([reported_user_id] ASC, [status] ASC);
