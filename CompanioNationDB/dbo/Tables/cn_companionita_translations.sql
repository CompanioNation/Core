CREATE TABLE [dbo].[cn_companionita_translations]
(
	[advice_id] INT NOT NULL, 
	[language_code] NVARCHAR(10) NOT NULL, 
	[advice_text] NVARCHAR(MAX) NOT NULL, 
	CONSTRAINT [PK_cn_companionita_translations] PRIMARY KEY ([advice_id], [language_code]), 
	CONSTRAINT [FK_cn_companionita_translations_advice] FOREIGN KEY ([advice_id]) REFERENCES [cn_companionita]([advice_id])
)

