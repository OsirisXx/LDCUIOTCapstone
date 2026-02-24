ACTIVITY DIAGRAMS DOCUMENTATION
IoT Attendance System - Liceo de Cagayan University

================================================================================
FIGURE 1. ACTIVITY DIAGRAM FOR FACULTY ROOM ACCESS AND SYSTEM ACTIVATION
VIA FINGERPRINT SCANNER
================================================================================

PARTICIPANTS: Faculty Member ----- User Interface ----- DATABASE ----- ESP32 Door Controller

START

Faculty Member Approaches external door

Faculty Member Places finger on External Fingerprint Scanner
(or scans RFID card)

External Fingerprint Scanner Captures fingerprint data
(or reads RFID identifier)

External Fingerprint Scanner Sends identifier and auth_method to System

System Receives scan request with identifier, auth_method, room_id

System Queries AUTHENTICATIONMETHODS table
JOIN USERS table
WHERE IDENTIFIER = ? AND METHODTYPE = ?
AND ISACTIVE = TRUE AND STATUS = 'Active'

DECISION: User found and valid?

NO ---- Log access denied in ACCESSLOGS ---- Display "Access Denied" ---- END

YES ---- Continue

DECISION: User is instructor?

NO ---- Log access denied in ACCESSLOGS ---- Display "Only instructors can access from outside" ---- END

YES ---- Continue

System Validates schedule using ScheduleValidationService
Check CLASSSCHEDULES for matching schedule
Check current time within schedule window (±15 minutes)

DECISION: Schedule valid?

NO ---- Log access denied in ACCESSLOGS ---- Display "Door access not allowed" ---- Display validation reason ---- END

YES ---- Continue

System Checks for active session in SESSIONS table
WHERE SCHEDULEID = ? AND SESSIONDATE = CURDATE() 
AND STATUS = 'active'

DECISION: Active session exists?

NO (Start Session):
- INSERT INTO SESSIONS (create new session)
  SET STATUS = 'active'
  SET STARTTIME = CURRENT_TIMESTAMP
  SET DOORUNLOCKEDAT = CURRENT_TIMESTAMP
- UPDATE ROOMS SET DOORSTATUS = 'unlocked'
- INSERT INTO ATTENDANCERECORDS (instructor time_in)
  SET SCANTYPE = 'time_in'
  SET LOCATION = 'outside'
  SET STATUS = 'Present'
- UPDATE ATTENDANCERECORDS (upgrade Early Arrival to Present)
  WHERE SCHEDULEID = ? AND STATUS = 'Early Arrival'
  SET STATUS = 'Present'
  SET SCANTYPE = 'early_arrival_upgraded'
- INSERT INTO ACCESSLOGS
  SET ACCESSTYPE = 'session_start'
  SET RESULT = 'success'
- Display "Session Started Successfully"
- Display door status: unlocked
- Send unlock signal to ESP32 door controller
- END

YES (End Session):
- UPDATE SESSIONS SET STATUS = 'ended'
  SET ENDTIME = CURRENT_TIMESTAMP
  SET DOORLOCKEDAT = CURRENT_TIMESTAMP
- UPDATE ROOMS SET DOORSTATUS = 'locked'
- INSERT INTO ATTENDANCERECORDS (instructor time_out)
  SET SCANTYPE = 'time_out'
  SET LOCATION = 'outside'
  SET STATUS = 'Present'
- INSERT INTO ACCESSLOGS
  SET ACCESSTYPE = 'session_end'
  SET RESULT = 'success'
- Display "Session Ended Successfully"
- Display door status: locked
- Send lock signal to ESP32 door controller
- END

================================================================================
FIGURE 2. ACTIVITY DIAGRAM FOR STUDENT ATTENDANCE RECORDING
================================================================================

PARTICIPANTS: Student ----- User Interface ----- DATABASE

START

Student enters room (after faculty member has activated session)

Student Places finger on Internal Fingerprint Scanner

Internal Fingerprint Scanner Captures fingerprint data

Internal Fingerprint Scanner Sends identifier to System

System Receives scan request with identifier, auth_method='fingerprint', room_id

System Queries AUTHENTICATIONMETHODS table
JOIN USERS table
WHERE IDENTIFIER = ? AND METHODTYPE = 'fingerprint'
AND ISACTIVE = TRUE AND STATUS = 'Active'

DECISION: User found and valid?

NO ---- Log access denied in ACCESSLOGS ---- Display "Invalid fingerprint" ---- END

YES ---- Continue

DECISION: User is student?

NO ---- Log access denied in ACCESSLOGS ---- Display "Only students can scan for attendance" ---- END

YES ---- Continue

System Validates using ScheduleValidationService
Check if student is enrolled in subject (SUBJECTENROLLMENT)
Check if active session exists (SESSIONS)
Verify session is active for attendance recording
Check room_id matches

DECISION: Validation passed?

NO ---- Log access denied in ACCESSLOGS ---- Display "Attendance recording not allowed" ---- Display validation reason ---- END

YES ---- Continue

System Checks existing attendance in ATTENDANCERECORDS
WHERE USERID = ? AND SCHEDULEID = ?
AND DATE(SCANDATETIME) = CURDATE()

DECISION: Existing attendance found?

YES ---- Check attendance status

IF STATUS = 'Early Arrival':
- UPDATE ATTENDANCERECORDS
  SET STATUS = 'Present'
  SET SCANTYPE = 'time_in_confirmation'
  SET LOCATION = 'inside'
  SET UPDATED_AT = CURRENT_TIMESTAMP
- INSERT INTO ACCESSLOGS
  SET ACCESSTYPE = 'early_arrival_confirmation'
  SET RESULT = 'success'
- Display "Early arrival confirmed successfully"
- Display student name and subject
- Display original timestamp and confirmation timestamp
- END

IF STATUS = 'Present' or 'Late':
- Log access denied in ACCESSLOGS
- Display "Attendance already recorded for today"
- END

NO (No existing attendance):
- Get session start time from SESSIONS table
  WHERE SESSIONID = ?
- Get late tolerance minutes from SETTINGS table
  WHERE SETTINGKEY = 'late_tolerance_minutes'
  (default: 15 minutes)
- Calculate current time vs session start time
- DECISION: Is student late? (current time > session start + tolerance)

IF LATE:
- INSERT INTO ATTENDANCERECORDS
  SET SCANTYPE = 'time_in'
  SET LOCATION = 'inside'
  SET STATUS = 'Late'
  SET AUTHMETHOD = 'fingerprint'
  SET ACADEMICYEAR = (from SETTINGS)
  SET SEMESTER = (from SETTINGS)
- INSERT INTO ACCESSLOGS
  SET ACCESSTYPE = 'attendance_scan'
  SET RESULT = 'success'
- Display "Attendance recorded successfully (Late)"
- Display student name and subject
- Display status: late
- END

IF NOT LATE:
- INSERT INTO ATTENDANCERECORDS
  SET SCANTYPE = 'time_in'
  SET LOCATION = 'inside'
  SET STATUS = 'Present'
  SET AUTHMETHOD = 'fingerprint'
  SET ACADEMICYEAR = (from SETTINGS)
  SET SEMESTER = (from SETTINGS)
- INSERT INTO ACCESSLOGS
  SET ACCESSTYPE = 'attendance_scan'
  SET RESULT = 'success'
- Display "Attendance recorded successfully"
- Display student name and subject
- Display status: present
- END

================================================================================
FIGURE 3. ACTIVITY DIAGRAM FOR ADMINISTRATOR LOGIN
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

