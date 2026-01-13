CREATE TABLE [dbo].[cn_companionita]
(
	[advice_id] INT NOT NULL PRIMARY KEY, 
    [date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
    [advice_text] NVARCHAR(MAX) NOT NULL
)
