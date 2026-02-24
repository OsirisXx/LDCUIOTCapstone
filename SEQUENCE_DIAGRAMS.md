SEQUENCE DIAGRAMS DOCUMENTATION
IoT Attendance System - Liceo de Cagayan University

================================================================================
FIGURE 1. SEQUENCE DIAGRAM FOR FACULTY ROOM ACCESS AND SYSTEM ACTIVATION
VIA FINGERPRINT SCANNER
================================================================================

PARTICIPANTS: Faculty Member ----- User Interface ----- DATABASE ----- ESP32 Door Controller

Faculty Member ---- 1. Places finger for scanning (fingerprint/RFID) ------> User Interface ---- 2. Send identifier and auth_method -----> DATABASE

Faculty Member <------- 7. Displays "Access Granted" / "Session Started" ------- User Interface <------- 6. Returns session status and door status ------- DATABASE

Faculty Member <------- 8. Send "Unlock" Signal to door controller ------- User Interface

User Interface ---- 3. Query AUTHENTICATIONMETHODS and USERS tables -----> DATABASE

User Interface ---- 4. Validate schedule and check for active session -----> DATABASE

User Interface ---- 5. Create/Update SESSIONS record and ROOMS table -----> DATABASE

User Interface ---- 5a. Insert ATTENDANCERECORDS for instructor -----> DATABASE

User Interface ---- 5b. Insert ACCESSLOGS entry -----> DATABASE

DETAILED FLOW:

Faculty Member
    |
    | 1. Places finger for scanning (fingerprint/RFID)
    |
    v
User Interface (Backend API)
    |
    | 2. Send identifier, auth_method, room_id
    |
    v
DATABASE
    |
    | 3. Query AUTHENTICATIONMETHODS JOIN USERS
    |    WHERE IDENTIFIER = ? AND METHODTYPE = ? 
    |    AND ISACTIVE = TRUE AND STATUS = 'Active'
    |
    | 4. Validate schedule using ScheduleValidationService
    |    Check CLASSSCHEDULES for matching schedule
    |
    | 5. Check for active session in SESSIONS table
    |    WHERE SCHEDULEID = ? AND SESSIONDATE = CURDATE() 
    |    AND STATUS = 'active'
    |
    | IF NO ACTIVE SESSION:
    |   5a. INSERT INTO SESSIONS (start session)
    |   5b. UPDATE ROOMS SET DOORSTATUS = 'unlocked'
    |   5c. INSERT INTO ATTENDANCERECORDS (instructor time_in)
    |   5d. UPDATE Early Arrival records to Present
    |
    | IF ACTIVE SESSION EXISTS:
    |   5a. UPDATE SESSIONS SET STATUS = 'ended'
    |   5b. UPDATE ROOMS SET DOORSTATUS = 'locked'
    |   5c. INSERT INTO ATTENDANCERECORDS (instructor time_out)
    |
    | 5e. INSERT INTO ACCESSLOGS (log access attempt)
    |
    | 6. Return session status, door status, instructor info
    |
    v
User Interface
    |
    | 7. Display "Session Started" or "Session Ended"
    |    Display door status (unlocked/locked)
    |
    | 8. Send unlock signal to ESP32 door controller
    |    (if session started)
    |
    v
Faculty Member

================================================================================
FIGURE 2. SEQUENCE DIAGRAM FOR STUDENT ATTENDANCE RECORDING
================================================================================

PARTICIPANTS: Student ----- User Interface ----- DATABASE

Student ---- 1. Places finger for scanning ------> User Interface ---- 2. Send fingerprint identifier and room_id -----> DATABASE

Student <------- 7. Displays "Attendance Recorded" / "Access Granted" ------- User Interface <------- 6. Returns attendance record status ------- DATABASE

User Interface ---- 3. Query AUTHENTICATIONMETHODS and USERS tables -----> DATABASE

User Interface ---- 4. Validate student enrollment and active session -----> DATABASE

User Interface ---- 5. Check existing attendance and calculate late status -----> DATABASE

User Interface ---- 5a. Insert/Update ATTENDANCERECORDS -----> DATABASE

User Interface ---- 5b. Insert ACCESSLOGS entry -----> DATABASE

DETAILED FLOW:

Student
    |
    | 1. Places finger on internal fingerprint scanner
    |
    v
User Interface (Backend API)
    |
    | 2. Send identifier, auth_method='fingerprint', room_id
    |
    v
DATABASE
    |
    | 3. Query AUTHENTICATIONMETHODS JOIN USERS
    |    WHERE IDENTIFIER = ? AND METHODTYPE = 'fingerprint'
    |    AND ISACTIVE = TRUE AND STATUS = 'Active'
    |    Verify USERTYPE = 'student'
    |
    | 4. Validate using ScheduleValidationService
    |    Check if student is enrolled in subject
    |    Check if active session exists
    |    Verify session is active for attendance recording
    |
    | 5. Check existing attendance in ATTENDANCERECORDS
    |    WHERE USERID = ? AND SCHEDULEID = ? 
    |    AND DATE(SCANDATETIME) = CURDATE()
    |
    | IF EARLY ARRIVAL EXISTS:
    |   5a. UPDATE ATTENDANCERECORDS SET STATUS = 'Present'
    |       SET SCANTYPE = 'time_in_confirmation'
    |       SET LOCATION = 'inside'
    |
    | IF NO EXISTING ATTENDANCE:
    |   5a. Get session start time from SESSIONS table
    |   5b. Calculate late status (15 minutes tolerance)
    |   5c. INSERT INTO ATTENDANCERECORDS
    |       SET STATUS = 'Late' or 'Present'
    |       SET SCANTYPE = 'time_in'
    |       SET LOCATION = 'inside'
    |
    | 5d. INSERT INTO ACCESSLOGS (log attendance scan)
    |
    | 6. Return attendance record with status
    |
    v
User Interface
    |
    | 7. Display "Attendance Recorded Successfully"
    |    Display status (Present/Late)
    |    Display student name and subject
    |
    v
Student

================================================================================
FIGURE 3. SEQUENCE DIAGRAM FOR ADMINISTRATOR LOGIN
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

Administrator ---- 1. Enter email/username and password -----> User Interface ---- 2. Send email and password -----> DATABASE

Administrator <------- 6. Returns JWT token and user info ------- User Interface <------- 5. Returns user details and password hash ------- DATABASE

User Interface ---- 3. Query USERS table by email -----> DATABASE

User Interface ---- 4. Query USERS table by STUDENTID or FACULTYID (if email not found) -----> DATABASE

User Interface ---- 4a. Verify password using bcrypt.compare() -----> (Internal processing)

User Interface ---- 4b. Generate JWT token -----> (Internal processing)

DETAILED FLOW:

Administrator
    |
    | 1. Enter email/username and password in login form
    |
    v
User Interface (Frontend)
    |
    | 2. POST /api/auth/login
    |    Send { email, password }
    |
    v