START

Administrator Opens login page

Administrator Enters email/username and password

System Receives login request with email and password

System Validates input
Check email format
Check password length (minimum 6 characters)

DECISION: Input valid?

NO ---- Display validation errors ---- END

YES ---- Continue

System Queries USERS table
SELECT * FROM USERS WHERE EMAIL = ?

DECISION: User found by email?

NO ---- Query USERS table by STUDENTID or FACULTYID
SELECT * FROM USERS WHERE STUDENTID = ? OR FACULTYID = ?

DECISION: User found by ID?

NO ---- Display "Invalid credentials" ---- END

YES ---- Continue

YES ---- Continue

System Checks user status
Verify STATUS = 'Active'

DECISION: Account active?

NO ---- Display "Account is inactive" ---- END

YES ---- Continue

System Verifies password
Use bcrypt.compare() to compare provided password
with stored PASSWORD_HASH

DECISION: Password valid?

NO ---- Display "Invalid credentials" ---- END

YES ---- Continue

System Normalizes user role
Convert USERTYPE to lowercase

System Generates JWT token
jwt.sign({ userId, email, role }, JWT_SECRET, { expiresIn: '7d' })

System Returns token and user info
{ token, user: { id, email, role, first_name, last_name, status } }

System Stores JWT token in localStorage (frontend)

System Redirects to admin dashboard

END

================================================================================
FIGURE 4. ACTIVITY DIAGRAM FOR FACULTY EXIT AND SYSTEM DEACTIVATION
================================================================================

PARTICIPANTS: Faculty Member ----- User Interface ----- DATABASE ----- ESP32 Door Controller

START

Faculty Member Intends to leave and end the session

Faculty Member Places finger on External Fingerprint Scanner
(or scans RFID card)
(location = 'outside')

External Fingerprint Scanner Captures fingerprint data
(or reads RFID identifier)

External Fingerprint Scanner Sends identifier and auth_method to System

System Receives scan request with identifier, auth_method, room_id, location='outside'

System Queries AUTHENTICATIONMETHODS table
JOIN USERS table
WHERE IDENTIFIER = ? AND METHODTYPE = ?
AND ISACTIVE = TRUE AND STATUS = 'Active'

DECISION: User found and valid?

NO ---- Log access denied in ACCESSLOGS ---- Display "Invalid credentials" ---- END

YES ---- Continue

DECISION: User is instructor?

NO ---- Log access denied in ACCESSLOGS ---- Display "Only instructors can end sessions" ---- END

YES ---- Continue

System Checks for active session in SESSIONS table
WHERE INSTRUCTORID = ? AND ROOMID = ?
AND SESSIONDATE = CURDATE() AND STATUS = 'active'

DECISION: Active session found?

NO ---- Log access denied in ACCESSLOGS ---- Display "No active session found" ---- END

YES ---- Continue

System Verifies faculty identity matches session instructor
Check if scanned instructor is the same as session instructor

DECISION: Identity verified?

NO ---- Log access denied in ACCESSLOGS ---- Display "Only session owner can end session" ---- END

YES ---- Continue

System Ends session
UPDATE SESSIONS SET STATUS = 'ended'
SET ENDTIME = CURRENT_TIMESTAMP
SET DOORLOCKEDAT = CURRENT_TIMESTAMP
SET UPDATED_AT = CURRENT_TIMESTAMP
WHERE SESSIONID = ?

System Locks door
UPDATE ROOMS SET DOORSTATUS = 'locked'
SET UPDATED_AT = CURRENT_TIMESTAMP
WHERE ROOMID = ?

System Records instructor attendance
INSERT INTO ATTENDANCERECORDS
SET USERID = instructor_id
SET SCHEDULEID = schedule_id
SET SCANTYPE = 'time_out'
SET AUTHMETHOD = auth_method
SET LOCATION = 'outside'
SET STATUS = 'Present'
SET ACADEMICYEAR = (from SETTINGS)
SET SEMESTER = (from SETTINGS)

System Logs access
INSERT INTO ACCESSLOGS
SET USERID = instructor_id
SET ROOMID = room_id
SET ACCESSTYPE = 'session_end'
SET AUTHMETHOD = auth_method
SET LOCATION = 'outside'
SET RESULT = 'success'
SET REASON = 'Session ended'

System Displays "Session Ended Successfully"
Display session status: ended
Display door status: locked
Display instructor name

System Sends lock signal to ESP32 door controller
POST to ESP32 /api/rfid-scan or /api/fingerprint-scan
{ action: 'lock', user: instructor_name }

END

================================================================================
NOTES
================================================================================

ACTIVITY DIAGRAM CONVENTIONS:

START NODE:
- Represented by solid black circle
- Marks the beginning of the process flow

ACTIVITIES:
- Represented by rounded rectangles
- Show actions performed by system components
- Executed in sequence from top to bottom

DECISION NODES:
- Represented by diamond shapes
- Show conditional branching points
- Have two or more outgoing paths (YES/NO, or multiple conditions)

END NODE:
- Represented by solid black circle with white outline
- Marks the completion of the process flow

LOOPS:
- Shown by arrows that loop back to previous activities
- Used for retry logic or iterative processes

PARALLEL ACTIVITIES:
- Shown by multiple activities that can occur simultaneously
- In this system, database operations are sequential but some logs can be written in parallel

ERROR PATHS:
- Shown by decision nodes with NO paths
- Lead to error messages and process termination
- Always logged in ACCESSLOGS table before ending

SUCCESS PATHS:
- Shown by decision nodes with YES paths
- Lead to successful completion of the process
- Always include logging and user feedback

SYSTEM COMPONENTS:

External Fingerprint Scanner:
- Located outside the room
- Used by instructors to start/end sessions
- Captures fingerprint data or reads RFID cards
- Sends data to backend API

Internal Fingerprint Scanner:
- Located inside the room
- Used by students to record attendance
- Captures fingerprint data only
- Sends data to backend API

Backend API:
- Receives scan requests from scanners
- Validates credentials and permissions
- Queries and updates database
- Sends responses and control signals

DATABASE:
- Stores all system data
- Validates user credentials
- Manages sessions and attendance
- Logs all access attempts

ESP32 Door Controller:
- Receives unlock/lock signals from backend
- Controls physical door mechanism
- Can be accessed via HTTP API

VALIDATION STEPS:

User Authentication:
1. Check if identifier exists in AUTHENTICATIONMETHODS
2. Check if authentication method is active
3. Check if user account is active
4. Verify user role matches required role

Schedule Validation:
1. Check if schedule exists for current time and room
2. Verify user has permission for this schedule
3. Check if schedule is within valid time window
4. Verify academic year and semester match

Session Validation:
1. Check if session exists
2. Verify session is active
3. Verify session belongs to correct instructor
4. Verify session is for correct room and schedule

Enrollment Validation:
1. Check if student is enrolled in subject
2. Verify enrollment is active
3. Verify enrollment matches current academic year and semester

ERROR HANDLING:

All errors are logged in ACCESSLOGS table with:
- USERID (if available)
- ROOMID
- ACCESSTYPE (type of access attempted)
- AUTHMETHOD (authentication method used)
- LOCATION (inside/outside)
- RESULT (success/denied)
- REASON (detailed error message)

Common error scenarios:
- Invalid credentials: User not found or authentication failed
- Invalid role: User does not have required permissions
- No active session: Session does not exist or is not active
- Already recorded: Attendance already recorded for today
- Validation failed: Schedule or enrollment validation failed
- Account inactive: User account is disabled

