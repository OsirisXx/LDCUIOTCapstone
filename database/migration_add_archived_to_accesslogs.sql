-- Add ARCHIVED columns to ACCESSLOGS table
-- This migration adds archive support to ACCESSLOGS so they can be properly filtered
-- when archiving attendance records

USE iot_attendance;

-- Add ARCHIVED_AT column
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ACCESSLOGS' AND COLUMN_NAME = 'ARCHIVED_AT');
SET @sql = IF(@col_exists = 0, 
    'ALTER TABLE ACCESSLOGS ADD COLUMN ARCHIVED_AT TIMESTAMP NULL DEFAULT NULL', 
    'SELECT "Column ARCHIVED_AT already exists in ACCESSLOGS"');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Add ARCHIVED_BY column
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ACCESSLOGS' AND COLUMN_NAME = 'ARCHIVED_BY');
SET @sql = IF(@col_exists = 0, 
    'ALTER TABLE ACCESSLOGS ADD COLUMN ARCHIVED_BY CHAR(36) NULL DEFAULT NULL', 
    'SELECT "Column ARCHIVED_BY already exists in ACCESSLOGS"');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Add ARCHIVE_REASON column
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ACCESSLOGS' AND COLUMN_NAME = 'ARCHIVE_REASON');
SET @sql = IF(@col_exists = 0, 
    'ALTER TABLE ACCESSLOGS ADD COLUMN ARCHIVE_REASON TEXT NULL DEFAULT NULL', 
    'SELECT "Column ARCHIVE_REASON already exists in ACCESSLOGS"');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Create index for better query performance (only if it doesn't exist)
SET @index_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS 
    WHERE TABLE_SCHEMA = DATABASE() 
    AND TABLE_NAME = 'ACCESSLOGS' 
    AND INDEX_NAME = 'idx_accesslogs_archived');
SET @sql = IF(@index_exists = 0, 
    'CREATE INDEX idx_accesslogs_archived ON ACCESSLOGS(ARCHIVED_AT)', 
    'SELECT "Index idx_accesslogs_archived already exists"');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

