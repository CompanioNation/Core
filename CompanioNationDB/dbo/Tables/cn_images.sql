CREATE TABLE [dbo].cn_images
(
	[image_id] INT NOT NULL PRIMARY KEY IDENTITY, 
	[image_guid] UNIQUEIDENTIFIER NOT NULL, 
	[connection_id] INT NULL, 
	[date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
	[user_id] INT NOT NULL, 
	[rating] INT NULL DEFAULT NULL, 
	[review] NVARCHAR(MAX) NULL DEFAULT NULL, 
	[image_visible] BIT NOT NULL DEFAULT 1, 
	[review_visible] BIT NOT NULL DEFAULT 0, 
	[ip_address] NVARCHAR(50) NULL, 
	CONSTRAINT [FK_cn_images_user] FOREIGN KEY ([user_id]) REFERENCES [cn_users]([user_id]),
	CONSTRAINT [FK_cn_images_connection] FOREIGN KEY ([connection_id]) REFERENCES [cn_connections]([connection_id])
)

GO

CREATE INDEX [IX_cn_images_users] ON [dbo].[cn_images] ([user_id])

GO

CREATE INDEX [IX_cn_images_connection] ON [dbo].[cn_images] ([connection_id])