User Interface (Backend API)
    |
    | 3. Query USERS table
    |    SELECT * FROM USERS WHERE EMAIL = ?
    |
    | IF NOT FOUND:
    |   4. Query USERS table
    |      SELECT * FROM USERS 
    |      WHERE STUDENTID = ? OR FACULTYID = ?
    |
    | 5. Verify user exists and STATUS = 'Active'
    |
    | 6. Verify password using bcrypt.compare()
    |    Compare provided password with PASSWORD_HASH
    |
    | 7. Generate JWT token
    |    jwt.sign({ userId, email, role }, JWT_SECRET, { expiresIn: '7d' })
    |
    | 8. Return token and user info
    |
    v
User Interface (Frontend)
    |
    | 9. Store JWT token in localStorage
    |    Redirect to admin dashboard
    |
    v
Administrator

IF INVALID CREDENTIALS:
Administrator <------- Display Error: "Invalid credentials" ------- User Interface

IF ACCOUNT INACTIVE:
Administrator <------- Display Error: "Account is inactive" ------- User Interface

================================================================================
FIGURE 4. SEQUENCE DIAGRAM FOR FACULTY EXIT AND SYSTEM DEACTIVATION
================================================================================

PARTICIPANTS: Faculty Member ----- User Interface ----- DATABASE ----- ESP32 Door Controller

Faculty Member ---- 1. Places finger for scanning at external scanner ------> User Interface ---- 2. Send identifier and auth_method -----> DATABASE

Faculty Member <------- 6. Displays "Session Ended" / "Access Granted" ------- User Interface <------- 5. Returns session end confirmation ------- DATABASE

Faculty Member <------- 7. Send "Lock" Signal to door controller ------- User Interface

User Interface ---- 3. Query AUTHENTICATIONMETHODS and USERS tables -----> DATABASE

User Interface ---- 4. Verify faculty identity and active session -----> DATABASE

User Interface ---- 4a. Update SESSIONS table (end session) -----> DATABASE

User Interface ---- 4b. Update ROOMS table (lock door) -----> DATABASE

User Interface ---- 4c. Insert ATTENDANCERECORDS (instructor time_out) -----> DATABASE

User Interface ---- 4d. Insert ACCESSLOGS entry -----> DATABASE

DETAILED FLOW:

Faculty Member
    |
    | 1. Places finger on external fingerprint scanner
    |    (to end session and exit)
    |
    v
User Interface (Backend API)
    |
    | 2. Send identifier, auth_method, room_id, location='outside'
    |
    v
DATABASE
    |
    | 3. Query AUTHENTICATIONMETHODS JOIN USERS
    |    WHERE IDENTIFIER = ? AND METHODTYPE = ?
    |    AND ISACTIVE = TRUE AND STATUS = 'Active'
    |    Verify USERTYPE = 'instructor'
    |
    | 4. Check for active session in SESSIONS table
    |    WHERE INSTRUCTORID = ? AND ROOMID = ?
    |    AND SESSIONDATE = CURDATE() AND STATUS = 'active'
    |
    | IF ACTIVE SESSION FOUND:
    |   4a. UPDATE SESSIONS SET STATUS = 'ended'
    |       SET ENDTIME = CURRENT_TIMESTAMP
    |       SET DOORLOCKEDAT = CURRENT_TIMESTAMP
    |
    |   4b. UPDATE ROOMS SET DOORSTATUS = 'locked'
    |
    |   4c. INSERT INTO ATTENDANCERECORDS
    |       SET SCANTYPE = 'time_out'
    |       SET LOCATION = 'outside'
    |       SET STATUS = 'Present'
    |
    |   4d. INSERT INTO ACCESSLOGS
    |       SET ACCESSTYPE = 'session_end'
    |       SET RESULT = 'success'
    |
    | 5. Return session end confirmation
    |
    v
User Interface
    |
    | 6. Display "Session Ended Successfully"
    |    Display door status (locked)
    |
    | 7. Send lock signal to ESP32 door controller
    |
    v
Faculty Member

IF NO ACTIVE SESSION:
Faculty Member <------- Display Error: "No active session found" ------- User Interface

================================================================================
NOTES
================================================================================

DATABASE TABLES REFERENCED:

AUTHENTICATIONMETHODS:
- Stores fingerprint templates and RFID identifiers
- Links to USERS table via USERID
- Contains METHODTYPE (fingerprint/rfid), IDENTIFIER, ISACTIVE

USERS:
- Stores user information (instructor, student, admin)
- Contains USERID, USERTYPE, STATUS, PASSWORD_HASH
- Links to AUTHENTICATIONMETHODS

SESSIONS:
- Stores active and ended class sessions
- Contains SESSIONID, SCHEDULEID, INSTRUCTORID, ROOMID
- Contains STARTTIME, ENDTIME, STATUS, DOORUNLOCKEDAT, DOORLOCKEDAT

ATTENDANCERECORDS:
- Stores student and instructor attendance records
- Contains USERID, SCHEDULEID, SCANTYPE, SCANDATETIME
- Contains STATUS (Present/Late/Early Arrival), LOCATION, AUTHMETHOD

ACCESSLOGS:
- Stores all access attempts and system actions
- Contains USERID, ROOMID, ACCESSTYPE, AUTHMETHOD, LOCATION
- Contains RESULT (success/denied), REASON

ROOMS:
- Stores room information and door status
- Contains ROOMID, ROOMNUMBER, ROOMNAME, DOORSTATUS

CLASSSCHEDULES:
- Stores class schedule information
- Contains SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK
- Contains STARTTIME, ENDTIME, ACADEMICYEAR, SEMESTER

SUBJECTENROLLMENT:
- Stores student enrollments in subjects
- Contains ENROLLMENTID, USERID, SUBJECTID, STATUS

AUTHENTICATION METHODS:

Fingerprint:
- Stored as IDENTIFIER in AUTHENTICATIONMETHODS table
- Format: FP_<STUDENTID> or fingerprint template data
- Used by students and instructors

RFID:
- Stored as IDENTIFIER and RFIDTAGNUMBER in AUTHENTICATIONMETHODS table
- Format: RFID_<EMPLOYEEID>
- Used by instructors for quick access

SESSION STATES:

Session Start:
- Created when instructor scans at external scanner
- Status set to 'active'
- Door unlocked (DOORSTATUS = 'unlocked')
- Early arrival students upgraded to Present

Session End:
- Updated when instructor scans at external scanner again
- Status set to 'ended'
- Door locked (DOORSTATUS = 'locked')
- ENDTIME and DOORLOCKEDAT recorded

ATTENDANCE STATUSES:

Present:
- Student scanned on time (within 15 minutes of session start)
- Or student confirmed early arrival scan

Late:
- Student scanned after 15 minutes from session start time
- Calculated based on session STARTTIME

Early Arrival:
- Student scanned outside before session start (up to 15 minutes early)
- Upgraded to Present when session starts or when student scans inside

ERROR HANDLING:

Invalid Credentials:
- User Interface returns 401 status
- Displays "Invalid credentials" or "Invalid fingerprint"
- Access logged in ACCESSLOGS with RESULT = 'denied'

Invalid Role:
- User Interface returns 403 status
- Displays "Only instructors can access from outside" or "Only students can scan for attendance"
- Access logged in ACCESSLOGS with RESULT = 'denied'

