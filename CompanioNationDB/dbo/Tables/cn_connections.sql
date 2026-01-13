CREATE TABLE [dbo].[cn_connections]
(
    [user1] INT NOT NULL, 
    [user2] INT NOT NULL, 
    [verification_code] UNIQUEIDENTIFIER NOT NULL DEFAULT CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER), 
    [date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
    [rating1] INT NULL, 
    [review1] NVARCHAR(MAX) NULL, 
    [review1_visible] BIT NULL, 
    [rating2] NCHAR(10) NULL, 
    [review2] NCHAR(10) NULL, 
    [review2_visible] NCHAR(10) NULL, 
    [confirmed] BIT NOT NULL DEFAULT 0, 
    CONSTRAINT [PK_cn_connections] PRIMARY KEY ([user1], [user2]),
    CONSTRAINT [FK_cn_links_userid] FOREIGN KEY ([user1]) REFERENCES [cn_users]([user_id]),
    CONSTRAINT [FK_cn_links_knows_userid] FOREIGN KEY ([user2]) REFERENCES [cn_users]([user_id])
)
