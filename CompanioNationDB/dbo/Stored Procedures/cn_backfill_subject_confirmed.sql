-- Backfill subject_confirmed for existing LINK photos.
-- Run once after adding subject_confirmed column to cn_images.
-- Sets subject_confirmed = 1 for all LINK photos (connection_id IS NOT NULL)
-- created before @before_date, since karma was already applied under the old flow.
--
-- Self-uploaded photos (connection_id IS NULL) are unaffected.
--
-- @before_date: If NULL, backfills all rows (use for initial migration).
--   If provided, only backfills photos created before that date, preventing
--   accidental confirmation of post-deployment unconfirmed photos.
--
-- Usage: EXEC [dbo].[cn_backfill_subject_confirmed]                     -- all rows
--        EXEC [dbo].[cn_backfill_subject_confirmed] @before_date = '2025-01-01'

CREATE PROCEDURE [dbo].[cn_backfill_subject_confirmed]
    @before_date DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE cn_images
    SET subject_confirmed = 1
    WHERE connection_id IS NOT NULL
      AND subject_confirmed = 0
      AND (@before_date IS NULL OR date_created < @before_date);

    SELECT @@ROWCOUNT AS RowsUpdated;
END
