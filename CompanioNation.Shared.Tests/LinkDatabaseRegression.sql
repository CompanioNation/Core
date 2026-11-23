-- Run only against the disposable cn_link_regression database after publishing the DACPAC.
SET NOCOUNT ON;
IF DB_NAME() <> 'cn_link_regression' THROW 51000, 'Use only the disposable cn_link_regression database.', 1;

DECLARE @token1 UNIQUEIDENTIFIER = NEWID(), @token2 UNIQUEIDENTIFIER = NEWID();
DECLARE @email1 VARCHAR(1024) = CONCAT('link1-', NEWID(), '@example.com');
DECLARE @email2 VARCHAR(1024) = CONCAT('link2-', NEWID(), '@example.com');
INSERT cn_users (email, name, verification_code, searchable, gender, login_token, ranking, verified)
VALUES (@email1, 'Link One', NEWID(), 1, 1, @token1, 0, 1);
DECLARE @user1 INT = SCOPE_IDENTITY();
INSERT cn_users (email, name, verification_code, searchable, gender, login_token, ranking, verified)
VALUES (@email2, 'Link Two', NEWID(), 1, 1, @token2, 0, 1);
DECLARE @user2 INT = SCOPE_IDENTITY();

CREATE TABLE #codes (verification_code UNIQUEIDENTIFIER);
INSERT #codes EXEC cn_link_email @token1, @email2;
DECLARE @code1 UNIQUEIDENTIFIER = (SELECT verification_code FROM #codes);
IF @code1 IS NULL THROW 51001, 'Missing first invite', 1;
DELETE #codes;
INSERT #codes EXEC cn_link_email @token1, @email2;
IF (SELECT verification_code FROM #codes) <> @code1 THROW 51002, 'Retry changed invite code', 1;
DELETE #codes;
INSERT #codes EXEC cn_link_email @token2, @email1;
DECLARE @code2 UNIQUEIDENTIFIER = (SELECT verification_code FROM #codes);
IF @code2 IS NULL OR @code1 = @code2 THROW 51003, 'Reverse invite code was reused', 1;
IF EXISTS (SELECT 1 FROM cn_users WHERE user_id IN (@user1, @user2) AND ranking <> 0)
	THROW 51004, 'Invite changed ranking', 1;
EXEC cn_confirm_link @code1;
EXEC cn_confirm_link @code1;
EXEC cn_confirm_link @code2;
IF (SELECT COUNT(*) FROM cn_connections WHERE confirmed = 1 AND ((user1 = @user1 AND user2 = @user2) OR (user1 = @user2 AND user2 = @user1))) <> 1
	THROW 51005, 'Pair has more than one confirmed LINK', 1;
IF EXISTS (SELECT 1 FROM cn_users WHERE user_id IN (@user1, @user2) AND ranking <> 2)
	THROW 51006, 'Confirmation ranking was not awarded exactly once', 1;
DELETE #codes;
INSERT #codes EXEC cn_link_email @token1, @email2;
IF (SELECT verification_code FROM #codes) IS NOT NULL THROW 51007, 'Confirmed LINK accepted a new invite', 1;

-- One person can invite
DECLARE @eventToken UNIQUEIDENTIFIER = NEWID();
INSERT cn_users (email, name, verification_code, searchable, gender, login_token, ranking, verified)
VALUES (CONCAT('event-', NEWID(), '@example.com'), 'Event Host', NEWID(), 1, 1, @eventToken, 0, 1);
DECLARE @eventUser INT = SCOPE_IDENTITY();
UPDATE cn_users SET link_complaints = 3 WHERE user_id = @eventUser;
DECLARE @i INT = 0, @attendee INT, @attendeeEmail VARCHAR(1024), @retryCode UNIQUEIDENTIFIER;
WHILE @i < 25
BEGIN
	SET @attendeeEmail = CONCAT('invite-', NEWID(), '@example.com');
	DELETE #codes;
	INSERT #codes EXEC cn_link_email @eventToken, @attendeeEmail;
	IF (SELECT verification_code FROM #codes) IS NULL THROW 51010, 'Email invite failed', 1;
	SET @retryCode = (SELECT verification_code FROM #codes);
	SET @i += 1;
END;
DELETE #codes;
INSERT #codes EXEC cn_link_email @eventToken, @attendeeEmail;
IF (SELECT verification_code FROM #codes) <> @retryCode THROW 51011, 'Resend failed after many invites', 1;
IF (SELECT COUNT(*) FROM cn_connections WHERE user1 = @eventUser AND link_type = 1) <> 25
	THROW 51012, 'Expected 25 distinct email invitations', 1;
IF (SELECT link_complaints FROM cn_users WHERE user_id = @eventUser) <> 3
	THROW 51014, 'Inviting unexpectedly changed the complaint counter', 1;

SET @i = 0;
WHILE @i < 25
BEGIN
	INSERT cn_users (email, name, verification_code, searchable, gender, login_token, ranking, verified)
	VALUES (CONCAT('qr-', NEWID(), '@example.com'), 'Event Attendee', NEWID(), 1, 1, NEWID(), 0, 1);
	SET @attendee = SCOPE_IDENTITY();
	EXEC cn_create_qr_link @eventToken, @attendee;
	SET @i += 1;
END;
IF (SELECT COUNT(*) FROM cn_connections WHERE (user1 = @eventUser OR user2 = @eventUser) AND link_type = 0 AND confirmed = 1) <> 25
	THROW 51013, 'Expected 25 confirmed QR LINKs', 1;

DECLARE @rejectToken UNIQUEIDENTIFIER = NEWID();
DECLARE @rejectEmail VARCHAR(1024) = CONCAT('reject-', NEWID(), '@example.com');
INSERT cn_users (email, name, verification_code, searchable, gender, login_token, ranking, verified)
VALUES (CONCAT('sender-', NEWID(), '@example.com'), 'Invite Sender', NEWID(), 1, 1, @rejectToken, 2, 1);
DECLARE @rejectSender INT = SCOPE_IDENTITY();
DELETE #codes;
INSERT #codes EXEC cn_link_email @rejectToken, @rejectEmail;
DECLARE @rejectCode UNIQUEIDENTIFIER = (SELECT verification_code FROM #codes);
EXEC cn_reject_link @rejectCode;
IF NOT EXISTS (SELECT 1 FROM cn_users WHERE user_id = @rejectSender AND link_complaints = 1 AND ranking = 1)
	THROW 51015, 'Rejecting an invite did not record the complaint and ranking penalty', 1;
PRINT 'LINK database regression checks passed';