No Active Session:
- User Interface returns 404 status
- Displays "No active session found"
- Access logged in ACCESSLOGS with RESULT = 'denied'

Already Recorded:
- User Interface returns 409 status
- Displays "Attendance already recorded for today"
- Access logged in ACCESSLOGS with RESULT = 'denied'

================================================================================
FIGURE 5. SEQUENCE DIAGRAM FOR FUTRONIC SYSTEM USER ENROLLMENT
================================================================================

PARTICIPANTS: Administrator ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- User

Administrator ---- 1. Select user from table -----> FutronicAttendanceSystem

Administrator ---- 2. Click "Start Enrollment" button -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 3. Query USERS table by user selection -----> DATABASE

FutronicAttendanceSystem <------- 4. Returns user details ------- DATABASE

FutronicAttendanceSystem ---- 5. Initialize FutronicEnrollment operation -----> Futronic SDK

User ---- 6. Places finger on scanner (scan 1) -----> Futronic SDK

Futronic SDK ---- 7. Captures fingerprint template (scan 1) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 8. Display "Scan finger again" -----> User

User ---- 9. Places finger on scanner (scan 2) -----> Futronic SDK

Futronic SDK ---- 10. Captures fingerprint template (scan 2) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 11. Compare templates and validate quality -----> (Internal processing)

FutronicAttendanceSystem ---- 12. Display "Scan finger third time" -----> User

User ---- 13. Places finger on scanner (scan 3) -----> Futronic SDK

Futronic SDK ---- 14. Captures fingerprint template (scan 3) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 15. Create composite template from all scans -----> (Internal processing)

FutronicAttendanceSystem ---- 16. Validate template quality -----> (Internal processing)

FutronicAttendanceSystem ---- 17. Save fingerprint template to AUTHENTICATIONMETHODS -----> DATABASE

FutronicAttendanceSystem <------- 18. Returns success confirmation ------- DATABASE

FutronicAttendanceSystem ---- 19. Refresh user list from DATABASE -----> DATABASE

FutronicAttendanceSystem <------- 20. Returns updated user list ------- DATABASE

FutronicAttendanceSystem ---- 21. Display "Enrollment complete" -----> Administrator

DETAILED FLOW:

Administrator
    |
    | 1. Select user from user table
    |
    v
FutronicAttendanceSystem (MainForm)
    |
    | 2. Click "Start Enrollment" button
    |
    | 3. Query DATABASE for user details
    |    SELECT * FROM USERS WHERE USERID = ?
    |
    v
DATABASE
    |
    | 4. Return user details
    |
    v
FutronicAttendanceSystem
    |
    | 5. Initialize FutronicEnrollment operation
    |    Set properties: FakeDetection=false, FARN=200
    |    Start enrollment process
    |
    v
Futronic SDK
    |
    | 6-7. First fingerprint scan
    |    Capture template, validate quality
    |
    | 8. Display "Scan finger again" message
    |
    | 9-10. Second fingerprint scan
    |    Capture template, compare with first
    |
    | 11. Validate template match
    |
    | 12. Display "Scan finger third time" message
    |
    | 13-14. Third fingerprint scan
    |    Capture template
    |
    | 15. Create composite template from all scans
    |
    | 16. Validate template quality
    |    Check template size and validity
    |
    v
FutronicAttendanceSystem
    |
    | 17. Save to DATABASE
    |    INSERT INTO AUTHENTICATIONMETHODS
    |    SET METHODTYPE = 'Fingerprint'
    |    SET IDENTIFIER = 'FP_{STUDENTID}'
    |    SET FINGERPRINTTEMPLATE = template_data
    |    SET ISACTIVE = TRUE
    |
    v
DATABASE
    |
    | 18. Return success confirmation
    |
    v
FutronicAttendanceSystem
    |
    | 19. Refresh user list
    |    SELECT * FROM USERS
    |
    | 20. Update user table display
    |
    | 21. Display "Enrollment complete" message
    |
    v
Administrator

================================================================================
FIGURE 6. SEQUENCE DIAGRAM FOR FUTRONIC SYSTEM DUAL SENSOR MODE
================================================================================

PARTICIPANTS: User ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- ESP32 Door Controller

FutronicAttendanceSystem ---- 1. Initialize dual sensor mode -----> Inside Sensor / Outside Sensor

FutronicAttendanceSystem ---- 2. Create FutronicIdentification for inside sensor -----> Futronic SDK (Inside)

FutronicAttendanceSystem ---- 3. Create FutronicIdentification for outside sensor -----> Futronic SDK (Outside)

User ---- 4. Places finger on sensor (inside or outside) -----> Futronic SDK

Futronic SDK ---- 5. Captures fingerprint and identifies user -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 6. Determine scan location (inside/outside) -----> (Internal processing)

FutronicAttendanceSystem ---- 7. Query user info from DATABASE -----> DATABASE

FutronicAttendanceSystem <------- 8. Returns user details and role ------- DATABASE

FutronicAttendanceSystem ---- 9. Process based on location and user type -----> (Internal processing)

IF OUTSIDE SCAN:
FutronicAttendanceSystem ---- 10a. Process instructor scan (session start/end) -----> DATABASE

IF INSIDE SCAN:
FutronicAttendanceSystem ---- 10b. Process student scan (attendance recording) -----> DATABASE

FutronicAttendanceSystem ---- 11. Send door control signal to ESP32 -----> ESP32 Door Controller

ESP32 Door Controller <------- 12. Returns door status ------- FutronicAttendanceSystem

FutronicAttendanceSystem ---- 13. Display result to user -----> User

DETAILED FLOW:

FutronicAttendanceSystem (MainForm)
    |
    | 1. Initialize dual sensor mode
    |    Set isDualSensorMode = true
    |    Load device configuration
    |
    | 2. Create inside sensor operation
    |    m_InsideSensorOperation = new FutronicIdentification()
    |    Set currentScanLocation = "inside"
    |
    | 3. Create outside sensor operation
    |    m_OutsideSensorOperation = new FutronicIdentification()
    |    Set currentScanLocation = "outside"
    |
    v
Futronic SDK (Inside/Outside)
    |
    | 4. User places finger on sensor
    |
    | 5. Capture fingerprint and identify user
    |    Match against enrolled templates
    |
    v
FutronicAttendanceSystem
    |
    | 6. Determine scan location
    |    Based on which sensor triggered event
    |
    | 7. Query DATABASE for user info
    |    SELECT * FROM USERS WHERE USERID = ?
    |
    v
DATABASE
    |
    | 8. Return user details
    |
    v
FutronicAttendanceSystem
    |
    | 9. Process based on location and user type
    |
    | IF OUTSIDE SCAN:
    |   - If instructor: Process session start/end
    |   - If student: Process early arrival or door access
    |
    | IF INSIDE SCAN:
    |   - If student: Process attendance recording
    |   - If instructor: Process session end
    |
    | 10a/10b. Update DATABASE
    |    Create/update SESSIONS
    |    Insert ATTENDANCERECORDS
    |    Insert ACCESSLOGS
    |
    | 11. Send door control signal to ESP32
    |    POST /api/lock-control or /api/fingerprint-scan
    |    { action: "open" or "lock", user: user_name }
    |
    v
