-- Deduplication ledger for provider webhooks that deliver at-least-once.
-- Google Play RTDN (delivered via Cloud Pub/Sub push) can redeliver the same message,
-- which would otherwise re-run the verification API call, re-write the subscription, and
-- send a duplicate activation email. Recording each provider message ID once lets a
-- redelivery be recognised and skipped.
-- Rows older than the provider's redelivery window are pruned opportunistically by
-- cn_try_mark_webhook_message_processed (Pub/Sub stops redelivering after ~7 days).
CREATE TABLE [dbo].[cn_processed_webhook_messages] (
	[message_id]   NVARCHAR(255) NOT NULL,
	[provider]     NVARCHAR(50)  NOT NULL,
	[date_created] DATETIME      NOT NULL DEFAULT GETUTCDATE(),
	CONSTRAINT [PK_cn_processed_webhook_messages] PRIMARY KEY CLUSTERED ([message_id] ASC, [provider] ASC)
);

GO