SUCCESS SCENARIOS:

Session Start:
- Session created in SESSIONS table
- Door unlocked in ROOMS table
- Instructor attendance recorded
- Early arrival students upgraded to Present
- Access logged as success

Session End:
- Session updated to ended status
- Door locked in ROOMS table
- Instructor time_out recorded
- Access logged as success

Attendance Recording:
- Attendance record created in ATTENDANCERECORDS
- Status set to Present or Late based on timing
- Access logged as success
- Student receives confirmation message

Administrator Login:
- JWT token generated
- Token stored in frontend
- User redirected to dashboard
- Session established for 7 days

================================================================================
FIGURE 5. ACTIVITY DIAGRAM FOR FUTRONIC SYSTEM USER ENROLLMENT
================================================================================

PARTICIPANTS: Administrator ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- User

START

Administrator Opens FutronicAttendanceSystem application

Administrator Navigates to "User Enrollment" tab

Administrator Selects user from user table
(displays users loaded from DATABASE)

DECISION: User selected?

NO ---- Display "Please select a user first" ---- END

YES ---- Continue

Administrator Clicks "Start Enrollment" button

System Validates user selection
Check if selectedUser is not null

DECISION: User data valid?

NO ---- Display "Invalid user data" ---- END

YES ---- Continue

System Displays enrollment confirmation dialog
Show user name, type, department

Administrator Confirms enrollment

System Initializes FutronicEnrollment operation
Set FakeDetection = false
Set FARN = 200
Set FastMode = false
Create new FutronicEnrollment object

System Stops attendance operation
(if running)

System Displays "Get ready: You'll scan your thumb multiple times"

User Places finger on scanner (Scan 1)

Futronic SDK Captures fingerprint template (Scan 1)

System Validates template quality (Scan 1)

DECISION: Template quality valid?

NO ---- Display "Poor quality. Please try again" ---- Loop back to "User Places finger on scanner (Scan 1)"

YES ---- Continue

System Displays "Scan finger again"

User Places finger on scanner (Scan 2)

Futronic SDK Captures fingerprint template (Scan 2)

System Compares templates (Scan 1 and Scan 2)

DECISION: Templates match?

NO ---- Display "Templates don't match. Please try again" ---- Loop back to "User Places finger on scanner (Scan 1)"

YES ---- Continue

System Displays "Scan finger third time"

User Places finger on scanner (Scan 3)

Futronic SDK Captures fingerprint template (Scan 3)

System Creates composite template from all three scans

System Validates composite template quality
Check template size and validity

DECISION: Composite template valid?

NO ---- Display "Template quality poor. Please try again" ---- Loop back to "User Places finger on scanner (Scan 1)"

YES ---- Continue

System Saves fingerprint template to DATABASE
INSERT INTO AUTHENTICATIONMETHODS
SET METHODTYPE = 'Fingerprint'
SET IDENTIFIER = 'FP_{STUDENTID}'
SET FINGERPRINTTEMPLATE = template_data
SET ISACTIVE = TRUE
SET USERID = selectedUser.USERID

DECISION: Save successful?

NO ---- Display "Failed to save fingerprint" ---- END

YES ---- Continue

System Refreshes user list from DATABASE
Query all users from USERS table

System Updates user table display
Show updated user list with fingerprint status

System Displays "Enrollment complete! Fingerprint added for '{user name}'"

System Clears user selection

END

================================================================================
FIGURE 6. ACTIVITY DIAGRAM FOR FUTRONIC SYSTEM DUAL SENSOR MODE
================================================================================

PARTICIPANTS: User ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- ESP32 Door Controller

START

System Initializes dual sensor mode
Load device configuration from config file

System Checks device configuration
Read AllowFingerprintOnly, AllowRfidOnly, AllowInstructorDoorAccess

System Creates inside sensor operation
m_InsideSensorOperation = new FutronicIdentification()
Set currentScanLocation = "inside"

System Creates outside sensor operation
m_OutsideSensorOperation = new FutronicIdentification()
Set currentScanLocation = "outside"

System Starts both sensor operations
Begin listening for fingerprint scans on both sensors

DECISION: Fingerprint detected?

NO ---- Continue listening ---- Loop back to "DECISION: Fingerprint detected?"

YES ---- Continue

System Determines scan location
Check which sensor triggered the event
Set currentScanLocation = "inside" or "outside"

Futronic SDK Identifies user from fingerprint
Match fingerprint against enrolled templates

System Queries DATABASE for user info
SELECT * FROM USERS WHERE USERID = ?

DECISION: User found?

NO ---- Display "User not found" ---- Log access denied ---- END

YES ---- Continue

System Gets user type and role
Check USERTYPE from user record

DECISION: Scan location?

INSIDE SCAN:
- DECISION: User is student?
  - YES: Process student attendance recording
  - NO: DECISION: User is instructor?
    - YES: Process instructor session end
    - NO: Display "Invalid user type for inside scan" ---- END

OUTSIDE SCAN:
- DECISION: User is instructor?
  - YES: Process instructor session start/end
  - NO: DECISION: User is student?
    - YES: Process student early arrival or door access
    - NO: Display "Invalid user type for outside scan" ---- END

STUDENT ATTENDANCE RECORDING (Inside):
- Check if session is active
- DECISION: Session active?
  - YES: Record attendance
    - Check if already signed in
    - DECISION: Already signed in?
      - YES: Allow door access only
      - NO: Record attendance and allow access
  - NO: Display "No active session" ---- END

INSTRUCTOR SESSION START/END (Outside):
- Check for active session
- DECISION: Active session exists?
  - YES: End session
    - Update SESSIONS SET STATUS = 'ended'
    - Update ROOMS SET DOORSTATUS = 'locked'
    - Record instructor time_out
  - NO: Start session
    - Create new SESSIONS record
    - Update ROOMS SET DOORSTATUS = 'unlocked'
    - Record instructor time_in
    - Upgrade early arrival students to Present

STUDENT EARLY ARRIVAL (Outside):
- Check if early arrival is enabled
- DECISION: Early arrival enabled?
  - YES: Process early arrival verification
    - Set awaitingEarlyArrivalVerification = true
    - Display "Please scan RFID to complete early arrival"
    - Wait for RFID scan
    - DECISION: RFID user matches fingerprint user?
      - YES: Record early arrival
      - NO: Display "Verification failed" ---- END
  - NO: Display "Early arrival not enabled" ---- END

System Sends door control signal to ESP32
POST /api/lock-control or /api/fingerprint-scan
{ action: "open" or "lock", user: user_name }

ESP32 Door Controller Processes door control signal
Unlock or lock door mechanism

System Updates DATABASE
Insert ATTENDANCERECORDS
Insert ACCESSLOGS

System Displays result to user
Show "Access Granted" or "Attendance Recorded"

END

================================================================================
FIGURE 7. ACTIVITY DIAGRAM FOR FUTRONIC SYSTEM CROSS-TYPE VERIFICATION
================================================================================

PARTICIPANTS: User ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- ESP32 Door Controller

START

User Scans RFID card (first authentication)

System Identifies user by RFID
Query AUTHENTICATIONMETHODS WHERE IDENTIFIER = RFID_data

DECISION: User found?

NO ---- Display "RFID not recognized" ---- END

YES ---- Continue

System Sets cross-type verification state
awaitingCrossTypeVerification = true
firstScanType = "RFID"
pendingCrossVerificationUser = userName
pendingCrossVerificationGuid = userGuid
crossVerificationStartTime = DateTime.Now

