-- Quick check for HARLEY S. BUSA sign-out on Nov 5, 2025
-- Run this query to see all attendance records for this student

-- First, get the USERID
SELECT USERID, FIRSTNAME, LASTNAME, STUDENTID
FROM USERS
WHERE STUDENTID = '20202422310';

-- Then use the USERID from above to check all attendance records
-- Replace 'YOUR_USERID_HERE' with the actual USERID from the query above
SELECT 
    ar.ATTENDANCEID,
    ar.SCANTYPE,
    ar.SCANDATETIME,
    ar.DATE,
    ar.STATUS,
    ar.ACTIONTYPE,
    ar.SCHEDULEID,
    ar.AUTHMETHOD,
    ar.LOCATION,
    CASE 
        WHEN ar.SCANTYPE = 'time_in' THEN '✅ SIGN IN'
        WHEN ar.SCANTYPE = 'time_out' THEN '✅✅ SIGN OUT'
        WHEN ar.SCANTYPE = 'early_arrival' THEN '⏰ EARLY ARRIVAL'
        WHEN ar.SCANTYPE = 'early_arrival_upgraded' THEN '⏰ EARLY ARRIVAL (UPGRADED)'
        WHEN ar.SCANTYPE = 'time_in_confirmation' THEN '✅ SIGN IN CONFIRMATION'
        ELSE ar.SCANTYPE
    END AS SCAN_DESCRIPTION,
    sub.SUBJECTCODE,
    cs.STARTTIME,
    r.ROOMNUMBER
FROM ATTENDANCERECORDS ar
LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
WHERE ar.USERID = 'YOUR_USERID_HERE'  -- Replace with actual USERID
  AND DATE(ar.SCANDATETIME) = '2025-11-05'
ORDER BY ar.SCANDATETIME;

-- Check specifically if sign-out exists
SELECT 
    COUNT(*) as signout_count,
    MAX(ar.SCANDATETIME) as latest_signout_time,
    GROUP_CONCAT(ar.SCANTYPE) as scan_types
FROM ATTENDANCERECORDS ar
WHERE ar.USERID = 'YOUR_USERID_HERE'  -- Replace with actual USERID
  AND DATE(ar.SCANDATETIME) = '2025-11-05'
  AND ar.SCANTYPE = 'time_out';

-- Summary: Check what scan types exist for this user on this date
SELECT 
    SCANTYPE,
    COUNT(*) as count,
    MIN(SCANDATETIME) as first_scan,
    MAX(SCANDATETIME) as last_scan
FROM ATTENDANCERECORDS
WHERE USERID = 'YOUR_USERID_HERE'  -- Replace with actual USERID
  AND DATE(SCANDATETIME) = '2025-11-05'
GROUP BY SCANTYPE
ORDER BY SCANTYPE;

