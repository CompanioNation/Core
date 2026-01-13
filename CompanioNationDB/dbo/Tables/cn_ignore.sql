CREATE TABLE [dbo].[cn_ignore]
(
	[ignore_id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [user_id] INT NOT NULL, 
    [user_id_to_ignore] INT NOT NULL, 
    [date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
    CONSTRAINT [FK_cn_ignore_user] FOREIGN KEY ([user_id]) REFERENCES [cn_users]([user_id]),
    CONSTRAINT [FK_cn_ignore_target] FOREIGN KEY ([user_id_to_ignore]) REFERENCES [cn_users]([user_id]), 
    CONSTRAINT [CK_cn_ignore_unique] UNIQUE (user_id, user_id_to_ignore)
)

GO