ESP32 Door Controller
    |
    | 12. Process door control signal
    |    Unlock or lock door mechanism
    |
    v
FutronicAttendanceSystem
    |
    | 13. Display result to user
    |    Show "Access Granted" or "Attendance Recorded"
    |
    v
User

================================================================================
FIGURE 7. SEQUENCE DIAGRAM FOR FUTRONIC SYSTEM CROSS-TYPE VERIFICATION
================================================================================

PARTICIPANTS: User ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- ESP32 Door Controller

User ---- 1. Scans RFID card (first authentication) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 2. Identify user by RFID -----> DATABASE

FutronicAttendanceSystem <------- 3. Returns user details ------- DATABASE

FutronicAttendanceSystem ---- 4. Set awaitingCrossTypeVerification = true -----> (Internal state)

FutronicAttendanceSystem ---- 5. Store firstScanType = "RFID" -----> (Internal state)

FutronicAttendanceSystem ---- 6. Store pendingCrossVerificationUser = userName -----> (Internal state)

FutronicAttendanceSystem ---- 7. Display "Please scan fingerprint to verify" -----> User

User ---- 8. Places finger on scanner (second authentication) -----> Futronic SDK

Futronic SDK ---- 9. Captures fingerprint and identifies user -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 10. Compare userName from RFID and fingerprint -----> (Internal processing)

DECISION: User names match?

NO ---- FutronicAttendanceSystem ---- 11a. Display "Verification failed" -----> User
NO ---- FutronicAttendanceSystem ---- 11b. Reset verification state -----> (Internal state)
NO ---- FutronicAttendanceSystem ---- 11c. Log denied access to DATABASE -----> DATABASE

YES ---- FutronicAttendanceSystem ---- 12a. Set awaitingCrossTypeVerification = false -----> (Internal state)
YES ---- FutronicAttendanceSystem ---- 12b. Process verified action (session start/attendance) -----> DATABASE
YES ---- FutronicAttendanceSystem ---- 12c. Display "Verified: Access Granted" -----> User
YES ---- FutronicAttendanceSystem ---- 12d. Send door control signal -----> ESP32 Door Controller

DETAILED FLOW:

User
    |
    | 1. Scan RFID card at scanner
    |
    v
FutronicAttendanceSystem
    |
    | 2. Identify user by RFID
    |    Query AUTHENTICATIONMETHODS WHERE IDENTIFIER = RFID_data
    |
    v
DATABASE
    |
    | 3. Return user details
    |
    v
FutronicAttendanceSystem
    |
    | 4-6. Set cross-type verification state
    |    awaitingCrossTypeVerification = true
    |    firstScanType = "RFID"
    |    pendingCrossVerificationUser = userName
    |    pendingCrossVerificationGuid = userGuid
    |    crossVerificationStartTime = DateTime.Now
    |
    | 7. Display "Please scan fingerprint to verify"
    |    Show waiting status on screen
    |
    v
User
    |
    | 8. Place finger on fingerprint scanner
    |
    v
Futronic SDK
    |
    | 9. Capture fingerprint and identify user
    |
    v
FutronicAttendanceSystem
    |
    | 10. Compare user names
    |    IF userName == pendingCrossVerificationUser
    |    AND userGuid == pendingCrossVerificationGuid
    |
    | DECISION: Match?
    |
    | NO:
    |   11a. Display "Verification failed: RFID and Fingerprint mismatch"
    |   11b. Reset verification state
    |   11c. Log denied access to ACCESSLOGS
    |   11d. END
    |
    | YES:
    |   12a. Reset verification state
    |   12b. Process verified action
    |       - If instructor: Start/end session
    |       - If student: Record attendance
    |   12c. Display "Verified: Access Granted"
    |   12d. Send door control signal to ESP32
    |
    v
User

TIMEOUT HANDLING:
- If verification not completed within 20 seconds
- Reset verification state
- Display "Verification timeout"
- Log timeout to ACCESSLOGS

================================================================================
FIGURE 8. SEQUENCE DIAGRAM FOR FUTRONIC SYSTEM EARLY ARRIVAL HANDLING
================================================================================

PARTICIPANTS: Student ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- ESP32 Door Controller

Student ---- 1. Scans fingerprint at outside sensor (before session start) -----> Futronic SDK

Futronic SDK ---- 2. Identifies student -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 3. Check if session is active -----> DATABASE

FutronicAttendanceSystem <------- 4. Returns no active session ------- DATABASE

FutronicAttendanceSystem ---- 5. Check if early arrival is enabled -----> (Internal configuration)

FutronicAttendanceSystem ---- 6. Check if schedule exists for upcoming class -----> DATABASE

FutronicAttendanceSystem <------- 7. Returns schedule details (if found) ------- DATABASE

FutronicAttendanceSystem ---- 8. Set awaitingEarlyArrivalVerification = true -----> (Internal state)

FutronicAttendanceSystem ---- 9. Store earlyPendingUser = userName -----> (Internal state)

FutronicAttendanceSystem ---- 10. Display "Please scan RFID to complete early arrival" -----> Student

Student ---- 11. Scans RFID card -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 12. Compare RFID user with fingerprint user -----> (Internal processing)

DECISION: Users match?

NO ---- FutronicAttendanceSystem ---- 13a. Display "Early arrival verification failed" -----> Student
NO ---- FutronicAttendanceSystem ---- 13b. Reset early arrival state -----> (Internal state)

YES ---- FutronicAttendanceSystem ---- 14a. Record early arrival in DATABASE -----> DATABASE
YES ---- FutronicAttendanceSystem ---- 14b. Insert ATTENDANCERECORDS with STATUS = 'Early Arrival' -----> DATABASE
YES ---- FutronicAttendanceSystem ---- 14c. Display "Early arrival recorded" -----> Student
YES ---- FutronicAttendanceSystem ---- 14d. Send door unlock signal -----> ESP32 Door Controller

Student ---- 15. Enters room and scans inside when session starts -----> Futronic SDK

Futronic SDK ---- 16. Identifies student -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 17. Check for early arrival record -----> DATABASE

FutronicAttendanceSystem <------- 18. Returns early arrival record ------- DATABASE

FutronicAttendanceSystem ---- 19. Upgrade early arrival to Present -----> DATABASE

FutronicAttendanceSystem ---- 20. Update ATTENDANCERECORDS SET STATUS = 'Present' -----> DATABASE

FutronicAttendanceSystem ---- 21. Display "Early arrival confirmed" -----> Student

DETAILED FLOW:

Student
    |
    | 1. Scan fingerprint at outside sensor
    |    (Before session start time, up to 15 minutes early)
    |
    v
Futronic SDK
    |
    | 2. Identify student
    |
    v
FutronicAttendanceSystem
    |
    | 3. Check if session is active
    |    Query SESSIONS WHERE STATUS = 'active'
    |
    v
