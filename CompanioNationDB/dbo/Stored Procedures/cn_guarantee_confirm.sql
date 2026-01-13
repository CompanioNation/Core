CREATE PROCEDURE [dbo].[cn_guarantee_confirm]
	@verification_code UNIQUEIDENTIFIER
AS

    DECLARE @user_id INT;
    DECLARE @target_user INT;
    SELECT @user_id = user1, @target_user = user2
    FROM cn_connections
    WHERE verification_code = @verification_code;

    -- Make sure the verification code exists
    IF (@user_id IS NULL OR @target_user IS NULL) RETURN 1;


    UPDATE cn_connections
    SET confirmed = 1
    WHERE verification_code = @verification_code;

    -- Merge the two groups, because these two users know each other, so we can guarantee they are real
    DECLARE @g1 INT;
    DECLARE @g2 INT;
    SET @g1 = (SELECT group_id FROM cn_users WHERE user_id = @user_id);
    SET @g2 = (SELECT group_id FROM cn_users WHERE user_id = @target_user);
    UPDATE cn_users  
        SET group_id = CASE 
            WHEN @g1 < @g2 THEN @g1 
            ELSE @g2 
        END
    WHERE cn_users.group_id = @g1 OR cn_users.group_id = @g2;
             


RETURN 0
