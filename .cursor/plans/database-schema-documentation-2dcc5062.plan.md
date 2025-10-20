<!-- 2dcc5062-ae13-4344-a7ac-3729543bdeca b652a0a1-9db1-4e46-9641-8fb36649d68a -->
# Complete ERD Generation Prompt - Fully Self-Contained

## Overview

Create `database/ERD_GENERATION_PROMPT.md` - a single file containing EVERYTHING a 3rd party AI needs to generate an ERD. No external file dependencies.

## What Goes in the Prompt File

### Section 1: Instructions for the AI

```
Task: Generate a complete Entity Relationship Diagram (ERD) for this database.
Database: iot_attendance (IoT Attendance System, Liceo de Cagayan University)
Total: 12 tables, 143 attributes, 15 foreign key relationships
Notation: Use Crow's foot notation
```

### Section 2: COMPLETE Table Definitions (All 143 attributes across 12 tables)

**ENTITY 1: USERS**

```
┌─────────────────────────────────────────────────────────────────┐
│                            Users                                │
├─────────────────────────────────────────────────────────────────┤
│ UserID          │ (PK) - CHAR(36)                               │
│ Firstname       │ VARCHAR(50) NOT NULL                          │
│ Lastname        │ VARCHAR(50) NOT NULL                          │
│ UserType        │ ENUM('student','instructor','admin') NOT NULL │
│ YearLevel       │ ENUM('1','2','3','4','5') DEFAULT NULL       │
│ Email           │ VARCHAR(100) UNIQUE NOT NULL                  │
│ Password_Hash   │ VARCHAR(255) DEFAULT NULL                     │
│ PhoneNumber     │ VARCHAR(15) DEFAULT NULL                      │
│ Department      │ VARCHAR(100) DEFAULT NULL                     │
│ StudentID       │ VARCHAR(20) UNIQUE DEFAULT NULL               │
│ FacultyID       │ VARCHAR(20) UNIQUE DEFAULT NULL               │
│ EmployeeID      │ VARCHAR(20) UNIQUE DEFAULT NULL               │
│ RfidTag         │ VARCHAR(100) UNIQUE DEFAULT NULL              │
│ Status          │ ENUM('Active','Inactive') DEFAULT 'Active'    │
│ created_at      │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP           │
│ updated_at      │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP         │
└─────────────────────────────────────────────────────────────────┘
```

**TABLE 2: AUTHENTICATIONMETHODS (9 attributes)**

```
AUTHID: CHAR(36), PRIMARY KEY
USERID: CHAR(36), NOT NULL, FOREIGN KEY → USERS(USERID) ON DELETE CASCADE
METHODTYPE: ENUM('RFID', 'Fingerprint'), NOT NULL
IDENTIFIER: VARCHAR(100), UNIQUE, NOT NULL
RFIDTAGNUMBER: VARCHAR(100), UNIQUE, DEFAULT NULL
FINGERPRINTTEMPLATE: BLOB, DEFAULT NULL
ISACTIVE: BOOLEAN, DEFAULT TRUE
DATEREGISTERED: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
LASTUPDATED: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
STATUS: ENUM('Active', 'Suspended'), DEFAULT 'Active'
```

**TABLE 3: ROOMS (9 attributes)**

```
ROOMID: CHAR(36), PRIMARY KEY
ROOMNUMBER: VARCHAR(20), UNIQUE, NOT NULL
ROOMNAME: VARCHAR(100), DEFAULT NULL
BUILDING: VARCHAR(100), NOT NULL
CAPACITY: INT UNSIGNED, DEFAULT NULL
DEVICEID: VARCHAR(100), DEFAULT NULL
DOORSTATUS: ENUM('locked', 'unlocked'), DEFAULT 'locked'
STATUS: ENUM('Available', 'Maintenance'), DEFAULT 'Available'
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
```

**TABLE 4: SUBJECTS (9 attributes)**