System Displays "Please scan fingerprint to verify"
Show waiting status on screen
Show user name from RFID scan

System Starts verification timeout timer
(20 seconds timeout)

User Places finger on fingerprint scanner (second authentication)

Futronic SDK Captures fingerprint and identifies user

System Compares user names
Check if userName == pendingCrossVerificationUser
Check if userGuid == pendingCrossVerificationGuid

DECISION: User names match?

NO ---- Display "Verification failed: RFID and Fingerprint mismatch" ---- Reset verification state ---- Log denied access to ACCESSLOGS ---- END

YES ---- Continue

System Resets verification state
awaitingCrossTypeVerification = false
firstScanType = ""
pendingCrossVerificationUser = ""
pendingCrossVerificationGuid = ""
crossVerificationStartTime = DateTime.MinValue

System Gets user type from DATABASE

DECISION: User is instructor?

YES:
- DECISION: Session state?
  - Inactive: Start session
    - Create SESSIONS record
    - Unlock door
    - Record instructor time_in
  - Active: End session
    - Update SESSIONS SET STATUS = 'ended'
    - Lock door
    - Record instructor time_out

NO (Student):
- DECISION: Session active?
  - YES: Record attendance
    - Check if already signed in
    - DECISION: Already signed in?
      - YES: Allow door access only
      - NO: Record attendance and allow access
  - NO: Display "No active session" ---- END

System Sends door control signal to ESP32
POST /api/lock-control
{ action: "open", user: user_name }

System Updates DATABASE
Insert ATTENDANCERECORDS
Insert ACCESSLOGS

System Displays "Verified: Access Granted"
Show user name and action taken

END

TIMEOUT HANDLING:
- If verification not completed within 20 seconds
- Reset verification state
- Display "Verification timeout"
- Log timeout to ACCESSLOGS
- END

REVERSE FLOW (Fingerprint First, Then RFID):
- Same process but firstScanType = "FINGERPRINT"
- User scans fingerprint first
- System waits for RFID scan
- Verification process is identical

================================================================================
FIGURE 8. ACTIVITY DIAGRAM FOR FUTRONIC SYSTEM EARLY ARRIVAL HANDLING
================================================================================

PARTICIPANTS: Student ----- FutronicAttendanceSystem ----- DATABASE ----- Futronic SDK ----- ESP32 Door Controller

START

Student Scans fingerprint at outside sensor
(Before session start time, up to 15 minutes early)

Futronic SDK Identifies student from fingerprint

System Checks if session is active
Query SESSIONS WHERE STATUS = 'active'

DECISION: Session active?

YES ---- Process normal attendance recording ---- END

NO ---- Continue

System Checks if early arrival is enabled
Check device configuration
Check IsFingerprintOnlyMode

DECISION: Early arrival enabled?

NO ---- Display "Early arrival not enabled" ---- END

YES ---- Continue

System Checks for upcoming schedule
Query CLASSSCHEDULES
WHERE STARTTIME > CURRENT_TIME
AND STARTTIME <= CURRENT_TIME + 15 minutes
AND ROOMID = current_room_id

DECISION: Upcoming schedule found?

NO ---- Display "No upcoming schedule found" ---- END

YES ---- Continue

System Sets early arrival verification state
awaitingEarlyArrivalVerification = true
earlyPendingUser = userName
earlyPendingGuid = userGuid
earlyVerificationStartTime = DateTime.Now
earlyFirstScanType = "FINGERPRINT"

System Displays "Please scan RFID to complete early arrival"
Show user name from fingerprint scan

System Starts verification timeout timer
(20 seconds timeout)

Student Scans RFID card

System Identifies user by RFID
Query AUTHENTICATIONMETHODS WHERE IDENTIFIER = RFID_data

System Compares users
Check if RFID_user == earlyPendingUser
Check if RFID_guid == earlyPendingGuid

DECISION: Users match?

NO ---- Display "Early arrival verification failed: RFID and Fingerprint mismatch" ---- Reset early arrival state ---- END

YES ---- Continue

System Resets early arrival verification state
awaitingEarlyArrivalVerification = false
earlyPendingUser = ""
earlyPendingGuid = ""
earlyFirstScanType = ""
earlyVerificationStartTime = DateTime.MinValue

System Records early arrival in DATABASE
INSERT INTO ATTENDANCERECORDS
SET USERID = userGuid
SET SCHEDULEID = schedule_id
SET SCANTYPE = 'early_arrival'
SET STATUS = 'Early Arrival'
SET LOCATION = 'outside'
SET AUTHMETHOD = 'fingerprint'
SET ACADEMICYEAR = (from SETTINGS)
SET SEMESTER = (from SETTINGS)

System Logs early arrival access
INSERT INTO ACCESSLOGS
SET ACCESSTYPE = 'early_arrival_scan'
SET RESULT = 'success'

System Sends door unlock signal to ESP32
POST /api/lock-control
{ action: "open", user: user_name, message: "Early Arrival recorded. Scan inside at start." }

System Displays "Early arrival recorded successfully"
Show student name and subject
Show message: "Please scan inside when class starts for confirmation"

Student Enters room

Student Waits for session to start

Instructor Starts session
(Scans at outside sensor)

System Upgrades early arrival to Present
UPDATE ATTENDANCERECORDS
SET STATUS = 'Present'
SET SCANTYPE = 'early_arrival_upgraded'
WHERE STATUS = 'Early Arrival'
AND SCHEDULEID = schedule_id
AND DATE(SCANDATETIME) = CURDATE()

OR Student scans inside when session starts:
Student Places finger on inside scanner

Futronic SDK Identifies student

System Checks for early arrival record
Query ATTENDANCERECORDS
WHERE USERID = ?
AND SCHEDULEID = ?
AND STATUS = 'Early Arrival'
AND DATE(SCANDATETIME) = CURDATE()

DECISION: Early arrival record found?

YES:
- Upgrade early arrival to Present
  UPDATE ATTENDANCERECORDS
  SET STATUS = 'Present'
  SET SCANTYPE = 'time_in_confirmation'
  SET LOCATION = 'inside'
  SET UPDATED_AT = CURRENT_TIMESTAMP
- Display "Early arrival confirmed successfully"
- Show original timestamp and confirmation timestamp
- END

NO:
- Process normal attendance recording
- Record attendance as Present or Late
- END

TIMEOUT HANDLING:
- If verification not completed within 20 seconds
- Reset early arrival verification state
- Display "Early arrival verification timeout"
- Log timeout to ACCESSLOGS
- END

================================================================================
FIGURE 9. ACTIVITY DIAGRAM FOR WEB DASHBOARD VIEWING
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

START

Administrator Navigates to Dashboard page
(URL: /dashboard)

Frontend Checks authentication token
Get token from localStorage

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests dashboard statistics
GET /api/dashboard/stats
Headers: Authorization: Bearer {token}

Backend API Validates token
Verify JWT token signature and expiration

DECISION: Token valid?

NO ---- Return 401 Unauthorized ---- Frontend redirects to login ---- END

YES ---- Continue

Backend API Queries total users count
SELECT COUNT(*) FROM USERS WHERE STATUS = 'Active'

Backend API Queries active sessions count
SELECT COUNT(*) FROM SESSIONS WHERE STATUS = 'active'
AND SESSIONDATE = CURDATE()

Backend API Queries today's attendance count
SELECT COUNT(*) FROM ATTENDANCERECORDS
WHERE DATE(SCANDATETIME) = CURDATE()

