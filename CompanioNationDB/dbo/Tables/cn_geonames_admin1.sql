CREATE TABLE [dbo].[cn_geonames_admin1](
	[code] [nvarchar](100) NOT NULL,
	[name] [nvarchar](100) NOT NULL,
	[name_ascii] [nvarchar](100) NOT NULL,
	[geonameid] [int] NOT NULL,
	[country_code] [nvarchar](2) NOT NULL,
	[admin1_code] [nvarchar](20) NOT NULL,
 CONSTRAINT [PK_cn_geonames_admin1] PRIMARY KEY CLUSTERED 
(
	[country_code] ASC,
	[admin1_code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

/****** Object:  Index [IX_cn_geonames_admin1]    Script Date: 2024-12-26 11:30:13 PM ******/
CREATE NONCLUSTERED INDEX [IX_cn_geonames_admin1] ON [dbo].[cn_geonames_admin1]
(
	[country_code] ASC,
	[admin1_code] ASC
)
INCLUDE([name]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO



