-- Migration script to ensure TIMEIN column exists and backfill data
-- This script ensures the TIMEIN column is properly set up for early arrival timestamp preservation

-- Ensure TIMEIN column exists and is properly typed
-- (Should already exist from schema, but verify)
ALTER TABLE ATTENDANCERECORDS 
MODIFY COLUMN TIMEIN TIME DEFAULT NULL;

-- Backfill any NULL TIMEIN values from SCANDATETIME
UPDATE ATTENDANCERECORDS
SET TIMEIN = TIME(SCANDATETIME)
WHERE TIMEIN IS NULL AND SCANDATETIME IS NOT NULL;

-- Add index for better performance on time-based queries
CREATE INDEX IF NOT EXISTS idx_attendancerecords_timein ON ATTENDANCERECORDS(TIMEIN);
CREATE INDEX IF NOT EXISTS idx_attendancerecords_scandatetime ON ATTENDANCERECORDS(SCANDATETIME);

-- Verify the migration
SELECT 
    COUNT(*) as total_records,
    COUNT(TIMEIN) as records_with_timein,
    COUNT(SCANDATETIME) as records_with_scandatetime
FROM ATTENDANCERECORDS;
