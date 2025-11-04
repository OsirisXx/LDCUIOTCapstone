-- Check for sign-out record created at 02:00:48 on today's date
-- Run this query to verify if the sign-out record was created

-- First, get the USERID for HARLEY S. BUSA
SELECT USERID, FIRSTNAME, LASTNAME, STUDENTID
FROM USERS
WHERE STUDENTID = '20202422310'
   OR (FIRSTNAME LIKE '%HARLEY%' AND LASTNAME LIKE '%BUSA%');

-- Then check for sign-out records created today around 02:00:48
-- Replace 'YOUR_USERID_HERE' with the actual USERID from above
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
  AND DATE(ar.SCANDATETIME) = CURDATE()
  AND TIME(ar.SCANDATETIME) >= '02:00:00'
ORDER BY ar.SCANDATETIME DESC
LIMIT 10;

-- Check specifically for time_out records today
SELECT 
    COUNT(*) as signout_count,
    MIN(SCANDATETIME) as first_signout,
    MAX(SCANDATETIME) as latest_signout,
    GROUP_CONCAT(SCANDATETIME ORDER BY SCANDATETIME DESC SEPARATOR ', ') as all_signouts
FROM ATTENDANCERECORDS
WHERE USERID = 'YOUR_USERID_HERE'  -- Replace with actual USERID
  AND DATE(SCANDATETIME) = CURDATE()
  AND SCANTYPE = 'time_out';

