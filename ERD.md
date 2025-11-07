1. Task Overview

- **Database name and system name**: IoT Attendance System (IOTCapstone)
- **Institution/organization**: TBD
- **Total entities (table count)**: 14
- **Total attributes (column count across all tables)**: 172
- **Total relationships (foreign key count)**: 25
- **Notation preference**: Crow's Foot

2. Database Summary Table
Table 1.

| ENTITY NAME | NO. OF ATTRIBUTES | PRIMARY KEY | NO. OF FOREIGN KEYS |
|---|---:|---|:---:|
| accesslogs | 12 | LOGID | ✓ |
| attendancerecords | 19 | ATTENDANCEID | ✓ |
| authenticationmethods | 10 | AUTHID | ✓ |
| classschedules | 14 | SCHEDULEID | ✓ |
| courses | 10 | COURSEID | ✓ |
| devices | 13 | DEVICEID | ✓ |
| import_logs | 6 | ID |  |
| rooms | 13 | ROOMID | ✓ |
| sessions | 17 | SESSIONID | ✓ |
| settings | 8 | SETTINGID |  |
| subjectenrollment | 11 | ENROLLMENTID | ✓ |
| subjects | 13 | SUBJECTID | ✓ |
| users | 18 | USERID | ✓ |

3. Complete Entity Definitions

accesslogs (12 attributes)

│ ACCESSLOGS │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ LOGID                 │ (PK) CHAR(36) NOT NULL                                               │
│ USERID                │ (FK) CHAR(36) NULL → USERS.USERID                                    │
│ ROOMID                │ (FK) CHAR(36) NOT NULL → ROOMS.ROOMID                                │
│ TIMESTAMP             │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                   │
│ AccessType            │ ENUM('session_start','session_end','attendance_scan','door_override') NOT NULL │
│ AuthMethod            │ ENUM('RFID','Fingerprint','Manual') NOT NULL                         │
│ Location              │ ENUM('inside','outside') NOT NULL                                    │
│ Result                │ ENUM('success','denied') NOT NULL                                    │
│ Reason                │ VARCHAR(255) DEFAULT NULL                                            │
│ IpAddress             │ VARCHAR(45) DEFAULT NULL                                             │
│ UserAgent             │ TEXT DEFAULT NULL                                                    │
│ Created_At            │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                   │
└──────────────────────────────────────────────────────────────────────────────────────────────┘

Purpose: Captures all door and session-related access events, including method, location, outcome, and client metadata.

attendancerecords (19 attributes)

│ ATTENDANCERECORDS │
├──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ AttendanceID          │ (PK) CHAR(36) NOT NULL                                                                                 │
│ UserID                │ (FK) CHAR(36) NOT NULL → USERS.UserID                                                                  │
│ ScheduleID            │ (FK) CHAR(36) NOT NULL → CLASSSCHEDULES.ScheduleID                                                     │
│ SessionID             │ (FK) CHAR(36) DEFAULT NULL → SESSIONS.SessionID                                                        │
│ ScanType              │ ENUM('time_in','time_out','early_arrival','time_in_confirmation','early_arrival_upgraded','early_arrival_expired') NOT NULL │
│ ScanDateTime          │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                                                     │
│ Date                  │ DATE NOT NULL                                                                                          │
│ TimeIn                │ TIME DEFAULT NULL                                                                                       │
│ AuthMethod            │ ENUM('RFID','Fingerprint') NOT NULL                                                                     │
│ Location              │ ENUM('inside','outside') NOT NULL                                                                       │
│ Status                │ ENUM('Present','Late','Absent','Early Arrival','Early Scan | Absent') NOT NULL                          │
│ ActionType            │ VARCHAR(50) DEFAULT NULL                                                                                │
│ AcademicYear          │ VARCHAR(10) NOT NULL                                                                                    │
│ Semester              │ VARCHAR(20) NOT NULL                                                                                    │
│ Created_At            │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                                                     │
│ Updated_At            │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                                                                   │
│ Archived_At           │ TIMESTAMP DEFAULT NULL                                                                                  │
│ Archived_By           │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID                                                               │
│ Archive_Reason        │ TEXT DEFAULT NULL                                                                                       │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘

Purpose: Stores each attendance scan with timing, method, computed status, and archival metadata.

authenticationmethods (10 attributes)

