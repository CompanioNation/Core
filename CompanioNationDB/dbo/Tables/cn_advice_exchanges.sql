CREATE TABLE [dbo].[cn_advice_exchanges]
(
	[exchange_id] INT NOT NULL PRIMARY KEY IDENTITY(1, 1),
	[thread_id] INT NOT NULL,
	[user_id] INT NOT NULL,
	[prompt] NVARCHAR(MAX) NOT NULL,
	[response] NVARCHAR(MAX) NULL,
	[date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(),
	CONSTRAINT [FK_cn_advice_exchanges_thread_id] FOREIGN KEY ([thread_id]) REFERENCES [cn_advice_threads]([thread_id]),
	CONSTRAINT [FK_cn_advice_exchanges_user_id] FOREIGN KEY ([user_id]) REFERENCES [cn_users]([user_id])
);

GO
-- Every exchange read is scoped to (user, thread) and ordered by exchange_id.
CREATE NONCLUSTERED INDEX [IX_cn_advice_exchanges_user_thread]
	ON [dbo].[cn_advice_exchanges]([user_id] ASC, [thread_id] ASC, [exchange_id] ASC);
