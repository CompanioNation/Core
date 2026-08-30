CREATE TABLE [dbo].[cn_event_badges] (
	[badge_id]     INT            IDENTITY (1, 1) NOT NULL,
	[name]         NVARCHAR(100)  NOT NULL,
	[description]  NVARCHAR(500)  NOT NULL DEFAULT '',
	[icon]         NVARCHAR(50)   NOT NULL DEFAULT N'🏅',
	[date_created] DATETIME       NOT NULL DEFAULT GETUTCDATE(),
	CONSTRAINT [PK_cn_event_badges] PRIMARY KEY CLUSTERED ([badge_id] ASC)
);

GO

CREATE TABLE [dbo].[cn_user_badges] (
	[user_badge_id] INT      IDENTITY (1, 1) NOT NULL,
	[user_id]       INT      NOT NULL,
	[badge_id]      INT      NOT NULL,
	[awarded_by]    INT      NULL,
	[date_awarded]  DATETIME NOT NULL DEFAULT GETUTCDATE(),
	CONSTRAINT [PK_cn_user_badges] PRIMARY KEY CLUSTERED ([user_badge_id] ASC),
	CONSTRAINT [FK_cn_user_badges_user] FOREIGN KEY ([user_id]) REFERENCES [dbo].[cn_users]([user_id]),
	CONSTRAINT [FK_cn_user_badges_badge] FOREIGN KEY ([badge_id]) REFERENCES [dbo].[cn_event_badges]([badge_id]),
	CONSTRAINT [FK_cn_user_badges_awarded_by] FOREIGN KEY ([awarded_by]) REFERENCES [dbo].[cn_users]([user_id]),
	CONSTRAINT [UQ_cn_user_badges] UNIQUE ([user_id], [badge_id])
);

GO
