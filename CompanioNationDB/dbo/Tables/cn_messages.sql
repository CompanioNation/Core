CREATE TABLE [dbo].[cn_messages]
(
	[message_id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [from_user_id] INT NOT NULL, 
    [to_user_id] INT NOT NULL, 
    [message_text] NVARCHAR(MAX) NOT NULL, 
    [isread] BIT NOT NULL DEFAULT 0, 
    [date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
    [companionita] BIT NOT NULL DEFAULT 0, 
    CONSTRAINT [FK_cn_messages_fromuser] FOREIGN KEY ([from_user_id]) REFERENCES [cn_users]([user_id]),
    CONSTRAINT [FK_cn_messages_touser] FOREIGN KEY ([to_user_id]) REFERENCES [cn_users]([user_id])
)

GO

CREATE INDEX [IX_cn_messages_touser] ON [dbo].[cn_messages] ([to_user_id])

GO

CREATE INDEX [IX_cn_messages_fromuser] ON [dbo].[cn_messages] ([from_user_id])
