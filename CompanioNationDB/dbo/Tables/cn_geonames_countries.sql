CREATE TABLE [dbo].[cn_geonames_countries](
	[ISO] [nvarchar](50) NOT NULL,
	[ISO3] [nvarchar](50) NOT NULL,
	[ISO_Numeric] [int] NOT NULL,
	[fips] [nvarchar](50) NULL,
	[Country] [nvarchar](50) NOT NULL,
	[Capital] [nvarchar](50) NULL,
	[Area_in_sq_km] [float] NOT NULL,
	[Population] [int] NOT NULL,
	[Continent] [nvarchar](50) NOT NULL,
	[tld] [nvarchar](50) NULL,
	[CurrencyCode] [nvarchar](50) NULL,
	[CurrencyName] [nvarchar](50) NULL,
	[Phone] [nvarchar](50) NULL,
	[Postal_Code_Format] [nvarchar](100) NULL,
	[Postal_Code_Regex] [nvarchar](200) NULL,
	[Languages] [nvarchar](200) NULL,
	[geonameid] [int] NOT NULL,
	[neighbours] [nvarchar](50) NULL,
	[EquivalentFipsCode] [nvarchar](50) NULL,
 CONSTRAINT [PK_cn_geonames_countries] PRIMARY KEY CLUSTERED 
(
	[ISO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

/****** Object:  Index [IX_cn_geonames_countries]    Script Date: 2024-12-26 11:32:31 PM ******/
CREATE NONCLUSTERED INDEX [IX_cn_geonames_countries] ON [dbo].[cn_geonames_countries]
(
	[ISO] ASC
)
INCLUDE([Country]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO




