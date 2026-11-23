-- =============================================
-- Durable outbound email queue.
--
-- Every email the app sends is INSERTed here and returned from, so no request
-- thread ever waits on Azure Communication Services. A single background daemon
-- (EmailQueueDaemon) claims rows one at a time, sends them, and records the outcome.
--
-- The row stores the FULLY RENDERED bodies (unsubscribe footer, List-Unsubscribe
-- headers, absolute site links) because those are derived from host-only state
-- (Email.LinkSecret, Util.SiteBaseUrl) that must be resolved once, at enqueue
-- time, on the host that produced the message.
--
-- status: 0 = Pending, 1 = Sent, 2 = DeadLettered
--
-- Claiming is a VISIBILITY TIMEOUT, not a lease: cn_email_claim pushes
-- next_attempt_at forward by the lease window and leaves attempt_count alone, so a
-- crash mid-send costs the email nothing. cn_email_complete counts the attempt once
-- its outcome is known, and checks that the claimed deadline has not been replaced.
-- The row never leaves status 0 while it is in flight, so a
-- crash mid-send simply leaves it due again once that window passes — there is no
-- stranded "Sending" state to reclaim. Delivery is at-least-once: a duplicate send
-- is possible and accepted, a lost email is not.
-- =============================================
CREATE TABLE [dbo].[cn_email_queue] (
	[email_id]        BIGINT         IDENTITY (1, 1) NOT NULL,
	[recipient]       NVARCHAR (255) NOT NULL,
	[subject]         NVARCHAR (512) NOT NULL,
	[body_text]       NVARCHAR (MAX) NOT NULL,
	[body_html]       NVARCHAR (MAX) NULL,
	[headers]         NVARCHAR (MAX) NULL,
	[status]          TINYINT        NOT NULL DEFAULT 0,
	[attempt_count]   INT            NOT NULL DEFAULT 0,
	[next_attempt_at] DATETIME       NOT NULL DEFAULT GETUTCDATE(),
	[last_error]      NVARCHAR (512) NULL,
	[date_created]    DATETIME       NOT NULL DEFAULT GETUTCDATE(),
	[date_sent]       DATETIME       NULL,
	CONSTRAINT [PK_cn_email_queue] PRIMARY KEY CLUSTERED ([email_id] ASC)
);

GO

-- The only index the hot path needs: the claim scan. Filtered so it stays
-- proportional to the backlog rather than to the (mostly Sent) table.
CREATE INDEX [IX_cn_email_queue_pending]
	ON [dbo].[cn_email_queue] ([next_attempt_at] ASC, [email_id] ASC)
	WHERE [status] = 0;

GO