Backend API Queries total rooms count
SELECT COUNT(*) FROM ROOMS WHERE STATUS = 'Available'

Backend API Queries recent activity
SELECT * FROM ATTENDANCERECORDS
JOIN USERS ON ATTENDANCERECORDS.USERID = USERS.USERID
JOIN CLASSSCHEDULES ON ATTENDANCERECORDS.SCHEDULEID = CLASSSCHEDULES.SCHEDULEID
JOIN SUBJECTS ON CLASSSCHEDULES.SUBJECTID = SUBJECTS.SUBJECTID
ORDER BY ATTENDANCERECORDS.SCANDATETIME DESC
LIMIT 10

Backend API Queries online devices
SELECT * FROM DEVICES WHERE STATUS = 'online'

Backend API Consolidates dashboard data
Combine all query results into JSON response

Backend API Returns dashboard data to Frontend
{ totalUsers, activeSessions, todayAttendance, totalRooms, recentActivity, devicesOnline }

Frontend Receives dashboard data

Frontend Displays statistics cards
Show: Total Users, Active Sessions, Today's Attendance, Total Rooms

Frontend Displays active sessions list
Show: Session time, Subject, Instructor, Room, Status

Frontend Displays recent activity feed
Show: Recent attendance records with timestamps

Frontend Displays device status indicators
Show: Online devices, Offline devices

Frontend Sets up real-time updates
Poll /api/devices/online every 5 seconds
Refresh dashboard data periodically

END

REAL-TIME UPDATES:
- Frontend polls /api/devices/online every 5 seconds
- Frontend refreshes dashboard data every 30 seconds
- WebSocket connections for real-time updates (if implemented)

================================================================================
FIGURE 10. ACTIVITY DIAGRAM FOR WEB USER MANAGEMENT
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

START

Administrator Navigates to Users page
(URL: /users)

Frontend Checks authentication token

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests users list
GET /api/users?page=1&limit=50&type=all&status=all

Backend API Queries USERS table with filters
SELECT * FROM USERS
WHERE (filters applied: type, status, search)
ORDER BY CREATED_AT DESC
LIMIT ? OFFSET ?

Backend API Returns users data with pagination
{ users: [...], pagination: { page, limit, total, pages } }

Frontend Receives users list

Frontend Displays users table
Show: Name, Email, Type, Status, Actions (Edit, Delete)

DECISION: Administrator action?

ADD USER:
- Administrator clicks "Add User" button
- Frontend displays user form modal
- Administrator fills form (First Name, Last Name, Email, User Type, etc.)
- Administrator submits form
- Frontend validates form data
- DECISION: Form valid?
  - NO: Display validation errors ---- Loop back to form
  - YES: Send POST /api/users request
    - Backend validates user data
    - Backend checks if user exists
    - DECISION: User exists?
      - YES: Return error "User already exists" ---- END
      - NO: Insert new user into USERS table
        - Generate UUID for USERID
        - Hash password (if provided)
        - INSERT INTO USERS
        - Return success response
    - Frontend refreshes users list
    - Frontend displays success message
    - END

EDIT USER:
- Administrator clicks "Edit User" button
- Frontend displays edit form with user data
- Administrator modifies user data
- Administrator submits form
- Frontend validates form data
- Send PUT /api/users/:id request
- Backend validates user data
- Backend updates user in USERS table
- UPDATE USERS SET ... WHERE USERID = ?
- Backend returns success response
- Frontend refreshes users list
- Frontend displays success message
- END

DELETE USER:
- Administrator clicks "Delete User" button
- Frontend displays delete confirmation modal
- Administrator confirms deletion
- Send DELETE /api/users/:id request
- Backend deletes user from USERS table
- DELETE FROM USERS WHERE USERID = ?
- (May cascade delete related records)
- Backend returns success response
- Frontend refreshes users list
- Frontend displays success message
- END

BULK DELETE:
- Administrator selects multiple users
- Administrator clicks "Bulk Delete" button
- Frontend displays bulk delete confirmation modal
- Administrator confirms bulk deletion
- Send POST /api/users/bulk-delete request
- Body: { user_ids: [...] }
- Backend deletes multiple users in single transaction
- DELETE FROM USERS WHERE USERID IN (?)
- Backend returns success response
- Frontend refreshes users list
- Frontend displays success message
- END

IMPORT USERS:
- Administrator clicks "Import Users" button
- Frontend displays file upload modal
- Administrator selects CSV/PDF file
- Administrator uploads file
- Frontend sends POST /api/users/import request
- Body: FormData with file
- Backend parses CSV/PDF file
- Backend validates imported user data
- DECISION: Data valid?
  - NO: Return error with validation details ---- END
  - YES: Bulk insert users into USERS table
    - INSERT INTO USERS ... (multiple records)
    - Return import results
      { success_count, error_count, errors: [...] }
- Frontend displays import results
- Show: Success count, Error count, Error details
- Frontend refreshes users list
- END

FILTER AND SEARCH:
- Administrator applies filters (type, status)
- Administrator enters search term
- Frontend requests filtered users list
- GET /api/users?type=student&status=active&search=john
- Backend queries USERS table with filters
- Backend returns filtered users list
- Frontend displays filtered users table
- END

END

================================================================================
FIGURE 11. ACTIVITY DIAGRAM FOR WEB REPORTS GENERATION
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

START

Administrator Navigates to Reports page
(URL: /reports)

Frontend Checks authentication token

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests initial data
GET /api/subjects
GET /api/settings

Backend API Queries SUBJECTS table
SELECT * FROM SUBJECTS WHERE ARCHIVED_AT IS NULL

Backend API Queries SETTINGS table
SELECT * FROM SETTINGS WHERE SETTINGKEY IN ('current_academic_year', 'current_semester')

Backend API Returns initial data
{ subjects: [...], academicYears: [...], semesters: [...] }

Frontend Receives initial data

Frontend Displays filters and empty reports table
Show: Date range picker, Subject dropdown, Academic Year dropdown, Semester dropdown

Administrator Selects filters
Select: Start Date, End Date, Subject, Academic Year, Semester

Administrator Applies filters
Click "Apply Filters" button

Frontend Requests attendance reports with filters
GET /api/reports/attendance?startDate=...&endDate=...&subjectId=...&academicYear=...&semester=...

Backend API Queries ATTENDANCERECORDS with filters
SELECT ar.*, u.*, s.*, cs.*
FROM ATTENDANCERECORDS ar
JOIN USERS u ON ar.USERID = u.USERID
JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
WHERE (filters applied)
ORDER BY ar.DATE DESC, ar.TIMEIN ASC

Backend API Calculates statistics
- Total attendance count
- Present count
- Late count
- Absent count
- Attendance percentage

Backend API Returns reports data
{ reports: [...], statistics: {...} }

Frontend Receives reports data

Frontend Groups reports by subject/date/session
Group by: Subject, Date, or Session (based on groupBy selection)

Frontend Displays grouped reports
Show: Report cards with statistics for each group

DECISION: Administrator action?

VIEW DETAILS:
- Administrator clicks "View Details" on report card
- Frontend requests session roster
- GET /api/reports/attendance-with-absents?date=...&scheduleId=...
- Backend queries enrolled students for schedule
- Backend queries attendance records for session
- Backend merges enrolled students with attendance
- Backend marks absent students
- Backend returns session roster
- Frontend displays session roster modal
- Show: Student name, Status (Present/Late/Absent), Time scanned
- END

