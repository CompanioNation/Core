CREATE TABLE [dbo].[cn_advice]
(
	[advice_id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
    [advice] NVARCHAR(MAX) NOT NULL, 
    [user_id] INT NOT NULL, 
    [prompt] NVARCHAR(MAX) NOT NULL DEFAULT '', 
    CONSTRAINT [FK_cn_advice_user_id] FOREIGN KEY (user_id) REFERENCES [cn_users]([user_id])
)
