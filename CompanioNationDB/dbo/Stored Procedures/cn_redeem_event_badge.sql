-- Redeems a signed badge QR into an award for the caller.
--   @issuer_user_id = 0/NULL  -> admin-origin: award a fresh instance (root).
--   @issuer_user_id > 0       -> peer propagation, governed by the badge's transfer_mode:
--                                 0 = None         (not transferable -> 50003)
--                                 1 = Multiplicative (issuer keeps theirs; caller gets a child copy)
--                                 2 = Singleton      (moved: removed from issuer, added to caller)
-- Returns the outcome ('awarded' | 'copied' | 'moved') plus the badge id/name.
CREATE PROCEDURE [dbo].[cn_redeem_event_badge]
	@login_token    UNIQUEIDENTIFIER,
	@badge_id       INT,
	@issuer_user_id INT = 0
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @caller_user_id INT;
	SELECT @caller_user_id = user_id
	FROM cn_users
	WHERE login_token = @login_token;

	IF (@caller_user_id IS NULL)
	BEGIN;
		THROW 100000, 'Invalid Credentials', 1;
	END;

	DECLARE @name NVARCHAR(100);
	DECLARE @icon NVARCHAR(50);
	DECLARE @icon_type NVARCHAR(10);
	DECLARE @icon_image_guid UNIQUEIDENTIFIER;
	DECLARE @transfer_mode TINYINT;
	DECLARE @is_active BIT;
	SELECT @name = name, @icon = icon, @icon_type = icon_type, @icon_image_guid = icon_image_guid,
		   @transfer_mode = transfer_mode, @is_active = is_active
	FROM cn_event_badges
	WHERE badge_id = @badge_id;

	IF (@name IS NULL)
	BEGIN;
		THROW 400005, 'Badge not found.', 1;
	END;

	IF (@is_active = 0)
	BEGIN;
		THROW 50003, 'This badge is no longer available.', 1;
	END;

	DECLARE @outcome NVARCHAR(20) = 'awarded';
	DECLARE @already_has BIT = CASE
		WHEN EXISTS (SELECT 1 FROM cn_user_badges WHERE user_id = @caller_user_id AND badge_id = @badge_id)
		THEN 1 ELSE 0 END;

	IF (@issuer_user_id IS NULL OR @issuer_user_id = 0)
	BEGIN
		-- Admin-origin QR: award a fresh instance to the caller.
		IF (@already_has = 0)
		BEGIN
			INSERT INTO cn_user_badges (user_id, badge_id, awarded_by, received_from_user_id, generation)
			VALUES (@caller_user_id, @badge_id, NULL, NULL, 0);
		END
		SET @outcome = 'awarded';
	END
	ELSE IF (@issuer_user_id = @caller_user_id)
	BEGIN
		-- A holder scanning their own code: no change at all. Critical for singleton,
		-- where a "transfer" deletes the giver's row — that must never be the caller.
		SET @outcome = 'awarded';
	END
	ELSE
	BEGIN
		-- Peer propagation: the issuer must currently hold the badge.
		IF NOT EXISTS (SELECT 1 FROM cn_user_badges WHERE user_id = @issuer_user_id AND badge_id = @badge_id)
		BEGIN;
			THROW 50003, 'The person sharing this badge no longer holds it.', 1;
		END;

		IF (@transfer_mode = 0)
		BEGIN;
			THROW 50003, 'This badge cannot be transferred.', 1;
		END;

		IF (@transfer_mode = 2)
		BEGIN
			-- Singleton: move the single instance to the caller (the giver loses it).
			DELETE FROM cn_user_badges WHERE user_id = @issuer_user_id AND badge_id = @badge_id;
			IF (@already_has = 0)
			BEGIN
				INSERT INTO cn_user_badges (user_id, badge_id, awarded_by, received_from_user_id, generation)
				VALUES (@caller_user_id, @badge_id, NULL, @issuer_user_id, 0);
			END
			SET @outcome = 'moved';
		END
		ELSE
		BEGIN
			-- Multiplicative: the issuer keeps their copy; the caller gets a child copy.
			IF (@already_has = 0)
			BEGIN
				DECLARE @parent_generation INT;
				SELECT @parent_generation = generation
				FROM cn_user_badges
				WHERE user_id = @issuer_user_id AND badge_id = @badge_id;

				INSERT INTO cn_user_badges (user_id, badge_id, awarded_by, received_from_user_id, generation)
				VALUES (@caller_user_id, @badge_id, NULL, @issuer_user_id, ISNULL(@parent_generation, 0) + 1);
			END
			SET @outcome = 'copied';
		END
	END

	SELECT @outcome AS outcome, @badge_id AS badge_id, @name AS name,
		   @icon AS icon, @icon_type AS icon_type, @icon_image_guid AS icon_image_guid;
END
GO