EXPORT TO CSV:
- Administrator clicks "Export to CSV" button
- Frontend generates CSV file from reports data
- Convert reports data to CSV format
- Include: Date, Student Name, Subject, Status, Time
- Frontend triggers browser download
- CSV file downloads to administrator's computer
- END

CLEAR FILTERS:
- Administrator clicks "Clear Filters" button
- Frontend resets all filters to default values
- Frontend requests reports without filters
- Backend returns all attendance records
- Frontend displays all reports
- END

CHANGE GROUP BY:
- Administrator selects group by option (Subject/Date/Session)
- Frontend regroups reports data
- Frontend displays regrouped reports
- END

END

================================================================================
FIGURE 12. ACTIVITY DIAGRAM FOR WEB ARCHIVE OPERATIONS
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE ----- Backend Service ----- File System

START

Administrator Navigates to Archive page
(URL: /archive)

Frontend Checks authentication token

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests archive dashboard statistics
GET /api/archive/dashboard

Backend API Queries archived data counts
SELECT COUNT(*) FROM SUBJECTS WHERE ARCHIVED_AT IS NOT NULL
SELECT COUNT(*) FROM ROOMS WHERE ARCHIVED_AT IS NOT NULL
SELECT COUNT(*) FROM CLASSSCHEDULES WHERE ARCHIVED_AT IS NOT NULL
SELECT COUNT(*) FROM USERS WHERE ARCHIVED_AT IS NOT NULL
SELECT COUNT(*) FROM ATTENDANCERECORDS WHERE ARCHIVED_AT IS NOT NULL

Backend API Returns archive dashboard data
{ stats: { subjects: count, rooms: count, schedules: count, users: count, attendance: count } }

Frontend Receives archive dashboard data

Frontend Displays archive dashboard
Show: Archive statistics for each category
Show: Archive options (Archive Subjects, Archive Rooms, etc.)

DECISION: Administrator action?

ARCHIVE SUBJECTS:
- Administrator clicks "Archive Subjects" button
- Frontend displays archive modal
- Administrator selects academic year and semester
- Administrator enters archive reason
- Administrator confirms archive operation
- Frontend sends POST /api/archive/subjects request
- Body: { academic_year, semester, reason }
- Backend validates archive request
- Backend archives subjects
  - UPDATE SUBJECTS SET ARCHIVED_AT = CURRENT_TIMESTAMP
    WHERE ACADEMICYEAR = ? AND SEMESTER = ?
- Backend archives related schedules
  - UPDATE CLASSSCHEDULES SET ARCHIVED_AT = CURRENT_TIMESTAMP
    WHERE SUBJECTID IN (SELECT SUBJECTID FROM SUBJECTS WHERE ARCHIVED_AT IS NOT NULL)
- Backend archives related enrollments
  - UPDATE SUBJECTENROLLMENT SET ARCHIVED_AT = CURRENT_TIMESTAMP
    WHERE SUBJECTID IN (SELECT SUBJECTID FROM SUBJECTS WHERE ARCHIVED_AT IS NOT NULL)
- Backend creates backup of archived data
  - Call backup service to create backup file
  - Generate ZIP file with archived data
- Backend returns archive success response
- Frontend refreshes archive dashboard
- Frontend displays success message
- END

ARCHIVE ROOMS:
- Administrator clicks "Archive Rooms" button
- Frontend displays archive modal with room selection
- Administrator selects rooms to archive
- Administrator enters archive reason
- Administrator confirms archive operation
- Frontend sends POST /api/archive/rooms request
- Body: { room_ids: [...], reason }
- Backend archives rooms
  - UPDATE ROOMS SET ARCHIVED_AT = CURRENT_TIMESTAMP
    WHERE ROOMID IN (?)
- Backend archives related schedules
  - UPDATE CLASSSCHEDULES SET ARCHIVED_AT = CURRENT_TIMESTAMP
    WHERE ROOMID IN (?)
- Backend creates backup
- Backend returns archive success response
- Frontend refreshes archive dashboard
- Frontend displays success message
- END

VIEW ARCHIVED DATA:
- Administrator clicks "View Archived Subjects" tab (or other archived category)
- Frontend requests archived data
- GET /api/archive/subjects (or /api/archive/rooms, etc.)
- Backend queries archived data
  - SELECT * FROM SUBJECTS WHERE ARCHIVED_AT IS NOT NULL
- Backend returns archived data
- Frontend displays archived data table
- Show: Archived items with archive date and reason
- END

UNARCHIVE:
- Administrator clicks "Unarchive" button on archived item
- Frontend displays unarchive confirmation modal
- Administrator confirms unarchive operation
- Frontend sends POST /api/archive/unarchive request
- Body: { category: 'subjects', id: subject_id }
- Backend unarchives item
  - UPDATE SUBJECTS SET ARCHIVED_AT = NULL WHERE SUBJECTID = ?
- Backend unarchives related records
  - UPDATE CLASSSCHEDULES SET ARCHIVED_AT = NULL WHERE SUBJECTID = ?
  - UPDATE SUBJECTENROLLMENT SET ARCHIVED_AT = NULL WHERE SUBJECTID = ?
- Backend returns unarchive success response
- Frontend refreshes archived data list
- Frontend displays success message
- END

END

ARCHIVE CATEGORIES:
- Subjects: Archive by academic year and semester
- Rooms: Archive specific rooms
- Schedules: Archive specific class schedules
- Users: Archive specific users
- Attendance: Archive attendance records by date range

BACKUP CREATION:
- Automatic backup created when archiving
- Backup file stored in backups directory
- Backup includes archived data and metadata
- Backup can be restored later if needed

================================================================================
FIGURE 13. ACTIVITY DIAGRAM FOR FUTRONIC SYSTEM SESSION STATE MANAGEMENT
================================================================================

PARTICIPANTS: Instructor ----- FutronicAttendanceSystem ----- DATABASE ----- Student

START

System Initializes session state
Set currentSessionState = Inactive
Set currentInstructorId = null
Set currentScheduleId = null
Clear signedInStudentGuids
Clear signedOutStudentGuids

DECISION: Always-on attendance enabled?

YES ---- Set currentSessionState = WaitingForInstructor ---- Continue

NO ---- Keep currentSessionState = Inactive ---- Continue

Instructor Scans at outside sensor

System Validates instructor and schedule
Query DATABASE for instructor
Query DATABASE for schedule

DECISION: Validation successful?

NO ---- Display "Invalid instructor or schedule" ---- END

YES ---- Continue

System Creates SESSIONS record
INSERT INTO SESSIONS
SET STATUS = 'active'
SET INSTRUCTORID = instructor_id
SET SCHEDULEID = schedule_id
SET STARTTIME = CURRENT_TIMESTAMP

System Updates session state
Set currentSessionState = ActiveForStudents
Set currentInstructorId = instructor_id
Set currentScheduleId = schedule_id

System Displays "Session Active - Students Can Sign In"

Student Scans at inside sensor

System Checks if student already signed in
Check signedInStudentGuids for student GUID

DECISION: Already signed in?

YES ---- Allow door access only ---- Display "Already signed in - Door access granted" ---- END

NO ---- Continue

System Records attendance
INSERT INTO ATTENDANCERECORDS
SET SCANTYPE = 'time_in'
SET STATUS = 'Present' or 'Late'
SET LOCATION = 'inside'

System Adds student GUID to signedInStudentGuids

System Displays "Attendance Recorded"

Instructor Scans at inside sensor (enable sign-out)

System Updates session state
Set currentSessionState = ActiveForSignOut

