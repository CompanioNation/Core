CREATE TABLE [dbo].[cn_suburbs] (
    [suburb_id] INT IDENTITY(1,1) PRIMARY KEY,
    [suburb_name] NVARCHAR(255) NOT NULL UNIQUE, 
    [is_city] BIT NOT NULL DEFAULT 1, 
    [indent_level] INT NOT NULL DEFAULT 2, 
    [order_index] INT NULL
);

GO

CREATE INDEX [IX_cn_suburbs_order] ON [dbo].[cn_suburbs] ([order_index])