```
SUBJECTID: CHAR(36), PRIMARY KEY
SUBJECTCODE: VARCHAR(20), NOT NULL
SUBJECTNAME: VARCHAR(255), NOT NULL
INSTRUCTORID: CHAR(36), NOT NULL, FOREIGN KEY → USERS(USERID) ON DELETE CASCADE
SEMESTER: VARCHAR(20), NOT NULL
YEAR: YEAR, NOT NULL
ACADEMICYEAR: VARCHAR(10), NOT NULL
DESCRIPTION: TEXT, DEFAULT NULL
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
```

**TABLE 5: CLASSSCHEDULES (10 attributes)**

```
SCHEDULEID: CHAR(36), PRIMARY KEY
SUBJECTID: CHAR(36), NOT NULL, FOREIGN KEY → SUBJECTS(SUBJECTID) ON DELETE CASCADE
ROOMID: CHAR(36), NOT NULL, FOREIGN KEY → ROOMS(ROOMID) ON DELETE CASCADE
DAYOFWEEK: ENUM('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'), NOT NULL
STARTTIME: TIME, NOT NULL
ENDTIME: TIME, NOT NULL
ACADEMICYEAR: VARCHAR(10), NOT NULL
SEMESTER: VARCHAR(20), NOT NULL
ISLAB: TINYINT(1), DEFAULT 0
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
```

**TABLE 6: SUBJECTENROLLMENT (8 attributes)**

```
ENROLLMENTID: CHAR(36), PRIMARY KEY
USERID: CHAR(36), NOT NULL, FOREIGN KEY → USERS(USERID) ON DELETE CASCADE
SUBJECTID: CHAR(36), NOT NULL, FOREIGN KEY → SUBJECTS(SUBJECTID) ON DELETE CASCADE
ENROLLMENTDATE: DATE, DEFAULT (CURRENT_DATE)
ACADEMICYEAR: VARCHAR(10), NOT NULL
SEMESTER: VARCHAR(20), NOT NULL
STATUS: ENUM('enrolled', 'dropped', 'completed'), DEFAULT 'enrolled'
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
```

**TABLE 7: SESSIONS (16 attributes)**

```
SESSIONID: CHAR(36), PRIMARY KEY
SCHEDULEID: CHAR(36), NOT NULL, FOREIGN KEY → CLASSSCHEDULES(SCHEDULEID) ON DELETE CASCADE
INSTRUCTORID: CHAR(36), NOT NULL, FOREIGN KEY → USERS(USERID) ON DELETE CASCADE
ROOMID: CHAR(36), NOT NULL, FOREIGN KEY → ROOMS(ROOMID) ON DELETE CASCADE
SESSIONDATE: DATE, NOT NULL
STARTTIME: TIMESTAMP, DEFAULT NULL
ENDTIME: TIMESTAMP, DEFAULT NULL
ATTENDANCE_WINDOW_MINUTES: INT, DEFAULT 15
ATTENDANCE_DEADLINE: TIMESTAMP, DEFAULT NULL
STATUS: ENUM('waiting', 'active', 'ended', 'cancelled'), DEFAULT 'waiting'
DOORUNLOCKEDAT: TIMESTAMP, DEFAULT NULL
DOORLOCKEDAT: TIMESTAMP, DEFAULT NULL
ACADEMICYEAR: VARCHAR(10), NOT NULL
SEMESTER: VARCHAR(20), NOT NULL
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
```

**TABLE 8: ATTENDANCERECORDS (15 attributes)**

```
ATTENDANCEID: CHAR(36), PRIMARY KEY
USERID: CHAR(36), NOT NULL, FOREIGN KEY → USERS(USERID) ON DELETE CASCADE
SCHEDULEID: CHAR(36), NOT NULL, FOREIGN KEY → CLASSSCHEDULES(SCHEDULEID) ON DELETE CASCADE
SESSIONID: CHAR(36), DEFAULT NULL, FOREIGN KEY → SESSIONS(SESSIONID) ON DELETE SET NULL
SCANTYPE: ENUM('time_in', 'time_out', 'early_arrival', 'time_in_confirmation', 'early_arrival_upgraded', 'early_arrival_expired'), NOT NULL
SCANDATETIME: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
DATE: DATE, NOT NULL
TIMEIN: TIME, DEFAULT NULL
AUTHMETHOD: ENUM('RFID', 'Fingerprint'), NOT NULL
LOCATION: ENUM('inside', 'outside'), NOT NULL
STATUS: ENUM('Present', 'Late', 'Absent', 'Early Arrival', 'Early Scan | Absent'), NOT NULL
ACADEMICYEAR: VARCHAR(10), NOT NULL
SEMESTER: VARCHAR(20), NOT NULL
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
```