│ AUTHENTICATIONMETHODS │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ AuthID                │ (PK) CHAR(36) NOT NULL                                              │
│ UserID                │ (FK) CHAR(36) NOT NULL → USERS.UserID                               │
│ MethodType            │ ENUM('RFID','Fingerprint') NOT NULL                                 │
│ Identifier            │ VARCHAR(100) UNIQUE NOT NULL                                        │
│ RfidTagNumber         │ VARCHAR(100) UNIQUE DEFAULT NULL                                     │
│ FingerprintTemplate   │ BLOB DEFAULT NULL                                                   │
│ IsActive              │ BOOLEAN DEFAULT TRUE                                                │
│ DateRegistered        │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                  │
│ LastUpdated           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                                │
│ Status                │ ENUM('Active','Suspended') DEFAULT 'Active'                         │
└──────────────────────────────────────────────────────────────────────────────────────────────┘

Purpose: Defines user authentication factors and their activation status.

classschedules (14 attributes)

│ CLASSSCHEDULES │
├──────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ScheduleID            │ (PK) CHAR(36) NOT NULL                                                        │
│ SubjectID             │ (FK) CHAR(36) NOT NULL → SUBJECTS.SubjectID                                   │
│ RoomID                │ (FK) CHAR(36) NOT NULL → ROOMS.RoomID                                         │
│ DayOfWeek             │ ENUM('Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday') NOT NULL │
│ StartTime             │ TIME NOT NULL                                                                  │
│ EndTime               │ TIME NOT NULL                                                                  │
│ AcademicYear          │ VARCHAR(10) NOT NULL                                                           │
│ Semester              │ VARCHAR(20) NOT NULL                                                           │
│ Created_At            │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                            │
│ Updated_At            │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                                          │
│ IsLab                 │ BOOLEAN DEFAULT FALSE                                                          │
│ Archived_At           │ TIMESTAMP DEFAULT NULL                                                         │
│ Archived_By           │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID                                      │
│ Archive_Reason        │ TEXT DEFAULT NULL                                                              │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘

Purpose: Recurring schedule templates that sessions instantiate on specific dates.



courses (10 attributes)

│ COURSES │
├──────────────────────────────────────────────────────────────────────────────┤
│ CourseID             │ (PK) CHAR(36) NOT NULL                               │
│ CourseCode           │ VARCHAR(20) NOT NULL                                 │
│ CourseName           │ VARCHAR(255) NOT NULL                                │
│ InstructorID         │ (FK) CHAR(36) NOT NULL → USERS.UserID                │
│ Semester             │ VARCHAR(20) NOT NULL                                 │
│ Year                 │ YEAR NOT NULL                                        │
│ AcademicYear         │ VARCHAR(10) NOT NULL                                 │
│ Description          │ TEXT DEFAULT NULL                                    │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                   │
│ Updated_At           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                 │
└──────────────────────────────────────────────────────────────────────────────┘

Purpose: Course catalog entries linked to lead instructors.

devices (13 attributes)

│ DEVICES │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ DeviceID             │ (PK) CHAR(36) NOT NULL                                               │
│ DeviceType           │ ENUM('RFID_Reader','Fingerprint_Scanner','Door_Controller') NOT NULL │
│ DeviceName           │ VARCHAR(100) NOT NULL                                                │
│ Location             │ VARCHAR(255) NOT NULL                                                │
│ RoomID               │ (FK) CHAR(36) NOT NULL → ROOMS.RoomID                                │
│ IpAddress            │ VARCHAR(45) DEFAULT NULL                                             │
│ MacAddress           │ VARCHAR(17) DEFAULT NULL                                             │
│ FirmwareVersion      │ VARCHAR(50) DEFAULT NULL                                             │
│ LastMaintenance      │ DATE DEFAULT NULL                                                    │
│ LastSeen             │ TIMESTAMP DEFAULT NULL                                               │
│ Status               │ ENUM('Active','Inactive','Maintenance') DEFAULT 'Active'             │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                   │
│ Updated_At           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                                 │
└──────────────────────────────────────────────────────────────────────────────────────────────┘

Purpose: Hardware registry for IoT devices bound to rooms.

import_logs (6 attributes)

│ IMPORT_LOGS │
├──────────────────────────────────────────────────────────────┤
│ ID                   │ (PK) VARCHAR(36) NOT NULL            │
│ Academic_Year        │ VARCHAR(20) DEFAULT NULL             │
│ Semester             │ VARCHAR(50) DEFAULT NULL             │
│ Import_Date          │ DATETIME DEFAULT NULL                │
│ Summary              │ JSON DEFAULT NULL                    │
│ Status               │ VARCHAR(20) DEFAULT NULL             │
└──────────────────────────────────────────────────────────────┘

Purpose: Audits data import operations and results.

rooms (13 attributes)

