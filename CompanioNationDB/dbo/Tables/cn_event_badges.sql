CREATE TABLE [dbo].[cn_event_badges] (
	[badge_id]         INT            IDENTITY (1, 1) NOT NULL,
	[name]             NVARCHAR(100)  NOT NULL,
	[description]      NVARCHAR(500)  NOT NULL DEFAULT '',
	[icon]             NVARCHAR(50)   NOT NULL DEFAULT N'🏅',
	-- 'emoji' => icon holds the glyph; 'image' => icon_image_guid names a blob in the badge container.
	[icon_type]        NVARCHAR(10)   NOT NULL DEFAULT 'emoji',
	[icon_image_guid]  UNIQUEIDENTIFIER NULL,
	[is_active]        BIT            NOT NULL DEFAULT 1,
	-- When 0 the badge still affects search but is never rendered to non-admin/non-owner viewers.
	[is_visible]       BIT            NOT NULL DEFAULT 1,
	-- Signed contribution folded into search ranking (negative sinks, positive boosts).
	[search_weight]    INT            NOT NULL DEFAULT 0,
	-- When 1 the badge is offered as a "must have" filter in the find-companion search.
	[is_search_filter] BIT            NOT NULL DEFAULT 0,
	-- 0 = None (admin-only, non-transferable), 1 = Multiplicative (holder copies, builds a tree),
	-- 2 = Singleton (hand-to-hand move; only admins issue new instances).
	[transfer_mode]    TINYINT        NOT NULL DEFAULT 0,
	[date_created]     DATETIME       NOT NULL DEFAULT GETUTCDATE(),
	CONSTRAINT [PK_cn_event_badges] PRIMARY KEY CLUSTERED ([badge_id] ASC)
);

GO

CREATE TABLE [dbo].[cn_user_badges] (
	[user_badge_id]         INT      IDENTITY (1, 1) NOT NULL,
	[user_id]               INT      NOT NULL,
	[badge_id]              INT      NOT NULL,
	[awarded_by]            INT      NULL,
	-- The user this badge was received from: the admin when admin-assigned, or the
	-- transferring holder for peer propagation. Enables the multiplicative badge tree.
	[received_from_user_id] INT      NULL,
	-- Depth in the multiplicative propagation tree (0 for a root award or a singleton).
	[generation]            INT      NOT NULL DEFAULT 0,
	[date_awarded]          DATETIME NOT NULL DEFAULT GETUTCDATE(),
	CONSTRAINT [PK_cn_user_badges] PRIMARY KEY CLUSTERED ([user_badge_id] ASC),
	CONSTRAINT [FK_cn_user_badges_user] FOREIGN KEY ([user_id]) REFERENCES [dbo].[cn_users]([user_id]),
	CONSTRAINT [FK_cn_user_badges_badge] FOREIGN KEY ([badge_id]) REFERENCES [dbo].[cn_event_badges]([badge_id]),
	CONSTRAINT [FK_cn_user_badges_awarded_by] FOREIGN KEY ([awarded_by]) REFERENCES [dbo].[cn_users]([user_id]),
	CONSTRAINT [FK_cn_user_badges_received_from] FOREIGN KEY ([received_from_user_id]) REFERENCES [dbo].[cn_users]([user_id]),
	CONSTRAINT [UQ_cn_user_badges] UNIQUE ([user_id], [badge_id])
);

GO
