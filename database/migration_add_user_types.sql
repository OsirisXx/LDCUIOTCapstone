-- Migration to add 'custodian' and 'dean' user types to USERS table
-- Run this script to update your existing database

USE iot_attendance;

-- Add new user types to the USERTYPE ENUM
ALTER TABLE USERS 
MODIFY COLUMN USERTYPE ENUM('student', 'instructor', 'admin', 'custodian', 'dean') NOT NULL;

-- Verify the change
DESCRIBE USERS;