│ ROOMS │
├──────────────────────────────────────────────────────────────────────────────┤
│ RoomID               │ (PK) CHAR(36) NOT NULL                               │
│ RoomNumber           │ VARCHAR(20) UNIQUE NOT NULL                           │
│ RoomName             │ VARCHAR(100) DEFAULT NULL                             │
│ Building             │ VARCHAR(100) NOT NULL                                 │
│ Capacity             │ INT UNSIGNED DEFAULT NULL                              │
│ DeviceID             │ VARCHAR(100) DEFAULT NULL                              │
│ DoorStatus           │ ENUM('locked','unlocked') DEFAULT 'locked'             │
│ Status               │ ENUM('Available','Maintenance') DEFAULT 'Available'    │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                     │
│ Updated_At           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                   │
│ Archived_At          │ TIMESTAMP DEFAULT NULL                                  │
│ Archived_By          │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID              │
│ Archive_Reason       │ TEXT DEFAULT NULL                                       │
└──────────────────────────────────────────────────────────────────────────────┘

Purpose: Defines physical rooms, capacity, and operational/archival status.

sessions (17 attributes)

│ SESSIONS │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ SessionID            │ (PK) CHAR(36) NOT NULL                                               │
│ ScheduleID           │ (FK) CHAR(36) NOT NULL → CLASSSCHEDULES.ScheduleID                   │
│ InstructorID         │ (FK) CHAR(36) NOT NULL → USERS.UserID                                │
│ RoomID               │ (FK) CHAR(36) NOT NULL → ROOMS.RoomID                                │
│ SessionDate          │ DATE NOT NULL                                                        │
│ StartTime            │ TIMESTAMP DEFAULT NULL                                               │
│ EndTime              │ TIMESTAMP DEFAULT NULL                                               │
│ Status               │ ENUM('active','ended','cancelled') DEFAULT 'active'                  │
│ DoorUnlockedAt       │ TIMESTAMP DEFAULT NULL                                               │
│ DoorLockedAt         │ TIMESTAMP DEFAULT NULL                                               │
│ AcademicYear         │ VARCHAR(10) NOT NULL                                                 │
│ Semester             │ VARCHAR(20) NOT NULL                                                 │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                                   │
│ Updated_At           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                                 │
│ Archived_At          │ TIMESTAMP DEFAULT NULL                                                │
│ Archived_By          │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID                            │
│ Archive_Reason       │ TEXT DEFAULT NULL                                                    │
└──────────────────────────────────────────────────────────────────────────────────────────────┘

Purpose: Realized class occurrences with timing, room, and door state marks.

settings (8 attributes)

│ SETTINGS │
├──────────────────────────────────────────────────────────────────────────────┤
│ SettingID            │ (PK) CHAR(36) NOT NULL                               │
│ SettingKey           │ VARCHAR(100) UNIQUE NOT NULL                          │
│ SettingValue         │ TEXT NOT NULL                                         │
│ Description          │ TEXT DEFAULT NULL                                     │
│ Category             │ VARCHAR(50) DEFAULT 'general'                         │
│ IsEditable           │ BOOLEAN DEFAULT TRUE                                  │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                    │
│ Updated_At           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                  │
└──────────────────────────────────────────────────────────────────────────────┘

Purpose: Editable configuration entries grouped by category.

subjectenrollment (11 attributes)

│ SUBJECTENROLLMENT │
├──────────────────────────────────────────────────────────────────────────────┤
│ EnrollmentID         │ (PK) CHAR(36) NOT NULL                               │
│ UserID               │ (FK) CHAR(36) NOT NULL → USERS.UserID                │
│ SubjectID            │ (FK) CHAR(36) NOT NULL → SUBJECTS.SubjectID          │
│ EnrollmentDate       │ DATE DEFAULT curdate()                               │
│ AcademicYear         │ VARCHAR(10) NOT NULL                                 │
│ Semester             │ VARCHAR(20) NOT NULL                                 │
│ Status               │ ENUM('enrolled','dropped','completed') DEFAULT 'enrolled' │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                   │
│ Archived_At          │ TIMESTAMP DEFAULT NULL                                │
│ Archived_By          │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID            │
│ Archive_Reason       │ TEXT DEFAULT NULL                                     │
└──────────────────────────────────────────────────────────────────────────────┘

Purpose: Tracks user enrollment at the subject level by term.

subjects (13 attributes)