DATABASE
    |
    | 4. Return no active session
    |
    v
FutronicAttendanceSystem
    |
    | 5. Check early arrival configuration
    |    IsFingerprintOnlyMode?
    |
    | 6. Check for upcoming schedule
    |    Query CLASSSCHEDULES
    |    WHERE STARTTIME > CURRENT_TIME
    |    AND STARTTIME <= CURRENT_TIME + 15 minutes
    |
    v
DATABASE
    |
    | 7. Return schedule details (if found)
    |
    v
FutronicAttendanceSystem
    |
    | 8-9. Set early arrival verification state
    |    awaitingEarlyArrivalVerification = true
    |    earlyPendingUser = userName
    |    earlyPendingGuid = userGuid
    |    earlyVerificationStartTime = DateTime.Now
    |
    | 10. Display "Please scan RFID to complete early arrival"
    |
    v
Student
    |
    | 11. Scan RFID card
    |
    v
FutronicAttendanceSystem
    |
    | 12. Compare users
    |    IF RFID_user == earlyPendingUser
    |
    | DECISION: Match?
    |
    | NO:
    |   13a. Display "Verification failed"
    |   13b. Reset early arrival state
    |   13c. END
    |
    | YES:
    |   14a. Record early arrival
    |   14b. INSERT INTO ATTENDANCERECORDS
    |       SET STATUS = 'Early Arrival'
    |       SET SCANTYPE = 'early_arrival'
    |       SET LOCATION = 'outside'
    |   14c. Display "Early arrival recorded"
    |   14d. Send door unlock signal to ESP32
    |
    v
Student
    |
    | 15. Enter room and scan inside when session starts
    |
    v
Futronic SDK
    |
    | 16. Identify student
    |
    v
FutronicAttendanceSystem
    |
    | 17. Check for early arrival record
    |    Query ATTENDANCERECORDS
    |    WHERE USERID = ? AND STATUS = 'Early Arrival'
    |
    v
DATABASE
    |
    | 18. Return early arrival record
    |
    v
FutronicAttendanceSystem
    |
    | 19-20. Upgrade early arrival to Present
    |    UPDATE ATTENDANCERECORDS
    |    SET STATUS = 'Present'
    |    SET SCANTYPE = 'time_in_confirmation'
    |    SET LOCATION = 'inside'
    |
    | 21. Display "Early arrival confirmed"
    |
    v
Student

================================================================================
FIGURE 9. SEQUENCE DIAGRAM FOR WEB DASHBOARD VIEWING
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to Dashboard page -----> User Interface

User Interface ---- 2. Check authentication token -----> (Internal processing)

User Interface ---- 3. Request dashboard statistics -----> DATABASE

User Interface <------- 4. Returns dashboard data ------- DATABASE

User Interface ---- 5. Display statistics cards -----> Administrator

User Interface ---- 6. Display active sessions list -----> Administrator

User Interface ---- 7. Display recent activity feed -----> Administrator

User Interface ---- 8. Display device status -----> Administrator

DETAILED FLOW:

Administrator
    |
    | 1. Navigate to /dashboard
    |
    v
User Interface
    |
    | 2. Check authentication
    |    Get token from localStorage
    |
    | 3. Request dashboard statistics
    |    Query DATABASE for:
    |    - Total users count
    |    - Active sessions count
    |    - Today's attendance count
    |    - Total rooms count
    |    - Recent activity logs
    |    - Online devices status
    |
    v
DATABASE
    |
    | 4. Return dashboard data
    |    Return consolidated statistics
    |
    v
User Interface
    |
    | 5-8. Display dashboard components
    |    - Statistics cards (users, sessions, attendance, rooms)
    |    - Active sessions list
    |    - Recent activity feed
    |    - Device status indicators
    |
    v
Administrator

REAL-TIME UPDATES:
- User Interface polls DATABASE every 5 seconds
- User Interface refreshes dashboard data periodically
- WebSocket connections for real-time updates (if implemented)

================================================================================
FIGURE 10. SEQUENCE DIAGRAM FOR WEB USER MANAGEMENT
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to Users page -----> User Interface

User Interface ---- 2. Request users list -----> DATABASE

User Interface <------- 3. Returns users list ------- DATABASE

User Interface ---- 4. Display users table -----> Administrator

Administrator ---- 5. Click "Add User" button -----> User Interface

User Interface ---- 6. Display user form modal -----> Administrator

Administrator ---- 7. Fill user form and submit -----> User Interface

User Interface ---- 8. Validate form data -----> (Internal validation)

User Interface ---- 9. Insert new user into USERS table -----> DATABASE

User Interface <------- 10. Returns new user ID ------- DATABASE

User Interface ---- 11. Refresh users list -----> DATABASE

User Interface ---- 12. Display success message -----> Administrator

Administrator ---- 13. Click "Edit User" button -----> User Interface

User Interface ---- 14. Display edit form with user data -----> Administrator

Administrator ---- 15. Modify user data and submit -----> User Interface

User Interface ---- 16. Update user in USERS table -----> DATABASE

User Interface <------- 17. Returns update confirmation ------- DATABASE

User Interface ---- 18. Refresh users list -----> DATABASE

User Interface ---- 19. Display success message -----> Administrator

Administrator ---- 20. Click "Delete User" button -----> User Interface

User Interface ---- 21. Display delete confirmation modal -----> Administrator

Administrator ---- 22. Confirm deletion -----> User Interface

User Interface ---- 23. Delete user from USERS table -----> DATABASE

User Interface <------- 24. Returns deletion confirmation ------- DATABASE

User Interface ---- 25. Refresh users list -----> DATABASE

User Interface ---- 26. Display success message -----> Administrator

BULK OPERATIONS:

Administrator ---- 27. Select multiple users -----> User Interface

Administrator ---- 28. Click "Bulk Delete" button -----> User Interface

User Interface ---- 29. Display bulk delete confirmation -----> Administrator

Administrator ---- 30. Confirm bulk deletion -----> User Interface

User Interface ---- 31. Delete multiple users from USERS table -----> DATABASE

User Interface <------- 32. Returns bulk deletion confirmation ------- DATABASE

User Interface ---- 33. Refresh users list -----> DATABASE

User Interface ---- 34. Display success message -----> Administrator

USER IMPORT:

Administrator ---- 35. Click "Import Users" button -----> User Interface

User Interface ---- 36. Display file upload modal -----> Administrator

Administrator ---- 37. Select CSV/PDF file and upload -----> User Interface

User Interface ---- 38. Parse CSV/PDF file -----> (Internal processing)

User Interface ---- 39. Validate imported user data -----> (Internal validation)

User Interface ---- 40. Bulk insert users into USERS table -----> DATABASE

User Interface <------- 41. Returns import results ------- DATABASE

User Interface ---- 42. Display import results (success/errors) -----> Administrator

DETAILED FLOW:

Administrator
    |
    | 1. Navigate to /users
    |
    v
User Interface
    |
    | 2. Request users list
    |    Query USERS table with filters
    |
    v
DATABASE
    |
    | 3. Return users list
    |
    v
