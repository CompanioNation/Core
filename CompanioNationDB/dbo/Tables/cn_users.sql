CREATE TABLE [dbo].[cn_users] (
    [user_id]          INT              IDENTITY (1, 1) NOT NULL,
    [email]            NVARCHAR(255)   NOT NULL UNIQUE,
    [password]         NVARCHAR(255)   NULL,
    [is_administrator] BIT              NOT NULL DEFAULT 0,
    [login_token]             UNIQUEIDENTIFIER NULL,
    [date_created] DATETIME NOT NULL DEFAULT GETUTCDATE(), 
    [last_login] DATETIME NULL DEFAULT NULL, 
    [description] NVARCHAR(MAX) NOT NULL DEFAULT '', 
    [gender] INT NOT NULL DEFAULT 1, 
    [searchable] BIT NOT NULL DEFAULT 1, 
    [verification_code] UNIQUEIDENTIFIER NULL DEFAULT CAST(CRYPT_GEN_RANDOM(16) AS UNIQUEIDENTIFIER), 
    [verified] BIT NOT NULL DEFAULT 0, 
    [verification_code_timestamp] DATETIME NULL DEFAULT GETUTCDATE(),
    [ranking] INT NOT NULL DEFAULT 0, 
    [ip_address] VARCHAR(50) NULL, 
    [failed_logins] INT NOT NULL DEFAULT 0, 
    [name] NVARCHAR(50) NOT NULL DEFAULT '', 
    [bday] DATE NULL DEFAULT NULL, 
    [main_photo_id] INT NULL DEFAULT NULL, 
    [new_email] NVARCHAR(255) NULL DEFAULT NULL, 
    [old_email] NVARCHAR(255) NULL DEFAULT NULL, 
    [average_rating] FLOAT NOT NULL DEFAULT 0, 
    [push_token] NVARCHAR(1024) NOT NULL DEFAULT '', 
    [ineligible_for_contest] BIT NOT NULL DEFAULT 0, 
    [group_id] INT NULL DEFAULT NULL, 
    [geonameid] INT NULL DEFAULT NULL, 
    [oauth_login] BIT NOT NULL DEFAULT 0, 
    [last_login_ip] VARCHAR(50) NULL DEFAULT NULL, 
    [password_hash] NVARCHAR(512) NULL DEFAULT NULL, 
    [password_hash_version] INT NULL DEFAULT NULL, 
    [subscription_expiry] DATETIME NULL DEFAULT NULL, 
    CONSTRAINT [PK_cn_users] PRIMARY KEY CLUSTERED ([user_id] ASC), 
    CONSTRAINT [FK_main_photo] FOREIGN KEY ([main_photo_id]) REFERENCES [cn_images]([image_id]),
    CONSTRAINT [FK_geonames_cities] FOREIGN KEY ([geonameid]) REFERENCES [cn_geonames_cities]([geonameid])
    );

GO
CREATE NONCLUSTERED INDEX [IX_guid]
    ON [dbo].[cn_users]([login_token] ASC);

GO

CREATE INDEX IX_cn_users_gender_searchable_ranking ON cn_users (gender, searchable, ranking DESC);

GO

CREATE INDEX [IX_cn_users_average_rating] ON [dbo].[cn_users] ([average_rating] DESC);

GO

CREATE INDEX IX_cn_users_geonameid ON [dbo].[cn_users]([geonameid] ASC);
