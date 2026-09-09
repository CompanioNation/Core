-- =============================================
-- Description:	Server-side fetch of a user's message history for AI scam
--              classification (admin-gated at the hub boundary, or invoked by the
--              automatic post-report job). Returns both sides of every conversation
--              involving the target (newest first, capped), WITHOUT marking read.
--              Intentionally permissionless: no client call path maps to this
--              directly; only trusted server code reaches it.
-- =============================================
CREATE PROCEDURE [dbo].[cn_get_user_messages_for_classification]
	@target_user_id INT,
	@max_messages INT = 200
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM cn_users WHERE user_id = @target_user_id)
	BEGIN;
		THROW 400001, 'User not found.', 1;
	END;

	IF @max_messages <= 0 OR @max_messages > 500
		SET @max_messages = 200;

	SELECT TOP (@max_messages)
		m.message_id,
		m.from_user_id,
		m.to_user_id,
		m.message_text,
		m.isread,
		m.date_created,
		from_u.name AS from_user_name,
		to_u.name AS to_user_name,
		m.companionita
	FROM cn_messages m
	INNER JOIN cn_users from_u ON m.from_user_id = from_u.user_id
	INNER JOIN cn_users to_u ON m.to_user_id = to_u.user_id
	WHERE m.from_user_id = @target_user_id OR m.to_user_id = @target_user_id
	ORDER BY m.message_id DESC;
END
GO
