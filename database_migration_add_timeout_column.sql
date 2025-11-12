-- Migration: Add TimeOut column to ATTENDANCERECORDS table
-- Date: 2025-01-12
-- Purpose: Fix early sign-in time preservation issue by adding separate TimeOut column
--          Previously, sign-out was creating new records which overwrote the TimeIn value
--          Now, sign-out will update the existing record with TimeOut while preserving TimeIn

-- Add TimeOut column after TimeIn
ALTER TABLE ATTENDANCERECORDS 
ADD COLUMN TimeOut TIME DEFAULT NULL AFTER TimeIn;

-- Add index for faster lookups when finding records to update with sign-out times
CREATE INDEX idx_attendance_signout_lookup 
ON ATTENDANCERECORDS(USERID, DATE, SCHEDULEID, ACTIONTYPE, TIMEOUT);

-- Update existing sign-out records (if any) to populate TimeOut from TIMEIN
-- This handles any existing incorrect data where sign-out overwrote sign-in time
UPDATE ATTENDANCERECORDS 
SET TimeOut = TIMEIN,
    TIMEIN = NULL
WHERE ACTIONTYPE = 'Sign Out' 
  AND SCANTYPE = 'time_out'
  AND TIMEOUT IS NULL;

-- Note: After running this migration, the application will:
-- 1. Create sign-in records with TimeIn populated
-- 2. Update the same record with TimeOut when student signs out
-- 3. Preserve the original early arrival time in TimeIn column

