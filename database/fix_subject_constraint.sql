-- Fix SUBJECTS table unique constraint to allow multiple sections/instructors
-- This migration removes the overly restrictive unique constraint and creates a proper one

USE iot_attendance;

-- Drop the existing overly restrictive unique constraint (actual name is 'unique_subject')
ALTER TABLE SUBJECTS DROP INDEX unique_subject;

-- Make DESCRIPTION indexable (TEXT cannot be part of a unique key without prefix)
ALTER TABLE SUBJECTS MODIFY DESCRIPTION VARCHAR(255) NULL;

-- Allow NULL instructor for pages with missing faculty
ALTER TABLE SUBJECTS MODIFY INSTRUCTORID CHAR(36) NULL;

-- Create a new unique constraint that includes DESCRIPTION and INSTRUCTORID
-- This allows the same subject code to exist with different sections/instructors
-- Use a prefix on DESCRIPTION to stay under InnoDB index key length limits
ALTER TABLE SUBJECTS ADD UNIQUE KEY unique_subject_section_instructor (
    SUBJECTCODE, SEMESTER, ACADEMICYEAR, DESCRIPTION(150), INSTRUCTORID
);

-- Note: MySQL unique constraints treat NULL values as distinct, so multiple NULL INSTRUCTORID values are allowed