│ SUBJECTS │
├──────────────────────────────────────────────────────────────────────────────┤
│ SubjectID            │ (PK) CHAR(36) NOT NULL                               │
│ SubjectCode          │ VARCHAR(20) NOT NULL                                  │
│ SubjectName          │ VARCHAR(255) NOT NULL                                 │
│ InstructorID         │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID             │
│ Semester             │ VARCHAR(20) NOT NULL                                  │
│ Year                 │ INT NOT NULL                                          │
│ AcademicYear         │ VARCHAR(10) NOT NULL                                  │
│ Description          │ VARCHAR(255) DEFAULT NULL                              │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                    │
│ Updated_At           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                  │
│ Archived_At          │ TIMESTAMP DEFAULT NULL                                 │
│ Archived_By          │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID             │
│ Archive_Reason       │ TEXT DEFAULT NULL                                      │
└──────────────────────────────────────────────────────────────────────────────┘

Purpose: Defines subjects and assigns optional instructor.

users (18 attributes)

│ USERS │
├──────────────────────────────────────────────────────────────────────────────┤
│ UserID               │ (PK) CHAR(36) NOT NULL                               │
│ FirstName            │ VARCHAR(50) NOT NULL                                  │
│ LastName             │ VARCHAR(50) NOT NULL                                   │
│ UserType             │ ENUM('student','instructor','admin','custodian','dean','superadmin') NOT NULL │
│ YearLevel            │ ENUM('1','2','3','4','5') DEFAULT NULL                │
│ Email                │ VARCHAR(100) DEFAULT NULL                              │
│ Password_Hash        │ VARCHAR(255) DEFAULT NULL                              │
│ PhoneNumber          │ VARCHAR(15) DEFAULT NULL                               │
│ Department           │ VARCHAR(100) DEFAULT NULL                              │
│ StudentID            │ VARCHAR(20) UNIQUE DEFAULT NULL                        │
│ FacultyID            │ VARCHAR(20) UNIQUE DEFAULT NULL                        │
│ Status               │ ENUM('Active','Inactive') DEFAULT 'Active'             │
│ Created_At           │ TIMESTAMP DEFAULT CURRENT_TIMESTAMP                    │
│ Updated_At           │ TIMESTAMP ON UPDATE CURRENT_TIMESTAMP                  │
│ RfidTag              │ VARCHAR(50) DEFAULT NULL                               │
│ Archived_At          │ TIMESTAMP DEFAULT NULL                                 │
│ Archived_By          │ (FK) CHAR(36) DEFAULT NULL → USERS.UserID              │
│ Archive_Reason       │ TEXT DEFAULT NULL                                      │
└──────────────────────────────────────────────────────────────────────────────┘

Purpose: Central user directory for all roles; includes optional archival owner.

4. Complete Relationship Specifications

Notes: Delete rules not specified in schema; recommend RESTRICT unless otherwise noted. Cardinality symbols: ●< = many, | = one, O| = zero-or-one.

1) User Access Logs
- Type: Many-to-One
- Mapping: accesslogs.USERID → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< accesslogs to | users
- Meaning: Many access log events belong to a single user.

2) Access Log Room
- Type: Many-to-One
- Mapping: accesslogs.ROOMID → rooms.ROOMID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< accesslogs to | rooms
- Meaning: Many access events occur for a single room.

3) Attendance User
- Type: Many-to-One
- Mapping: attendancerecords.USERID → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< attendancerecords to | users
- Meaning: Each attendance scan belongs to a user.

4) Attendance Schedule
- Type: Many-to-One
- Mapping: attendancerecords.SCHEDULEID → classschedules.SCHEDULEID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< attendancerecords to | classschedules
- Meaning: Attendance scan aligns to a recurring schedule.

5) Attendance Session
- Type: Many-to-One (optional)
- Mapping: attendancerecords.SESSIONID → sessions.SESSIONID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< attendancerecords to | sessions
- Meaning: Attendance scan may reference a concrete session instance.

6) Attendance Archived By
- Type: Many-to-One (optional)
- Mapping: attendancerecords.ARCHIVED_BY → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< attendancerecords to | users
- Meaning: Archive action performed by a user.

7) Auth Method Owner
- Type: Many-to-One
- Mapping: authenticationmethods.USERID → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< authenticationmethods to | users
- Meaning: User can have multiple authentication methods.

8) Class Schedule Subject
- Type: Many-to-One
- Mapping: classschedules.SUBJECTID → subjects.SUBJECTID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< classschedules to | subjects
- Meaning: Schedules are created for a subject.

9) Class Schedule Room
- Type: Many-to-One
- Mapping: classschedules.ROOMID → rooms.ROOMID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< classschedules to | rooms
- Meaning: Schedules are assigned to a room.

10) Class Schedule Archived By
- Type: Many-to-One (optional)
- Mapping: classschedules.ARCHIVED_BY → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< classschedules to | users
- Meaning: Archive actor for schedule.





