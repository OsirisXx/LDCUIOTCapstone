-- Migration script to add new attendance statuses and scan types
-- for Early Arrival scenario support

USE iot_attendance;

-- Add new attendance statuses
ALTER TABLE ATTENDANCERECORDS 
MODIFY COLUMN STATUS ENUM('Present', 'Late', 'Absent', 'Early Arrival', 'Early Scan | Absent') NOT NULL;

-- Add new scan types
ALTER TABLE ATTENDANCERECORDS 
MODIFY COLUMN SCANTYPE ENUM('time_in', 'time_out', 'early_arrival', 'time_in_confirmation', 'early_arrival_upgraded', 'early_arrival_expired') NOT NULL;

-- Add index for early arrival cleanup queries
ALTER TABLE ATTENDANCERECORDS 
ADD INDEX idx_status_date (STATUS, DATE);

-- Show the updated table structure
DESCRIBE ATTENDANCERECORDS;

-- Test query to verify the changes
SELECT COLUMN_NAME, COLUMN_TYPE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = 'iot_attendance' 
AND TABLE_NAME = 'ATTENDANCERECORDS' 
AND COLUMN_NAME IN ('STATUS', 'SCANTYPE');








































