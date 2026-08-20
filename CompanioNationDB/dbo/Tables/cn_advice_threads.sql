CREATE TABLE [dbo].[cn_advice_threads]
(
	[thread_id] INT NOT NULL PRIMARY KEY IDENTITY(1, 1),
	[user_id] INT NOT NULL,
	[title] NVARCHAR(200) NULL,
	[date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(),
	[last_updated] DATETIME NOT NULL DEFAULT GETUTCDATE(),
	CONSTRAINT [FK_cn_advice_threads_user_id] FOREIGN KEY ([user_id]) REFERENCES [cn_users]([user_id])
);

GO
-- Thread listing is scoped to one user and sorted by recency.
CREATE NONCLUSTERED INDEX [IX_cn_advice_threads_user_last_updated]
	ON [dbo].[cn_advice_threads]([user_id] ASC, [last_updated] DESC);