13) Course Instructor
- Type: Many-to-One
- Mapping: courses.INSTRUCTORID → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< courses to | users
- Meaning: Courses have an instructor owner.

14) Device Room
- Type: Many-to-One
- Mapping: devices.ROOMID → rooms.ROOMID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< devices to | rooms
- Meaning: Device installed in a room.

15) Room Archived By
- Type: Many-to-One (optional)
- Mapping: rooms.ARCHIVED_BY → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< rooms to | users
- Meaning: Archive action performer.

16) Session Schedule
- Type: Many-to-One
- Mapping: sessions.SCHEDULEID → classschedules.SCHEDULEID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< sessions to | classschedules
- Meaning: Session instantiates a schedule on a date.

17) Session Instructor
- Type: Many-to-One
- Mapping: sessions.INSTRUCTORID → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< sessions to | users
- Meaning: Session is led by an instructor.

18) Session Room
- Type: Many-to-One
- Mapping: sessions.ROOMID → rooms.ROOMID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< sessions to | rooms
- Meaning: Session occurs in a room.

19) Session Archived By
- Type: Many-to-One (optional)
- Mapping: sessions.ARCHIVED_BY → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< sessions to | users
- Meaning: Archive actor for session.

20) Subject Enrollment User
- Type: Many-to-One
- Mapping: subjectenrollment.USERID → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< subjectenrollment to | users
- Meaning: User enrolled in a subject.

21) Subject Enrollment Subject
- Type: Many-to-One
- Mapping: subjectenrollment.SUBJECTID → subjects.SUBJECTID
- Delete: Not specified (assume RESTRICT)
- Cardinality: ●< subjectenrollment to | subjects
- Meaning: Enrollment targets a subject.

22) Subject Enrollment Archived By
- Type: Many-to-One (optional)
- Mapping: subjectenrollment.ARCHIVED_BY → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< subjectenrollment to | users
- Meaning: Archive actor for subject enrollment.

23) Subject Instructor (optional)
- Type: Many-to-One (optional)
- Mapping: subjects.INSTRUCTORID → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< subjects to | users
- Meaning: Subject may have an assigned instructor.

24) Subject Archived By
- Type: Many-to-One (optional)
- Mapping: subjects.ARCHIVED_BY → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< subjects to | users
- Meaning: Archive actor for subject.

25) User Archived By (self)
- Type: Many-to-One (optional, self-reference)
- Mapping: users.ARCHIVED_BY → users.USERID
- Delete: Not specified (assume RESTRICT)
- Cardinality: O< users to | users
- Meaning: A user can archive another user.

5. Additional Context (if applicable)

- **Business rules**:
  - Attendance status derived from SCANDATETIME vs schedule STARTTIME/ENDTIME and SCANTYPE.
  - Sessions instantiate from classschedules per SESSIONDATE; door unlock/lock timestamps tracked.
  - Authentication methods must be active to authorize access; identifiers unique.
- **Role mappings**:
  - admin/dean: manage archival and settings.
  - instructor: owns courses/subjects, leads sessions.
  - student: holds enrollments and produces attendance.
- **Workflow descriptions**:
  - Device captures scan → accesslogs; if within session/schedule → attendancerecords created/updated; doors controlled via sessions state.

6. ERD Generation Requirements

- **Visual notation standards**: Use Crow's Foot; mark PK, FK; show nullability (optional attributes with O); include data types.
- **Cardinality symbols**: ●< many, | one, O| zero-or-one; apply at relationship ends.
- **Layout suggestions**: Place `users` centrally; group `subjects`, `classschedules`, `sessions`, `attendancerecords` together; place `rooms` and `devices` adjacent; separate `courses`/`` cluster; peripheral `settings` and `import_logs`.
- **Color coding recommendations**:
  - Entities: blue; Reference/master data: teal (`users`, `rooms`, `subjects`, `courses`); Transactions: purple (`attendancerecords`, `accesslogs`, `sessions`); Admin/config: gray (`settings`, `import_logs`).
- **Validation checklist**:
  - All 14 entities present with PKs marked.
  - 25 relationships drawn with correct cardinalities and optionality.
  - All FK attributes annotated with targets and nullability.
  - Unique constraints indicated (e.g., `ROOMNUMBER`, `IDENTIFIER`, `STUDENTID`, `FACULTYID`).
  - Enums listed; defaults and NOT NULL constraints shown.
  - Self-reference on `users.ARCHIVED_BY` modeled.

Input I Will Provide

[Paste your database schema here - include all tables with columns, data types, constraints, and foreign key relationships]

Output Format

Generate a complete markdown document following the exact structure of the IoT Attendance System ERD prompt above, populated with my database schema details.


