CREATE TABLE [dbo].[cn_geonames_cities](
	[geonameid] [int] NOT NULL,
	[name] [nvarchar](200) NOT NULL,
	[asciiname] [nvarchar](200) NULL,
	[alternatenames] [nvarchar](max) NULL,
	[latitude] [decimal](9, 6) NULL,
	[longitude] [decimal](9, 6) NULL,
	[feature_class] [nvarchar](1) NOT NULL,
	[feature_code] [nvarchar](10) NOT NULL,
	[country_code] [nvarchar](2) NOT NULL,
	[cc2] [nvarchar](200) NULL,
	[admin1_code] [nvarchar](20) NULL,
	[admin2_code] [nvarchar](80) NULL,
	[admin3_code] [nvarchar](20) NULL,
	[admin4_code] [nvarchar](20) NULL,
	[population] [int] NOT NULL,
	[elevation] [int] NULL,
	[dem] [int] NOT NULL,
	[timezone] [nvarchar](40) NOT NULL,
	[modification_date] [date] NOT NULL,
 CONSTRAINT [PK_cn_geonames_cities] PRIMARY KEY CLUSTERED 
(
	[geonameid] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

/****** Object:  Index [IX_cn_geonames_cities]    Script Date: 2024-12-26 11:33:16 PM ******/
CREATE NONCLUSTERED INDEX [IX_cn_geonames_cities] ON [dbo].[cn_geonames_cities]
(
	[country_code] ASC,
	[admin1_code] ASC,
	[name] ASC
)
INCLUDE([geonameid]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO


/****** Object:  Index [IX_cn_geonames_cities_latitude_longitude]    Script Date: 2024-12-26 11:33:30 PM ******/
CREATE NONCLUSTERED INDEX [IX_cn_geonames_cities_latitude_longitude] ON [dbo].[cn_geonames_cities]
(
	[latitude] ASC,
	[longitude] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO



