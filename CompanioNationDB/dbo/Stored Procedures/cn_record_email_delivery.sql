-- =============================================
-- Records the delivery outcome of an email send for the user with the given address.
-- Called (best-effort) by the email delivery pipeline once the send reaches a terminal
-- status, so an undeliverable address can be surfaced in the app and corrected.
--
-- State values: 0 = Unknown, 1 = Delivered, 2 = TransientFailure (4xx),
--               3 = PermanentFailure (5xx).
--
-- A NULL/empty address, or an address that matches no user (e.g. an email sent to a
-- non-member such as support@), is a no-op: this must never error.
-- =============================================
CREATE PROCEDURE [dbo].[cn_record_email_delivery]
	@email       NVARCHAR(255),
	@state       INT,
	@error       NVARCHAR(512) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF @email IS NULL OR LTRIM(RTRIM(@email)) = ''
		RETURN;

	UPDATE cn_users
	SET email_delivery_state     = @state,
		email_delivery_error     = @error,
		email_delivery_timestamp = GETUTCDATE()
	WHERE email = LTRIM(RTRIM(@email));
END;