User Interface
    |
    | 4. Display users table
    |    Show: Name, Email, Type, Status, Actions
    |
    v
Administrator
    |
    | 5. Click "Add User"
    |
    v
User Interface
    |
    | 6. Display user form modal
    |    Fields: First Name, Last Name, Email, User Type, etc.
    |
    | 7. Administrator fills form and submits
    |
    | 8. Validate form data
    |    Check required fields, email format, etc.
    |
    | 9. Insert new user into USERS table
    |    INSERT INTO USERS (USERID, FIRSTNAME, LASTNAME, EMAIL, USERTYPE, ...)
    |
    v
DATABASE
    |
    | 10. Return new user ID
    |
    v
User Interface
    |
    | 11. Refresh users list
    |
    | 12. Display success message
    |
    v
Administrator

EDIT USER FLOW (13-19):
- Similar to create user flow
- Updates existing user record in DATABASE
- Refreshes users list and displays success message

DELETE USER FLOW (20-26):
- Deletes user from USERS table
- Soft delete or hard delete based on configuration
- May cascade delete related records

BULK DELETE FLOW (27-34):
- Deletes multiple users in single transaction
- Returns bulk deletion confirmation

IMPORT USERS FLOW (35-42):
- Parses CSV/PDF file
- Validates imported user data
- Bulk inserts users into USERS table
- Returns import results with success/error counts

================================================================================
FIGURE 11. SEQUENCE DIAGRAM FOR WEB REPORTS GENERATION
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to Reports page -----> User Interface

User Interface ---- 2. Request initial data (subjects, academic years, semesters) -----> DATABASE

User Interface <------- 3. Returns initial data ------- DATABASE

User Interface ---- 4. Display filters and empty reports table -----> Administrator

Administrator ---- 5. Select filters (date range, subject, academic year, semester) -----> User Interface

User Interface ---- 6. Request attendance reports with filters -----> DATABASE

User Interface <------- 7. Returns attendance records ------- DATABASE

User Interface ---- 8. Calculate statistics -----> (Internal processing)

User Interface ---- 9. Group reports by subject/date/session -----> (Internal processing)

User Interface ---- 10. Display grouped reports -----> Administrator

Administrator ---- 11. Click "Export to CSV" button -----> User Interface

User Interface ---- 12. Generate CSV file from reports data -----> (Internal processing)

User Interface ---- 13. Download CSV file -----> Administrator

Administrator ---- 14. Click "View Details" on report card -----> User Interface

User Interface ---- 15. Request session roster -----> DATABASE

User Interface <------- 16. Returns session roster ------- DATABASE

User Interface ---- 17. Display session roster modal -----> Administrator

DETAILED FLOW:

Administrator
    |
    | 1. Navigate to /reports
    |
    v
User Interface
    |
    | 2. Request initial data
    |    Query SUBJECTS table
    |    Query SETTINGS table for academic years and semesters
    |
    v
DATABASE
    |
    | 3. Return initial data
    |
    v
User Interface
    |
    | 4. Display filters and empty reports table
    |    Filters: Date range, Subject, Academic Year, Semester
    |
    v
Administrator
    |
    | 5. Select filters and apply
    |
    v
User Interface
    |
    | 6. Request attendance reports with filters
    |    Query ATTENDANCERECORDS with joins to USERS, CLASSSCHEDULES, SUBJECTS
    |
    v
DATABASE
    |
    | 7. Return attendance records
    |
    v
User Interface
    |
    | 8. Calculate statistics
    |    - Total attendance count
    |    - Present count
    |    - Late count
    |    - Absent count
    |    - Attendance percentage
    |
    | 9. Group reports
    |    Group by: Subject, Date, or Session
    |
    | 10. Display grouped reports
    |    Show report cards with statistics
    |
    v
Administrator
    |
    | 11. Click "Export to CSV"
    |
    v
User Interface
    |
    | 12. Generate CSV file
    |    Convert reports data to CSV format
    |
    | 13. Download CSV file
    |    Trigger browser download
    |
    v
Administrator

VIEW SESSION ROSTER FLOW (14-17):
- Administrator clicks "View Details" on report card
- User Interface requests session roster from DATABASE
- DATABASE returns enrolled students and attendance records
- User Interface merges data and marks absent students
- User Interface displays session roster modal with full attendance list

================================================================================
FIGURE 12. SEQUENCE DIAGRAM FOR WEB ARCHIVE OPERATIONS
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to Archive page -----> User Interface

User Interface ---- 2. Request archive dashboard statistics -----> DATABASE

User Interface <------- 3. Returns archive statistics ------- DATABASE

User Interface ---- 4. Display archive dashboard -----> Administrator

Administrator ---- 5. Click "Archive Subjects" button -----> User Interface

User Interface ---- 6. Display archive modal -----> Administrator

Administrator ---- 7. Select academic year and semester -----> User Interface

Administrator ---- 8. Enter archive reason -----> User Interface

Administrator ---- 9. Confirm archive operation -----> User Interface

User Interface ---- 10. Update SUBJECTS table SET ARCHIVED_AT = CURRENT_TIMESTAMP -----> DATABASE

User Interface ---- 11. Update related CLASSSCHEDULES SET ARCHIVED_AT = CURRENT_TIMESTAMP -----> DATABASE

User Interface ---- 12. Update related SUBJECTENROLLMENT SET ARCHIVED_AT = CURRENT_TIMESTAMP -----> DATABASE

User Interface ---- 13. Create backup of archived data -----> DATABASE

User Interface <------- 14. Returns archive confirmation ------- DATABASE

User Interface ---- 15. Refresh archive dashboard -----> DATABASE

User Interface ---- 16. Display success message -----> Administrator

Administrator ---- 17. Click "View Archived Subjects" tab -----> User Interface

User Interface ---- 18. Request archived subjects -----> DATABASE

User Interface <------- 19. Returns archived subjects ------- DATABASE

User Interface ---- 20. Display archived subjects table -----> Administrator

Administrator ---- 21. Click "Unarchive" button -----> User Interface

User Interface ---- 22. Display unarchive confirmation modal -----> Administrator

Administrator ---- 23. Confirm unarchive operation -----> User Interface

User Interface ---- 24. Update SUBJECTS SET ARCHIVED_AT = NULL -----> DATABASE

User Interface ---- 25. Update related CLASSSCHEDULES SET ARCHIVED_AT = NULL -----> DATABASE

User Interface ---- 26. Update related SUBJECTENROLLMENT SET ARCHIVED_AT = NULL -----> DATABASE

User Interface <------- 27. Returns unarchive confirmation ------- DATABASE

User Interface ---- 28. Refresh archived subjects list -----> DATABASE

User Interface ---- 29. Display success message -----> Administrator

DETAILED FLOW:

Administrator
    |
    | 1. Navigate to /archive
    |
    v
User Interface
    |
    | 2. Request archive dashboard statistics
    |    Query archived data counts from DATABASE
    |
    v
DATABASE
    |
    | 3. Return archive statistics
    |
    v
User Interface
    |
    | 4. Display archive dashboard
    |    Show archive statistics and archive options
    |
    v
