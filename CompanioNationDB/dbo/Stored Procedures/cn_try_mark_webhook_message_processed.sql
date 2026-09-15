-- =============================================
-- Author:		CompanioNation Services
-- Create date: 2026
-- Description:	Claims a provider webhook message ID for at-least-once delivery
--              de-duplication. Returns is_new = 1 the first time a message ID is
--              seen (the caller should process it) and is_new = 0 on a redelivery
--              (the caller should skip it). Rows older than 7 days (the provider
--              redelivery window) are pruned opportunistically so the ledger stays tiny.
--
--              Only a duplicate-key violation (2627/2601) is swallowed; any other
--              error is re-raised so a genuine failure is never mistaken for
--              "already processed" (which would silently drop a real event).
-- =============================================
CREATE PROCEDURE [dbo].[cn_try_mark_webhook_message_processed]
	@message_id NVARCHAR(255),
	@provider   NVARCHAR(50)
AS
BEGIN
	SET NOCOUNT ON;

	IF @message_id IS NULL OR @message_id = ''
		THROW 50001, 'Message ID is required', 1;

	IF @provider IS NULL OR @provider = ''
		THROW 50001, 'Provider is required', 1;

	-- Opportunistic prune: drop rows past the provider's redelivery window.
	DELETE FROM cn_processed_webhook_messages
	WHERE date_created < DATEADD(DAY, -7, GETUTCDATE());

	DECLARE @is_new BIT = 0;

	BEGIN TRY
		INSERT INTO cn_processed_webhook_messages (message_id, provider)
		VALUES (@message_id, @provider);

		SET @is_new = 1;
	END TRY
	BEGIN CATCH
		-- 2627 = unique/primary-key constraint, 2601 = unique index. Anything else is a
		-- real failure and must surface, not be treated as a duplicate.
		IF ERROR_NUMBER() NOT IN (2627, 2601)
			THROW;
	END CATCH

	SELECT @is_new AS is_new;
END
