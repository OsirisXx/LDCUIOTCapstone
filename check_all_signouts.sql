-- Check if ANY students have sign-out records for Nov 5, 2025
-- This helps verify if sign-out functionality is working at all

-- Check total sign-out records for this date
SELECT 
    COUNT(*) as total_signouts,
    COUNT(DISTINCT USERID) as students_with_signout
FROM ATTENDANCERECORDS
WHERE DATE(SCANDATETIME) = '2025-11-05'
  AND SCANTYPE = 'time_out';

-- List all students who signed out on Nov 5, 2025
SELECT 
    u.FIRSTNAME,
    u.LASTNAME,
    u.STUDENTID,
    ar.SCANDATETIME as signout_time,
    ar.SCHEDULEID,
    ar.ACTIONTYPE,
    sub.SUBJECTCODE
FROM ATTENDANCERECORDS ar
JOIN USERS u ON ar.USERID = u.USERID
LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
WHERE DATE(ar.SCANDATETIME) = '2025-11-05'
  AND ar.SCANTYPE = 'time_out'
ORDER BY ar.SCANDATETIME;

-- Compare sign-in vs sign-out counts for this date
SELECT 
    'Sign In' as scan_type,
    COUNT(*) as total_records,
    COUNT(DISTINCT USERID) as unique_students
FROM ATTENDANCERECORDS
WHERE DATE(SCANDATETIME) = '2025-11-05'
  AND SCANTYPE IN ('time_in', 'time_in_confirmation', 'early_arrival', 'early_arrival_upgraded')
UNION ALL
SELECT 
    'Sign Out' as scan_type,
    COUNT(*) as total_records,
    COUNT(DISTINCT USERID) as unique_students
FROM ATTENDANCERECORDS
WHERE DATE(SCANDATETIME) = '2025-11-05'
  AND SCANTYPE = 'time_out';