System Displays "Session Active - Students Can Sign Out"

Student Scans at inside sensor (sign out)

System Checks if student already signed out
Check signedOutStudentGuids for student GUID

DECISION: Already signed out?

YES ---- Allow door access only ---- Display "Already signed out - Door access granted" ---- END

NO ---- Continue

System Records sign-out
INSERT INTO ATTENDANCERECORDS
SET SCANTYPE = 'time_out'
SET STATUS = 'Present'
SET LOCATION = 'inside'

System Adds student GUID to signedOutStudentGuids

System Displays "Sign-out Recorded"

Instructor Scans at outside sensor (session end)

System Updates SESSIONS
UPDATE SESSIONS SET STATUS = 'ended'
SET ENDTIME = CURRENT_TIMESTAMP
SET DOORLOCKEDAT = CURRENT_TIMESTAMP
WHERE SESSIONID = ?

System Updates ROOMS
UPDATE ROOMS SET DOORSTATUS = 'locked'
WHERE ROOMID = ?

System Updates session state
Set currentSessionState = Inactive
Set currentInstructorId = null
Set currentScheduleId = null

System Clears student tracking
Clear signedInStudentGuids
Clear signedOutStudentGuids

System Displays "Session Ended"

END

SESSION STATE TRANSITIONS:

Inactive:
- Initial state
- No session active
- Waiting for instructor to start session

WaitingForInstructor:
- System boot with always-on attendance enabled
- Waiting for instructor to scan and start session
- Students cannot record attendance

ActiveForStudents:
- Session active
- Students can sign in for attendance
- Instructor can enable sign-out by scanning inside

ActiveForSignOut:
- Session in sign-out phase
- Students can sign out
- Students can still sign in if not already signed in

Inactive (Session End):
- Session ended
- All student tracking cleared
- Waiting for next session to start

================================================================================
FIGURE 14. ACTIVITY DIAGRAM FOR WEB ROOM MANAGEMENT
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

START

Administrator Navigates to Rooms page
(URL: /rooms)

Frontend Checks authentication token

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests rooms list
GET /api/rooms

Backend API Queries ROOMS table
SELECT * FROM ROOMS WHERE ARCHIVED_AT IS NULL

Backend API Returns rooms data
{ rooms: [...], pagination: {...} }

Frontend Receives rooms list

Frontend Displays rooms table
Show: Room Number, Room Name, Building, Capacity, Door Status, Actions

DECISION: Administrator action?

ADD ROOM:
- Administrator clicks "Add Room" button
- Frontend displays room form modal
- Administrator fills form (Room Number, Room Name, Building, Capacity, Room Type)
- Administrator submits form
- Frontend validates form data
- DECISION: Form valid?
  - NO: Display validation errors ---- Loop back to form
  - YES: Send POST /api/rooms request
    - Backend validates room data
    - Backend checks for duplicate room number
    - DECISION: Duplicate found?
      - YES: Return error "Room number already exists" ---- END
      - NO: Insert new room into ROOMS table
        - INSERT INTO ROOMS (ROOMID, ROOMNUMBER, ROOMNAME, BUILDING, CAPACITY, ROOMTYPE, DOORSTATUS)
        - Return success response
    - Frontend refreshes rooms list
    - Frontend displays success message
    - END

EDIT ROOM:
- Administrator clicks "Edit Room" button
- Frontend displays edit form with room data
- Administrator modifies room data
- Administrator submits form
- Frontend validates form data
- Send PUT /api/rooms/:id request
- Backend validates room data
- Backend updates room in ROOMS table
- UPDATE ROOMS SET ... WHERE ROOMID = ?
- Backend returns success response
- Frontend refreshes rooms list
- Frontend displays success message
- END

DELETE ROOM:
- Administrator clicks "Delete Room" button
- Frontend displays delete confirmation modal
- Administrator confirms deletion
- Send DELETE /api/rooms/:id request
- Backend checks for related schedules
- DECISION: Related schedules found?
  - YES: Return error "Cannot delete room with active schedules" ---- END
  - NO: Delete room from ROOMS table
    - DELETE FROM ROOMS WHERE ROOMID = ?
    - (Soft delete: SET ARCHIVED_AT = CURRENT_TIMESTAMP)
- Backend returns success response
- Frontend refreshes rooms list
- Frontend displays success message
- END

END

================================================================================
FIGURE 15. ACTIVITY DIAGRAM FOR WEB SUBJECT MANAGEMENT
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

START

Administrator Navigates to Subjects page
(URL: /subjects)

Frontend Checks authentication token

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests subjects list and instructors list
GET /api/subjects
GET /api/subjects/instructors/list

Backend API Queries SUBJECTS table
SELECT * FROM SUBJECTS WHERE ARCHIVED_AT IS NULL

Backend API Queries USERS table for instructors
SELECT * FROM USERS WHERE USERTYPE = 'instructor' AND STATUS = 'Active'

Backend API Returns subjects and instructors data
{ subjects: [...], instructors: [...] }

Frontend Receives subjects and instructors lists

Frontend Displays subjects table
Show: Subject Code, Subject Name, Instructor, Semester, Academic Year, Actions

DECISION: Administrator action?

ADD SUBJECT:
- Administrator clicks "Add Subject" button
- Frontend displays subject form modal
- Administrator fills form (Subject Code, Subject Name, Description, Instructor, Semester, Academic Year)
- Administrator submits form
- Frontend validates form data
- DECISION: Form valid?
  - NO: Display validation errors ---- Loop back to form
  - YES: Send POST /api/subjects request
    - Backend validates subject data
    - Backend checks for duplicate subject code
    - DECISION: Duplicate found?
      - YES: Return error "Subject code already exists" ---- END
      - NO: Insert new subject into SUBJECTS table
        - INSERT INTO SUBJECTS (SUBJECTID, SUBJECTCODE, SUBJECTNAME, DESCRIPTION, INSTRUCTORID, SEMESTER, ACADEMICYEAR)
        - Return success response
    - Frontend refreshes subjects list
    - Frontend displays success message
    - END

EDIT SUBJECT:
- Administrator clicks "Edit Subject" button
- Frontend displays edit form with subject data
- Administrator modifies subject data
- Administrator submits form
- Frontend validates form data
- Send PUT /api/subjects/:id request
- Backend validates subject data
- Backend updates subject in SUBJECTS table
- UPDATE SUBJECTS SET ... WHERE SUBJECTID = ?
- Backend returns success response
- Frontend refreshes subjects list
- Frontend displays success message
- END

ENROLL STUDENTS:
- Administrator clicks "Enroll Students" button on subject
- Frontend displays enrollment modal
- Frontend requests available students
- GET /api/users?type=student
- Backend returns available students list
- Frontend displays available students
- Administrator selects students to enroll
- Administrator confirms enrollment
- Frontend sends POST /api/enrollment/enroll request
- Body: { subject_id, student_ids: [...] }
- Backend inserts enrollments into SUBJECTENROLLMENT table
- INSERT INTO SUBJECTENROLLMENT (ENROLLMENTID, USERID, SUBJECTID, STATUS, ACADEMICYEAR, SEMESTER)
- Backend returns success response
- Frontend refreshes subjects list
- Frontend displays success message
- END

DELETE SUBJECT:
- Administrator clicks "Delete Subject" button
- Frontend displays delete confirmation modal
- Administrator confirms deletion
- Send DELETE /api/subjects/:id request
- Backend checks for related schedules and enrollments
- DECISION: Related records found?
  - YES: Return error "Cannot delete subject with active schedules or enrollments" ---- END
  - NO: Delete subject from SUBJECTS table
    - DELETE FROM SUBJECTS WHERE SUBJECTID = ?
    - (Soft delete: SET ARCHIVED_AT = CURRENT_TIMESTAMP)
