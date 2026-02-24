-- Migration script to allow NULL SCHEDULEID in ATTENDANCERECORDS table
-- This enables custodians and deans without schedules to have attendance records
-- without requiring a schedule reference

-- Find the foreign key constraint name dynamically
SET @fk_name = (
    SELECT CONSTRAINT_NAME
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'ATTENDANCERECORDS'
      AND COLUMN_NAME = 'SCHEDULEID'
      AND REFERENCED_TABLE_NAME = 'CLASSSCHEDULES'
    LIMIT 1
);

-- Drop the foreign key constraint if it exists
SET @drop_fk = IF(@fk_name IS NOT NULL,
    CONCAT('ALTER TABLE ATTENDANCERECORDS DROP FOREIGN KEY ', @fk_name),
    'SELECT "Foreign key constraint not found or already dropped"'
);
PREPARE stmt FROM @drop_fk;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Modify the SCHEDULEID column to allow NULL
ALTER TABLE ATTENDANCERECORDS 
MODIFY COLUMN SCHEDULEID CHAR(36) NULL;

-- Re-add the foreign key constraint (NULL values won't violate the constraint)
ALTER TABLE ATTENDANCERECORDS 
ADD CONSTRAINT attendancerecords_scheduleid_fk
FOREIGN KEY (SCHEDULEID) REFERENCES CLASSSCHEDULES(SCHEDULEID) 
ON DELETE CASCADE;

-- Verify the change
SELECT 
    COLUMN_NAME,
    IS_NULLABLE,
    COLUMN_TYPE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'ATTENDANCERECORDS'
  AND COLUMN_NAME = 'SCHEDULEID';
