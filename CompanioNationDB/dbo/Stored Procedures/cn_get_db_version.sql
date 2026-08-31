CREATE PROCEDURE [dbo].[cn_get_db_version]
AS
BEGIN
	SET NOCOUNT ON;

	-- cn_db_version is a view whose definition embeds the build version literal
	-- (generated at DACPAC build time). It is part of the schema, so extracting and
	-- re-applying the DACPAC carries the stamp along automatically.
	SELECT [schema_version]
	FROM [dbo].[cn_db_version];
END