**TABLE 9: ACCESSLOGS (12 attributes)**

```
LOGID: CHAR(36), PRIMARY KEY
USERID: CHAR(36), DEFAULT NULL, FOREIGN KEY → USERS(USERID) ON DELETE SET NULL
ROOMID: CHAR(36), NOT NULL, FOREIGN KEY → ROOMS(ROOMID) ON DELETE CASCADE
TIMESTAMP: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
ACCESSTYPE: ENUM('session_start', 'session_end', 'attendance_scan', 'door_override'), NOT NULL
AUTHMETHOD: ENUM('RFID', 'Fingerprint', 'Manual'), NOT NULL
LOCATION: ENUM('inside', 'outside'), NOT NULL
RESULT: ENUM('success', 'denied'), NOT NULL
REASON: VARCHAR(255), DEFAULT NULL
IPADDRESS: VARCHAR(45), DEFAULT NULL
USERAGENT: TEXT, DEFAULT NULL
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
```

**TABLE 10: DEVICES (12 attributes)**

```
DEVICEID: CHAR(36), PRIMARY KEY
DEVICETYPE: ENUM('RFID_Reader', 'Fingerprint_Scanner', 'Door_Controller'), NOT NULL
DEVICENAME: VARCHAR(100), NOT NULL
LOCATION: VARCHAR(255), NOT NULL
ROOMID: CHAR(36), NOT NULL, FOREIGN KEY → ROOMS(ROOMID) ON DELETE CASCADE
IPADDRESS: VARCHAR(45), DEFAULT NULL
MACADDRESS: VARCHAR(17), DEFAULT NULL
FIRMWAREVERSION: VARCHAR(50), DEFAULT NULL
LASTMAINTENANCE: DATE, DEFAULT NULL
LASTSEEN: TIMESTAMP, DEFAULT NULL
STATUS: ENUM('Active', 'Inactive', 'Maintenance'), DEFAULT 'Active'
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
```

**TABLE 11: SETTINGS (8 attributes)**

```
SETTINGID: CHAR(36), PRIMARY KEY
SETTINGKEY: VARCHAR(100), UNIQUE, NOT NULL
SETTINGVALUE: TEXT, NOT NULL
DESCRIPTION: TEXT, DEFAULT NULL
CATEGORY: VARCHAR(50), DEFAULT 'general'
ISEDITABLE: BOOLEAN, DEFAULT TRUE
CREATED_AT: TIMESTAMP, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: TIMESTAMP, ON UPDATE CURRENT_TIMESTAMP
```

**TABLE 12: IMPORT_LOGS (8 attributes)**

```
ID: VARCHAR(36), PRIMARY KEY
ACADEMIC_YEAR: VARCHAR(10), NOT NULL
SEMESTER: VARCHAR(20), NOT NULL
IMPORT_DATE: DATETIME, NOT NULL
SUMMARY: TEXT, DEFAULT NULL
STATUS: VARCHAR(20), DEFAULT 'completed'
CREATED_AT: DATETIME, DEFAULT CURRENT_TIMESTAMP
UPDATED_AT: DATETIME, ON UPDATE CURRENT_TIMESTAMP
```

### Section 3: Complete Relationship List (All 15 relationships with cardinality)

