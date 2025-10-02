-- Fix EMAIL column to allow null values for students while keeping it for admin login
-- This script makes EMAIL nullable and removes the UNIQUE constraint

USE iot_attendance;

-- First, let's check what indexes exist on the EMAIL column
SHOW INDEX FROM USERS WHERE Column_name = 'EMAIL';

-- Remove the UNIQUE constraint on EMAIL (try different possible names)
-- The constraint might be named differently, so we'll try common variations
ALTER TABLE USERS DROP INDEX IF EXISTS EMAIL;
ALTER TABLE USERS DROP INDEX IF EXISTS users_email_unique;
ALTER TABLE USERS DROP INDEX IF EXISTS idx_email;

-- Alternative: Find and drop the unique constraint by checking all indexes
-- This will show us what indexes exist:
SHOW INDEX FROM USERS;

-- Make EMAIL column nullable (remove NOT NULL constraint)
ALTER TABLE USERS MODIFY COLUMN EMAIL VARCHAR(100) NULL;

-- For MySQL 8.0+, we can create a functional unique index that only applies to non-null values
-- This ensures admin emails are still unique while allowing multiple null values for students
-- CREATE UNIQUE INDEX idx_email_unique ON USERS ((CASE WHEN EMAIL IS NOT NULL THEN EMAIL ELSE NULL END));

-- For older MySQL versions, we'll need to handle uniqueness at the application level
-- or create a separate table for admin users with email requirements
