CREATE TABLE [dbo].[cn_connections]
(
    [connection_id] INT IDENTITY(1,1) NOT NULL,
    [user1] INT NOT NULL, 
    [user2] INT NOT NULL, 
    [verification_code] UNIQUEIDENTIFIER NOT NULL DEFAULT CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER), 
    [date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
    [rating1] INT NULL, 
    [review1] NVARCHAR(MAX) NULL, 
    [review1_visible] BIT NULL, 
    [rating2] INT NULL, 
    [review2] NVARCHAR(MAX) NULL, 
    [review2_visible] BIT NULL, 
    [confirmed] BIT NOT NULL DEFAULT 0, 
    [link_type] INT NOT NULL DEFAULT 0,
    [expires_at] DATETIME NULL,
    [complaint] BIT NOT NULL DEFAULT 0,
    CONSTRAINT [PK_cn_connections] PRIMARY KEY ([user1], [user2]),
    CONSTRAINT [FK_cn_links_userid] FOREIGN KEY ([user1]) REFERENCES [cn_users]([user_id]),
    CONSTRAINT [FK_cn_links_knows_userid] FOREIGN KEY ([user2]) REFERENCES [cn_users]([user_id])
)

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_cn_connections_connection_id] ON [dbo].[cn_connections]([connection_id])