```
1. AUTHENTICATIONMETHODS → USERS (Many-to-One)
   AUTHENTICATIONMETHODS.USERID → USERS.USERID, ON DELETE CASCADE
   Meaning: One user can have multiple authentication methods

2. SUBJECTS → USERS (Many-to-One)
   SUBJECTS.INSTRUCTORID → USERS.USERID, ON DELETE CASCADE
   Meaning: One instructor can teach multiple subjects

3. CLASSSCHEDULES → SUBJECTS (Many-to-One)
   CLASSSCHEDULES.SUBJECTID → SUBJECTS.SUBJECTID, ON DELETE CASCADE
   Meaning: One subject can have multiple schedule entries

4. CLASSSCHEDULES → ROOMS (Many-to-One)
   CLASSSCHEDULES.ROOMID → ROOMS.ROOMID, ON DELETE CASCADE
   Meaning: One room can host multiple scheduled classes

5. SUBJECTENROLLMENT → USERS (Many-to-One)
   SUBJECTENROLLMENT.USERID → USERS.USERID, ON DELETE CASCADE
   Meaning: One user (student) can enroll in multiple subjects

6. SUBJECTENROLLMENT → SUBJECTS (Many-to-One)
   SUBJECTENROLLMENT.SUBJECTID → SUBJECTS.SUBJECTID, ON DELETE CASCADE
   Meaning: One subject can have multiple enrolled students

7. SESSIONS → CLASSSCHEDULES (Many-to-One)
   SESSIONS.SCHEDULEID → CLASSSCHEDULES.SCHEDULEID, ON DELETE CASCADE
   Meaning: One schedule entry can have multiple session instances

8. SESSIONS → USERS (Many-to-One)
   SESSIONS.INSTRUCTORID → USERS.USERID, ON DELETE CASCADE
   Meaning: One instructor can manage multiple sessions

9. SESSIONS → ROOMS (Many-to-One)
   SESSIONS.ROOMID → ROOMS.ROOMID, ON DELETE CASCADE
   Meaning: One room can host multiple sessions

10. ATTENDANCERECORDS → USERS (Many-to-One)
    ATTENDANCERECORDS.USERID → USERS.USERID, ON DELETE CASCADE
    Meaning: One user can have multiple attendance records

11. ATTENDANCERECORDS → CLASSSCHEDULES (Many-to-One)
    ATTENDANCERECORDS.SCHEDULEID → CLASSSCHEDULES.SCHEDULEID, ON DELETE CASCADE
    Meaning: One schedule can have multiple attendance records

12. ATTENDANCERECORDS → SESSIONS (Many-to-One)
    ATTENDANCERECORDS.SESSIONID → SESSIONS.SESSIONID, ON DELETE SET NULL
    Meaning: One session can have multiple attendance records

13. ACCESSLOGS → USERS (Many-to-One)
    ACCESSLOGS.USERID → USERS.USERID, ON DELETE SET NULL
    Meaning: One user can have multiple access log entries

14. ACCESSLOGS → ROOMS (Many-to-One)
    ACCESSLOGS.ROOMID → ROOMS.ROOMID, ON DELETE CASCADE
    Meaning: One room can have multiple access log entries

15. DEVICES → ROOMS (Many-to-One)
    DEVICES.ROOMID → ROOMS.ROOMID, ON DELETE CASCADE
    Meaning: One room can have multiple devices
```

### Section 4: ERD Requirements

```
- Use Crow's foot notation for all relationships
- Mark PRIMARY KEYS with (PK) symbol
- Mark FOREIGN KEYS with (FK) symbol  
- Show cardinality: one (single line) to many (crow's foot)
- Include ALL attributes inside each entity box
- Draw relationship lines between connected tables
- Label each relationship line with its meaning
```

## File to Create

- `database/ERD_GENERATION_PROMPT.md` - Complete prompt with all 143 attributes embedded

## Implementation

Write the markdown file with all the content above formatted clearly for copy-paste to any AI tool.

### To-dos

- [ ] Analyze complete database structure including base schema and dynamically added columns
- [ ] Count attributes, primary keys, and foreign keys for all 13 tables
- [ ] Create formatted summary table showing entity overview
- [ ] Create detailed specifications for all 13 tables with columns, types, and descriptions
- [ ] Add database metadata, institution info, and formatting