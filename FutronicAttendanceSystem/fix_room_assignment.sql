-- Check current room assignments
SELECT * FROM ROOMS;

-- Check if there's a device record
SELECT * FROM devices;

-- Check the instructor's schedule
SELECT cs.SCHEDULEID, s.SUBJECTNAME, cs.STARTTIME, cs.ENDTIME, cs.ROOMID, r.ROOMNAME
FROM CLASSSCHEDULES cs
JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
WHERE s.INSTRUCTORID = (SELECT USERID FROM USERS WHERE EMAIL = 'harleyinstructor@gmail.com')
  AND cs.DAYOFWEEK = 'Friday'
  AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
  AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester');

-- Check current academic settings
SELECT SETTINGKEY, SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY IN ('current_academic_year', 'current_semester');















