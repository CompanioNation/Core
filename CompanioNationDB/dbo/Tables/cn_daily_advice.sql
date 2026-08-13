CREATE TABLE [dbo].[cn_daily_advice]
(
	[language_code] NVARCHAR(10) NOT NULL PRIMARY KEY, 
	[daily_advice] NVARCHAR(MAX) NOT NULL DEFAULT ''
)