- Backend returns success response
- Frontend refreshes subjects list
- Frontend displays success message
- END

END

================================================================================
FIGURE 16. ACTIVITY DIAGRAM FOR WEB SCHEDULE MANAGEMENT
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE

START

Administrator Navigates to Schedules page
(URL: /schedules)

Frontend Checks authentication token

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests schedules and related data
GET /api/schedules
GET /api/subjects
GET /api/rooms
GET /api/users?type=instructor

Backend API Queries CLASSSCHEDULES table
SELECT * FROM CLASSSCHEDULES WHERE ARCHIVED_AT IS NULL

Backend API Queries SUBJECTS table
SELECT * FROM SUBJECTS WHERE ARCHIVED_AT IS NULL

Backend API Queries ROOMS table
SELECT * FROM ROOMS WHERE ARCHIVED_AT IS NULL

Backend API Queries USERS table for instructors
SELECT * FROM USERS WHERE USERTYPE = 'instructor'

Backend API Returns consolidated data
{ schedules: [...], subjects: [...], rooms: [...], instructors: [...] }

Frontend Receives schedules and related data

Frontend Displays schedules table
Show: Subject, Room, Day, Start Time, End Time, Instructor, Actions

DECISION: Administrator action?

ADD SCHEDULE:
- Administrator clicks "Add Schedule" button
- Frontend displays schedule form modal
- Administrator selects subject, room, day of week
- Administrator sets start time and end time
- Administrator selects academic year and semester
- Administrator submits schedule
- Frontend validates schedule data
- DECISION: Form valid?
  - NO: Display validation errors ---- Loop back to form
  - YES: Send POST /api/schedules request
    - Backend validates schedule data
    - Backend checks for schedule conflicts
    - Query CLASSSCHEDULES
    - WHERE ROOMID = ? AND DAYOFWEEK = ?
    - AND (time overlap check)
    - DECISION: Conflict found?
      - YES: Return error "Schedule conflict detected" ---- END
      - NO: Insert new schedule into CLASSSCHEDULES table
        - INSERT INTO CLASSSCHEDULES (SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME, ACADEMICYEAR, SEMESTER)
        - Return success response
    - Frontend refreshes schedules list
    - Frontend displays success message
    - END

EDIT SCHEDULE:
- Administrator clicks "Edit Schedule" button
- Frontend displays edit form with schedule data
- Administrator modifies schedule data
- Administrator submits form
- Frontend validates schedule data
- Send PUT /api/schedules/:id request
- Backend validates schedule data
- Backend checks for schedule conflicts (excluding current schedule)
- DECISION: Conflict found?
  - YES: Return error "Schedule conflict detected" ---- END
  - NO: Update schedule in CLASSSCHEDULES table
    - UPDATE CLASSSCHEDULES SET ... WHERE SCHEDULEID = ?
    - Backend returns success response
- Frontend refreshes schedules list
- Frontend displays success message
- END

DELETE SCHEDULE:
- Administrator clicks "Delete Schedule" button
- Frontend displays delete confirmation modal
- Administrator confirms deletion
- Send DELETE /api/schedules/:id request
- Backend checks for active sessions
- DECISION: Active session found?
  - YES: Return error "Cannot delete schedule with active session" ---- END
  - NO: Delete schedule from CLASSSCHEDULES table
    - DELETE FROM CLASSSCHEDULES WHERE SCHEDULEID = ?
    - (Soft delete: SET ARCHIVED_AT = CURRENT_TIMESTAMP)
- Backend returns success response
- Frontend refreshes schedules list
- Frontend displays success message
- END

END

SCHEDULE CONFLICT DETECTION:
- Room conflict: Check if room is already scheduled at same time and day
- Instructor conflict: Check if instructor has another class at same time
- Time overlap: Check if schedule times overlap with existing schedules

================================================================================
FIGURE 17. ACTIVITY DIAGRAM FOR WEB BACKUP OPERATIONS
================================================================================

PARTICIPANTS: Administrator ----- Frontend ----- Backend API ----- DATABASE ----- Backend Service ----- File System

START

Administrator Navigates to Backup page
(URL: /backup)

Frontend Checks authentication token

DECISION: Token valid?

NO ---- Redirect to login page ---- END

YES ---- Continue

Frontend Requests backup statistics
GET /api/backup/stats

Backend API Queries backup file information
List files in backups directory
Get file sizes and creation dates

Backend API Returns backup statistics
{ total_backups: count, total_size: size, last_backup: date }

Frontend Receives backup statistics

Frontend Displays backup dashboard
Show: Backup statistics, Backup options, Existing backups list

Administrator Selects backup options
Select: Include database, Include files, Include config, Database format

Administrator Clicks "Create Backup" button

Frontend Sends create backup request
POST /api/backup/create
Body: { includeDatabase, includeFiles, includeConfig, dbFormat }

Backend API Validates backup request

Backend API Queries DATABASE for all data
Export all tables to SQL dump
SELECT * FROM USERS
SELECT * FROM SUBJECTS
SELECT * FROM ROOMS
... (all tables)

Backend API Generates backup file
Call backup service to create backup file

Backend Service Creates ZIP file
Include: SQL dump, files, config files
Generate backup file name with timestamp
Format: backup_YYYY-MM-DD_HH-MM-SS.zip

Backend Service Saves backup file to File System
Store in backups directory

Backend Service Returns backup file path

Backend API Returns backup success response
{ backup_file: file_path, backup_size: size, backup_date: date }

Frontend Receives backup success response

Frontend Refreshes backup list
Request updated backup list from backend

Frontend Displays success message
Show backup file name and size

DECISION: Administrator action?

DOWNLOAD BACKUP:
- Administrator clicks "Download Backup" button
- Frontend requests backup file download
- GET /api/backup/download/:filename
- Backend reads backup file from File System
- Backend returns backup file as download
- Frontend triggers browser download
- Backup file downloads to administrator's computer
- END

RESTORE BACKUP:
- Administrator clicks "Restore Backup" button
- Frontend displays restore confirmation modal
- Administrator confirms restore operation
- Frontend sends POST /api/backup/restore request
- Body: { backup_file: filename }
- Backend reads backup file from File System
- Backend validates backup file
- DECISION: Backup file valid?
  - NO: Return error "Invalid backup file" ---- END
  - YES: Restore database from backup file
    - Extract SQL dump from ZIP file
    - Execute SQL dump to restore database
    - Return success response
- Frontend displays success message
- END

DELETE BACKUP:
- Administrator clicks "Delete Backup" button
- Frontend displays delete confirmation modal
- Administrator confirms deletion
- Frontend sends DELETE /api/backup/:filename request
- Backend deletes backup file from File System
- Backend returns success response
- Frontend refreshes backup list
- Frontend displays success message
- END

END

BACKUP OPTIONS:

Include Database:
- Export all database tables to SQL dump
- Include schema and data
- Format: SQL or JSON

Include Files:
- Include uploaded files and attachments
- Include user documents
- Include system files

Include Config:
- Include configuration files
- Include environment variables
- Include system settings

Database Format:
- SQL format: Standard SQL dump
- JSON format: JSON export
- Both: SQL and JSON formats

================================================================================
END OF ACTIVITY DIAGRAMS DOCUMENTATION
================================================================================