Administrator
    |
    | 5. Click "Archive Subjects"
    |
    v
User Interface
    |
    | 6. Display archive modal
    |    Fields: Academic Year, Semester, Archive Reason
    |
    | 7-8. Administrator selects options and enters reason
    |
    | 9. Administrator confirms archive operation
    |
    | 10. Archive subjects
    |    UPDATE SUBJECTS SET ARCHIVED_AT = CURRENT_TIMESTAMP
    |
    | 11. Archive related schedules
    |    UPDATE CLASSSCHEDULES SET ARCHIVED_AT = CURRENT_TIMESTAMP
    |
    | 12. Archive related enrollments
    |    UPDATE SUBJECTENROLLMENT SET ARCHIVED_AT = CURRENT_TIMESTAMP
    |
    | 13. Create backup of archived data
    |
    v
DATABASE
    |
    | 14. Return archive confirmation
    |
    v
User Interface
    |
    | 15. Refresh archive dashboard
    |
    | 16. Display success message
    |
    v
Administrator

VIEW ARCHIVED DATA FLOW (17-20):
- Administrator clicks "View Archived Subjects" tab
- User Interface requests archived subjects from DATABASE
- DATABASE returns archived subjects
- User Interface displays archived subjects table

UNARCHIVE FLOW (21-29):
- Administrator clicks "Unarchive" button
- User Interface displays unarchive confirmation modal
- Administrator confirms unarchive operation
- User Interface updates ARCHIVED_AT = NULL for subject and related records in DATABASE
- User Interface refreshes archived subjects list
- User Interface displays success message

ARCHIVE CATEGORIES:
- Subjects: Archive by academic year and semester
- Rooms: Archive specific rooms
- Schedules: Archive specific class schedules
- Users: Archive specific users
- Attendance: Archive attendance records by date range

================================================================================
FIGURE 13. SEQUENCE DIAGRAM FOR FUTRONIC SYSTEM SESSION STATE MANAGEMENT
================================================================================

PARTICIPANTS: Instructor ----- FutronicAttendanceSystem ----- DATABASE

FutronicAttendanceSystem ---- 1. Initialize session state to Inactive -----> (Internal state)

Instructor ---- 2. Scans at outside sensor (session start) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 3. Validate instructor and schedule -----> DATABASE

FutronicAttendanceSystem <------- 4. Returns validation result ------- DATABASE

FutronicAttendanceSystem ---- 5. Create SESSIONS record -----> DATABASE

FutronicAttendanceSystem ---- 6. Update session state to ActiveForStudents -----> (Internal state)

FutronicAttendanceSystem ---- 7. Display "Session Active - Students Can Sign In" -----> Instructor

Student ---- 8. Scans at inside sensor (sign in) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 9. Record attendance and add to signedInStudentGuids -----> DATABASE

FutronicAttendanceSystem ---- 10. Display "Attendance Recorded" -----> Student

Instructor ---- 11. Scans at inside sensor (enable sign-out) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 12. Update session state to ActiveForSignOut -----> (Internal state)

FutronicAttendanceSystem ---- 13. Display "Session Active - Students Can Sign Out" -----> Instructor

Student ---- 14. Scans at inside sensor (sign out) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 15. Record sign-out and add to signedOutStudentGuids -----> DATABASE

FutronicAttendanceSystem ---- 16. Display "Sign-out Recorded" -----> Student

Instructor ---- 17. Scans at outside sensor (session end) -----> FutronicAttendanceSystem

FutronicAttendanceSystem ---- 18. Update SESSIONS SET STATUS = 'ended' -----> DATABASE

FutronicAttendanceSystem ---- 19. Update session state to Inactive -----> (Internal state)

FutronicAttendanceSystem ---- 20. Clear signedInStudentGuids and signedOutStudentGuids -----> (Internal state)

FutronicAttendanceSystem ---- 21. Display "Session Ended" -----> Instructor

SESSION STATE TRANSITIONS:

Inactive -> WaitingForInstructor:
- System boot with always-on attendance enabled
- Waiting for instructor to start session

WaitingForInstructor -> ActiveForStudents:
- Instructor scans at outside sensor
- Session validated and created
- Students can now sign in

ActiveForStudents -> ActiveForSignOut:
- Instructor scans at inside sensor
- Students can now sign out
- Students can still sign in if not already signed in

ActiveForSignOut -> Inactive:
- Instructor scans at outside sensor
- Session ended
- All student tracking cleared

Any State -> Inactive:
- Instructor scans at outside sensor (session end)
- Force end session (admin override)
- System shutdown

================================================================================
FIGURE 14. SEQUENCE DIAGRAM FOR WEB ROOM MANAGEMENT
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to Rooms page -----> User Interface

User Interface ---- 2. Request rooms list -----> DATABASE

User Interface <------- 3. Returns rooms list ------- DATABASE

User Interface ---- 4. Display rooms table -----> Administrator

Administrator ---- 5. Click "Add Room" button -----> User Interface

User Interface ---- 6. Display room form modal -----> Administrator

Administrator ---- 7. Fill room form and submit -----> User Interface

User Interface ---- 8. Insert new room into ROOMS table -----> DATABASE

User Interface <------- 9. Returns new room ID ------- DATABASE

User Interface ---- 10. Refresh rooms list -----> DATABASE

User Interface ---- 11. Display success message -----> Administrator

================================================================================
FIGURE 15. SEQUENCE DIAGRAM FOR WEB UNIFIED MANAGEMENT (ROOMS, SUBJECTS, SCHEDULES)
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to /management page -----> User Interface

User Interface ---- 2. Request unified data (rooms, subjects, schedules) -----> DATABASE

User Interface <------- 3. Returns unified data with filters and statistics ------- DATABASE

User Interface ---- 4. Display unified management page -----> Administrator

Administrator ---- 5. Click "Show Import" button -----> User Interface

User Interface ---- 6. Display PDF import section -----> Administrator

Administrator ---- 7. Upload PDF file (drag & drop or file select) -----> User Interface

User Interface ---- 8. Store PDF file -----> (Internal processing)

Administrator ---- 9. Click "Parse & Preview" button -----> User Interface

User Interface ---- 10. Send PDF file to backend for parsing -----> DATABASE

User Interface <------- 11. Returns parsed data (subjects, students, instructors, rooms, schedules) ------- DATABASE

User Interface ---- 12. Display "PDF Parsed Successfully" message -----> Administrator

Administrator ---- 13. Click "View Uploaded Data" button -----> User Interface

User Interface ---- 14. Display data preview modal with tabs (subjects, students, instructors, rooms, schedules) -----> Administrator

Administrator ---- 15. Select import options (update existing, create missing rooms, skip duplicates) -----> User Interface

Administrator ---- 16. Click "Import Data" button -----> User Interface

User Interface ---- 17. Execute import with options -----> DATABASE

User Interface <------- 18. Returns import results (created, updated, errors) ------- DATABASE

User Interface ---- 19. Display import results -----> Administrator

User Interface ---- 20. Refresh unified data -----> DATABASE

Administrator ---- 21. Select sort by: Rooms / Subjects / Schedules -----> User Interface

