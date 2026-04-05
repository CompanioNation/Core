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
