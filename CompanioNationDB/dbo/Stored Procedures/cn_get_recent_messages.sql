CREATE PROCEDURE [dbo].[cn_get_recent_messages]
AS
	SELECT TOP (200) 
		u_from.name as name_from, u_to.name as name_to, m.message_text, m.date_created
	FROM cn_messages m, cn_users u_from, cn_users u_to
	WHERE m.from_user_id = u_from.user_id AND m.to_user_id = u_to.user_id
		AND m.date_created > DATEADD(day, -30, GETUTCDATE())
	ORDER BY message_id DESC