User Interface ---- 22. Display filtered cards (rooms/subjects/schedules) -----> Administrator

Administrator ---- 23. Apply filters (academic year, semester, search) -----> User Interface

User Interface ---- 24. Filter and display updated cards -----> Administrator

Administrator ---- 25. Click "View Details" (eye icon) on room/subject/schedule card -----> User Interface

User Interface ---- 26. Request detailed data for selected item -----> DATABASE

User Interface <------- 27. Returns detailed data (room with schedules/subjects, subject with schedules/students, schedule with students) ------- DATABASE

User Interface ---- 28. Display detailed information modal -----> Administrator

DETAILED FLOW:

Administrator
    |
    | 1. Navigate to /management
    |
    v
User Interface
    |
    | 2. Request unified data
    |    GET /api/unified/data?academic_year=&semester=
    |
    v
DATABASE
    |
    | 3. Query ROOMS, SUBJECTS, CLASSSCHEDULES tables
    |    Return rooms with schedule counts
    |    Return subjects with schedule counts and enrollment counts
    |    Return schedules with subject and room information
    |    Return filters (academic years, semesters)
    |    Return statistics (total rooms, subjects, schedules, enrollments)
    |
    v
User Interface
    |
    | 4. Display unified management page
    |    Show PDF import section (collapsed)
    |    Show filters (sort by, academic year, semester, search)
    |    Show statistics cards
    |    Show data grid with rooms/subjects/schedules cards
    |
    v
Administrator
    |
    | 5. Click "Show Import" button
    |
    v
User Interface
    |
    | 6. Expand PDF import section
    |    Show upload area (drag & drop or file select)
    |
    | 7. Administrator uploads PDF file
    |
    | 8. Store PDF file in state
    |
    | 9. Administrator clicks "Parse & Preview"
    |
    | 10. Send PDF file to backend
    |     POST /api/import/preview
    |     Body: FormData with PDF file
    |
    v
DATABASE (Backend processing)
    |
    | 11. Parse PDF file
    |     Extract subjects, students, instructors, rooms, schedules
    |     Return parsed data with statistics
    |
    v
User Interface
    |
    | 12. Display "PDF Parsed Successfully" message
    |     Show parsed data statistics
    |     Show import options checkboxes
    |     Show "View Uploaded Data" and "Import Data" buttons
    |
    | 13. Administrator clicks "View Uploaded Data"
    |
    | 14. Display data preview modal
    |     Show tabs: Subjects, Students, Instructors, Rooms, Schedules
    |     Display parsed data in each tab
    |
    | 15. Administrator selects import options
    |     - Update existing records
    |     - Create missing rooms
    |     - Skip duplicates
    |
    | 16. Administrator clicks "Import Data"
    |
    | 17. Execute import
    |     POST /api/import/execute
    |     Body: { parsedData, options }
    |
    v
DATABASE
    |
    | 18. Process import
    |     Create/update subjects, students, instructors, rooms, schedules
    |     Create enrollments
    |     Return import results (created, updated, errors)
    |
    v
User Interface
    |
    | 19. Display import results
    |     Show created/updated counts for each entity type
    |     Show errors if any
    |
    | 20. Refresh unified data
    |     GET /api/unified/data
    |
    v
Administrator
    |
    | 21. Select sort by: Rooms / Subjects / Schedules
    |
    v
User Interface
    |
    | 22. Display filtered cards
    |     If Rooms: Show room cards with schedule counts
    |     If Subjects: Show subject cards with enrollment counts
    |     If Schedules: Show schedule groups by subject
    |
    | 23. Administrator applies filters
    |     Select academic year
    |     Select semester
    |     Enter search term
    |
    | 24. Filter and display updated cards
    |     Apply filters to current data
    |     Update card display
    |
    | 25. Administrator clicks "View Details" on card
    |
    | 26. Request detailed data
    |     GET /api/unified/room/:id (for rooms)
    |     GET /api/unified/subject/:id (for subjects)
    |     GET /api/unified/schedule/:id (for schedules)
    |
    v
DATABASE
    |
    | 27. Return detailed data
    |     For room: room details, subjects in room, schedules in room
    |     For subject: subject details, rooms, schedules, enrolled students
    |     For schedule: schedule details, enrolled students
    |
    v
User Interface
    |
    | 28. Display detailed information modal
    |     Show full details with related data
    |     Show students list (for subjects/schedules)
    |     Show schedules list (for rooms/subjects)
    |
    v
Administrator

================================================================================
FIGURE 16. SEQUENCE DIAGRAM FOR WEB SCHEDULE MANAGEMENT
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to Schedules page -----> User Interface

User Interface ---- 2. Request schedules, subjects, rooms, instructors -----> DATABASE

User Interface <------- 3. Returns all data ------- DATABASE

User Interface ---- 4. Display schedules table -----> Administrator

Administrator ---- 5. Click "Add Schedule" button -----> User Interface

User Interface ---- 6. Display schedule form modal -----> Administrator

Administrator ---- 7. Fill schedule form (subject, room, day, time slots) -----> User Interface

Administrator ---- 8. Submit schedule -----> User Interface

User Interface ---- 9. Check for schedule conflicts -----> DATABASE

User Interface <------- 10. Returns conflict check result ------- DATABASE

DECISION: Conflict found?

YES ---- User Interface ---- 11a. Display conflict error -----> Administrator

NO ---- User Interface ---- 11b. Insert new schedule into CLASSSCHEDULES table -----> DATABASE
NO ---- User Interface <------- 12. Returns new schedule ID ------- DATABASE
NO ---- User Interface ---- 13. Refresh schedules list -----> DATABASE
NO ---- User Interface ---- 14. Display success message -----> Administrator

================================================================================
FIGURE 17. SEQUENCE DIAGRAM FOR WEB BACKUP OPERATIONS
================================================================================

PARTICIPANTS: Administrator ----- User Interface ----- DATABASE

Administrator ---- 1. Navigate to Backup page -----> User Interface

User Interface ---- 2. Request backup statistics -----> DATABASE

User Interface <------- 3. Returns backup statistics ------- DATABASE

User Interface ---- 4. Display backup dashboard -----> Administrator

Administrator ---- 5. Select backup options -----> User Interface

Administrator ---- 6. Click "Create Backup" button -----> User Interface

User Interface ---- 7. Query DATABASE for all data -----> DATABASE

User Interface <------- 8. Returns database data ------- DATABASE

User Interface ---- 9. Generate backup file (ZIP) -----> (Internal processing)

User Interface ---- 10. Save backup file -----> DATABASE

User Interface ---- 11. Refresh backup list -----> DATABASE

User Interface ---- 12. Display success message -----> Administrator

Administrator ---- 13. Click "Download Backup" button -----> User Interface

User Interface ---- 14. Request backup file from DATABASE -----> DATABASE

User Interface <------- 15. Returns backup file data ------- DATABASE

User Interface ---- 16. Trigger browser download -----> Administrator

================================================================================
END OF SEQUENCE DIAGRAMS DOCUMENTATION
================================================================================

