-- Simple fix for EMAIL column to allow null values
-- Run these commands one by one in your MySQL client

USE iot_attendance;

-- Step 1: Check what indexes exist on EMAIL column
SHOW INDEX FROM USERS WHERE Column_name = 'EMAIL';

-- Step 2: If there's a unique constraint, you'll need to drop it
-- The constraint name might be different, so check the output from Step 1
-- Common constraint names might be:
-- ALTER TABLE USERS DROP INDEX users_email_unique;
-- ALTER TABLE USERS DROP INDEX EMAIL;
-- ALTER TABLE USERS DROP INDEX idx_email;

-- Step 3: Make EMAIL column nullable
ALTER TABLE USERS MODIFY COLUMN EMAIL VARCHAR(100) NULL;

-- Step 4: Verify the change
DESCRIBE USERS;
