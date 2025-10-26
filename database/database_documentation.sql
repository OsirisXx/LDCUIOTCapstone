/*
=============================================================================
                        DATABASE SCHEMA DOCUMENTATION
=============================================================================
Database Name: iot_attendance
Institution: Liceo de Cagayan University
System: IoT Attendance System
Documentation Date: January 2025
Purpose: Complete database schema documentation with entity summary and 
         detailed table specifications

=============================================================================
*/

/*
=============================================================================
                           ENTITY SUMMARY TABLE
=============================================================================
| ENTITY NAME              | NO. ATTRIBUTES | PRIMARY KEY | NO. FOREIGN KEYS |
|--------------------------|----------------|-------------|------------------|
| USERS                    | 16             | ✓           | 0                |
| AUTHENTICATIONMETHODS    | 9              | ✓           | 1                |
| ROOMS                    | 9              | ✓           | 0                |
| SUBJECTS                 | 9              | ✓           | 1                |
| CLASSSCHEDULES           | 10             | ✓           | 2                |
| SUBJECTENROLLMENT        | 8              | ✓           | 2                |
| SESSIONS                 | 16             | ✓           | 3                |
| ATTENDANCERECORDS        | 15             | ✓           | 3                |
| ACCESSLOGS               | 12             | ✓           | 2                |
| DEVICES                  | 12             | ✓           | 1                |
| SETTINGS                 | 8              | ✓           | 0                |
| IMPORT_LOGS              | 8              | ✓           | 0                |
=============================================================================
*/

/*
=============================================================================
                         DETAILED TABLE SPECIFICATIONS
=============================================================================
*/

/*
=============================================================================
                            Table 1. USERS Table
=============================================================================
| FILENAME         | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|------------------|-----------|------|------------------------------|---------------------------------------|
| USERID           | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for the user |
| FIRSTNAME        | VARCHAR   | 50   | NOT NULL                     | User's first name                     |
| LASTNAME         | VARCHAR   | 50   | NOT NULL                     | User's last name                      |
| USERTYPE         | ENUM      | -    | NOT NULL                     | Type: student, instructor, admin     |
| YEARLEVEL        | ENUM      | -    | DEFAULT NULL                 | Year level: 1, 2, 3, 4, 5           |
| EMAIL            | VARCHAR   | 100  | UNIQUE NOT NULL              | User's email address                 |
| PASSWORD_HASH    | VARCHAR   | 255  | DEFAULT NULL                 | Hashed password for authentication   |
| PHONENUMBER      | VARCHAR   | 15   | DEFAULT NULL                 | User's phone number                  |
| DEPARTMENT       | VARCHAR   | 100  | DEFAULT NULL                 | User's department affiliation        |
| STUDENTID        | VARCHAR   | 20   | UNIQUE DEFAULT NULL          | Unique student identifier            |
| FACULTYID        | VARCHAR   | 20   | UNIQUE DEFAULT NULL          | Unique faculty identifier            |
| EMPLOYEEID       | VARCHAR   | 20   | UNIQUE DEFAULT NULL          | Unique employee identifier           |
| RFIDTAG          | VARCHAR   | 100  | UNIQUE DEFAULT NULL          | RFID tag identifier                  |
| STATUS           | ENUM      | -    | DEFAULT 'Active'             | Account status: Active, Inactive     |
| CREATED_AT       | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT       | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships: 
- Referenced by: AUTHENTICATIONMETHODS (USERID), SUBJECTS (INSTRUCTORID), 
  SUBJECTENROLLMENT (USERID), SESSIONS (INSTRUCTORID), ATTENDANCERECORDS (USERID),
  ACCESSLOGS (USERID)

Indexes:
- PRIMARY KEY (USERID)
- UNIQUE INDEX (EMAIL)
- UNIQUE INDEX (STUDENTID)
- UNIQUE INDEX (FACULTYID)  
- UNIQUE INDEX (EMPLOYEEID)
- UNIQUE INDEX (RFIDTAG)
- INDEX idx_users_email
- INDEX idx_users_type_status
=============================================================================
*/

/*
=============================================================================
                     Table 2. AUTHENTICATIONMETHODS Table
=============================================================================
| FILENAME             | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|----------------------|-----------|------|------------------------------|---------------------------------------|
| AUTHID               | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for auth method |
| USERID               | CHAR      | 36   | NOT NULL                     | (FK) - Reference to USERS table       |
| METHODTYPE           | ENUM      | -    | NOT NULL                     | Method type: RFID, Fingerprint        |
| IDENTIFIER           | VARCHAR   | 100  | UNIQUE NOT NULL              | Unique identifier for the method      |
| RFIDTAGNUMBER        | VARCHAR   | 100  | UNIQUE DEFAULT NULL          | RFID tag number                      |
| FINGERPRINTTEMPLATE  | BLOB      | -    | DEFAULT NULL                 | Fingerprint biometric template       |
| ISACTIVE             | BOOLEAN   | -    | DEFAULT TRUE                 | Whether method is active              |
| DATEREGISTERED       | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Registration date                     |
| LASTUPDATED          | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Last update timestamp                 |
| STATUS               | ENUM      | -    | DEFAULT 'Active'             | Method status: Active, Suspended     |
=============================================================================
Relationships:
- Foreign Key: USERID → USERS(USERID) ON DELETE CASCADE

Indexes:
- PRIMARY KEY (AUTHID)
- UNIQUE INDEX (IDENTIFIER)
- UNIQUE INDEX (RFIDTAGNUMBER)
- INDEX idx_identifier_method (IDENTIFIER, METHODTYPE)
=============================================================================
*/

/*
=============================================================================
                            Table 3. ROOMS Table
=============================================================================
| FILENAME      | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|---------------|-----------|------|------------------------------|---------------------------------------|
| ROOMID        | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for the room |
| ROOMNUMBER    | VARCHAR   | 20   | UNIQUE NOT NULL              | Room number or identifier            |
| ROOMNAME      | VARCHAR   | 100  | DEFAULT NULL                 | Descriptive room name                 |
| BUILDING      | VARCHAR   | 100  | NOT NULL                     | Building name where room is located  |
| CAPACITY      | INT       | -    | UNSIGNED DEFAULT NULL        | Maximum room capacity                |
| DEVICEID      | VARCHAR   | 100  | DEFAULT NULL                 | Associated device identifier         |
| DOORSTATUS    | ENUM      | -    | DEFAULT 'locked'             | Door status: locked, unlocked        |
| STATUS        | ENUM      | -    | DEFAULT 'Available'          | Room status: Available, Maintenance  |
| CREATED_AT    | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT    | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships:
- Referenced by: CLASSSCHEDULES (ROOMID), SESSIONS (ROOMID), ACCESSLOGS (ROOMID), DEVICES (ROOMID)

Indexes:
- PRIMARY KEY (ROOMID)
- UNIQUE INDEX (ROOMNUMBER)
- INDEX idx_rooms_status
=============================================================================
*/

/*
=============================================================================
                           Table 4. SUBJECTS Table
=============================================================================
| FILENAME      | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|---------------|-----------|------|------------------------------|---------------------------------------|
| SUBJECTID     | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for subject |
| SUBJECTCODE   | VARCHAR   | 20   | NOT NULL                     | Subject code identifier              |
| SUBJECTNAME   | VARCHAR   | 255  | NOT NULL                     | Full name of the subject             |
| INSTRUCTORID  | CHAR      | 36   | NOT NULL                     | (FK) - Reference to USERS table      |
| SEMESTER      | VARCHAR   | 20   | NOT NULL                     | Academic semester                    |
| YEAR          | YEAR      | -    | NOT NULL                     | Academic year                        |
| ACADEMICYEAR  | VARCHAR   | 10   | NOT NULL                     | Academic year string                 |
| DESCRIPTION   | TEXT      | -    | DEFAULT NULL                 | Subject description                  |
| CREATED_AT    | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT    | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships:
- Foreign Key: INSTRUCTORID → USERS(USERID) ON DELETE CASCADE
- Referenced by: CLASSSCHEDULES (SUBJECTID), SUBJECTENROLLMENT (SUBJECTID)

Indexes:
- PRIMARY KEY (SUBJECTID)
- UNIQUE INDEX unique_subject_term (SUBJECTCODE, SEMESTER, ACADEMICYEAR)
=============================================================================
*/

/*
=============================================================================
                        Table 5. CLASSSCHEDULES Table
=============================================================================
| FILENAME      | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|---------------|-----------|------|------------------------------|---------------------------------------|
| SCHEDULEID    | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for schedule |
| SUBJECTID     | CHAR      | 36   | NOT NULL                     | (FK) - Reference to SUBJECTS table   |
| ROOMID        | CHAR      | 36   | NOT NULL                     | (FK) - Reference to ROOMS table      |
| DAYOFWEEK     | ENUM      | -    | NOT NULL                     | Day: Monday-Sunday                   |
| STARTTIME     | TIME      | -    | NOT NULL                     | Class start time                     |
| ENDTIME       | TIME      | -    | NOT NULL                     | Class end time                       |
| ACADEMICYEAR  | VARCHAR   | 10   | NOT NULL                     | Academic year                        |
| SEMESTER      | VARCHAR   | 20   | NOT NULL                     | Academic semester                    |
| CREATED_AT    | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT    | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
| ISLAB         | TINYINT   | 1    | DEFAULT 0                    | Lab flag (dynamically added)         |
=============================================================================
Relationships:
- Foreign Keys: SUBJECTID → SUBJECTS(SUBJECTID) ON DELETE CASCADE
                ROOMID → ROOMS(ROOMID) ON DELETE CASCADE
- Referenced by: SESSIONS (SCHEDULEID), ATTENDANCERECORDS (SCHEDULEID)

Indexes:
- PRIMARY KEY (SCHEDULEID)
=============================================================================
*/

/*
=============================================================================
                       Table 6. SUBJECTENROLLMENT Table
=============================================================================
| FILENAME        | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|-----------------|-----------|------|------------------------------|---------------------------------------|
| ENROLLMENTID    | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for enrollment |
| USERID          | CHAR      | 36   | NOT NULL                     | (FK) - Reference to USERS table      |
| SUBJECTID       | CHAR      | 36   | NOT NULL                     | (FK) - Reference to SUBJECTS table   |
| ENROLLMENTDATE  | DATE      | -    | DEFAULT (CURRENT_DATE)       | Date of enrollment                   |
| ACADEMICYEAR    | VARCHAR   | 10   | NOT NULL                     | Academic year                        |
| SEMESTER        | VARCHAR   | 20   | NOT NULL                     | Academic semester                    |
| STATUS          | ENUM      | -    | DEFAULT 'enrolled'           | Status: enrolled, dropped, completed |
| CREATED_AT      | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
=============================================================================
Relationships:
- Foreign Keys: USERID → USERS(USERID) ON DELETE CASCADE
                SUBJECTID → SUBJECTS(SUBJECTID) ON DELETE CASCADE

Indexes:
- PRIMARY KEY (ENROLLMENTID)
- UNIQUE INDEX unique_enrollment (USERID, SUBJECTID, ACADEMICYEAR, SEMESTER)
=============================================================================
*/

/*
=============================================================================
                            Table 7. SESSIONS Table
=============================================================================
| FILENAME                 | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|--------------------------|-----------|------|------------------------------|---------------------------------------|
| SESSIONID                | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for session |
| SCHEDULEID               | CHAR      | 36   | NOT NULL                     | (FK) - Reference to CLASSSCHEDULES   |
| INSTRUCTORID             | CHAR      | 36   | NOT NULL                     | (FK) - Reference to USERS table      |
| ROOMID                   | CHAR      | 36   | NOT NULL                     | (FK) - Reference to ROOMS table      |
| SESSIONDATE              | DATE      | -    | NOT NULL                     | Date of the session                  |
| STARTTIME                | TIMESTAMP | -    | DEFAULT NULL                 | Actual session start time            |
| ENDTIME                  | TIMESTAMP | -    | DEFAULT NULL                 | Actual session end time              |
| ATTENDANCE_WINDOW_MINUTES| INT       | -    | DEFAULT 15                   | Attendance window in minutes         |
| ATTENDANCE_DEADLINE      | TIMESTAMP | -    | DEFAULT NULL                 | Deadline for attendance              |
| STATUS                   | ENUM      | -    | DEFAULT 'waiting'            | Status: waiting, active, ended, cancelled |
| DOORUNLOCKEDAT           | TIMESTAMP | -    | DEFAULT NULL                 | Door unlock timestamp                |
| DOORLOCKEDAT             | TIMESTAMP | -    | DEFAULT NULL                 | Door lock timestamp                  |
| ACADEMICYEAR             | VARCHAR   | 10   | NOT NULL                     | Academic year                        |
| SEMESTER                 | VARCHAR   | 20   | NOT NULL                     | Academic semester                    |
| CREATED_AT               | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT               | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships:
- Foreign Keys: SCHEDULEID → CLASSSCHEDULES(SCHEDULEID) ON DELETE CASCADE
                INSTRUCTORID → USERS(USERID) ON DELETE CASCADE
                ROOMID → ROOMS(ROOMID) ON DELETE CASCADE
- Referenced by: ATTENDANCERECORDS (SESSIONID)

Indexes:
- PRIMARY KEY (SESSIONID)
- INDEX idx_session_date_status (SESSIONDATE, STATUS)
- INDEX idx_room_date_status (ROOMID, SESSIONDATE, STATUS)
=============================================================================
*/

/*
=============================================================================
                       Table 8. ATTENDANCERECORDS Table
=============================================================================
| FILENAME      | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|---------------|-----------|------|------------------------------|---------------------------------------|
| ATTENDANCEID  | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for record  |
| USERID        | CHAR      | 36   | NOT NULL                     | (FK) - Reference to USERS table      |
| SCHEDULEID    | CHAR      | 36   | NOT NULL                     | (FK) - Reference to CLASSSCHEDULES   |
| SESSIONID     | CHAR      | 36   | DEFAULT NULL                 | (FK) - Reference to SESSIONS table   |
| SCANTYPE      | ENUM      | -    | NOT NULL                     | Scan type: time_in, time_out, early_arrival, etc. |
| SCANDATETIME  | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Timestamp of scan                    |
| DATE          | DATE      | -    | NOT NULL                     | Date of attendance                   |
| TIMEIN        | TIME      | -    | DEFAULT NULL                 | Time in                              |
| AUTHMETHOD    | ENUM      | -    | NOT NULL                     | Auth method: RFID, Fingerprint       |
| LOCATION      | ENUM      | -    | NOT NULL                     | Location: inside, outside            |
| STATUS        | ENUM      | -    | NOT NULL                     | Status: Present, Late, Absent, Early Arrival, Early Scan | Absent |
| ACADEMICYEAR  | VARCHAR   | 10   | NOT NULL                     | Academic year                        |
| SEMESTER      | VARCHAR   | 20   | NOT NULL                     | Academic semester                    |
| CREATED_AT    | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT    | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships:
- Foreign Keys: USERID → USERS(USERID) ON DELETE CASCADE
                SCHEDULEID → CLASSSCHEDULES(SCHEDULEID) ON DELETE CASCADE
                SESSIONID → SESSIONS(SESSIONID) ON DELETE SET NULL

Indexes:
- PRIMARY KEY (ATTENDANCEID)
- INDEX idx_user_date (USERID, DATE)
- INDEX idx_schedule_date (SCHEDULEID, DATE)
- INDEX idx_status_date (STATUS, DATE)
=============================================================================
*/

/*
=============================================================================
                          Table 9. ACCESSLOGS Table
=============================================================================
| FILENAME      | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|---------------|-----------|------|------------------------------|---------------------------------------|
| LOGID         | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for log     |
| USERID        | CHAR      | 36   | DEFAULT NULL                 | (FK) - Reference to USERS table      |
| ROOMID        | CHAR      | 36   | NOT NULL                     | (FK) - Reference to ROOMS table      |
| TIMESTAMP     | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Timestamp of access attempt          |
| ACCESSTYPE    | ENUM      | -    | NOT NULL                     | Type: session_start, session_end, attendance_scan, door_override |
| AUTHMETHOD    | ENUM      | -    | NOT NULL                     | Method: RFID, Fingerprint, Manual    |
| LOCATION      | ENUM      | -    | NOT NULL                     | Location: inside, outside            |
| RESULT        | ENUM      | -    | NOT NULL                     | Result: success, denied              |
| REASON        | VARCHAR   | 255  | DEFAULT NULL                 | Reason for access result             |
| IPADDRESS     | VARCHAR   | 45   | DEFAULT NULL                 | IP address of request               |
| USERAGENT     | TEXT      | -    | DEFAULT NULL                 | User agent information               |
| CREATED_AT    | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
=============================================================================
Relationships:
- Foreign Keys: USERID → USERS(USERID) ON DELETE SET NULL
                ROOMID → ROOMS(ROOMID) ON DELETE CASCADE

Indexes:
- PRIMARY KEY (LOGID)
- INDEX idx_timestamp (TIMESTAMP)
- INDEX idx_user_timestamp (USERID, TIMESTAMP)
=============================================================================
*/

/*
=============================================================================
                           Table 10. DEVICES Table
=============================================================================
| FILENAME         | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|------------------|-----------|------|------------------------------|---------------------------------------|
| DEVICEID         | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for device  |
| DEVICETYPE       | ENUM      | -    | NOT NULL                     | Type: RFID_Reader, Fingerprint_Scanner, Door_Controller |
| DEVICENAME       | VARCHAR   | 100  | NOT NULL                     | Name of the device                   |
| LOCATION         | VARCHAR   | 255  | NOT NULL                     | Physical location of device          |
| ROOMID           | CHAR      | 36   | NOT NULL                     | (FK) - Reference to ROOMS table      |
| IPADDRESS        | VARCHAR   | 45   | DEFAULT NULL                 | Device IP address                    |
| MACADDRESS       | VARCHAR   | 17   | DEFAULT NULL                 | Device MAC address                   |
| FIRMWAREVERSION  | VARCHAR   | 50   | DEFAULT NULL                 | Device firmware version              |
| LASTMAINTENANCE  | DATE      | -    | DEFAULT NULL                 | Last maintenance date                |
| LASTSEEN         | TIMESTAMP | -    | DEFAULT NULL                 | Last communication timestamp         |
| STATUS           | ENUM      | -    | DEFAULT 'Active'             | Device status: Active, Inactive, Maintenance |
| CREATED_AT       | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT       | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships:
- Foreign Key: ROOMID → ROOMS(ROOMID) ON DELETE CASCADE

Indexes:
- PRIMARY KEY (DEVICEID)
- INDEX idx_devices_room (ROOMID)
- INDEX idx_devices_type (DEVICETYPE)
=============================================================================
*/

/*
=============================================================================
                          Table 11. SETTINGS Table
=============================================================================
| FILENAME      | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|---------------|-----------|------|------------------------------|---------------------------------------|
| SETTINGID     | CHAR      | 36   | PRIMARY KEY                  | (PK) - Unique identifier for setting |
| SETTINGKEY    | VARCHAR   | 100  | UNIQUE NOT NULL              | Setting key identifier               |
| SETTINGVALUE  | TEXT      | -    | NOT NULL                     | Setting value                        |
| DESCRIPTION   | TEXT      | -    | DEFAULT NULL                 | Description of the setting           |
| CATEGORY      | VARCHAR   | 50   | DEFAULT 'general'            | Setting category                     |
| ISEDITABLE    | BOOLEAN   | -    | DEFAULT TRUE                 | Whether setting can be edited        |
| CREATED_AT    | TIMESTAMP | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT    | TIMESTAMP | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships: None

Indexes:
- PRIMARY KEY (SETTINGID)
- UNIQUE INDEX (SETTINGKEY)
=============================================================================
*/

/*
=============================================================================
                         Table 12. IMPORT_LOGS Table
=============================================================================
| FILENAME      | TYPE      | SIZE | VALUE/CONSTRAINT              | DESCRIPTION                           |
|---------------|-----------|------|------------------------------|---------------------------------------|
| ID            | VARCHAR   | 36   | PRIMARY KEY                  | (PK) - Unique identifier for import log |
| ACADEMIC_YEAR | VARCHAR   | 10   | NOT NULL                     | Academic year of import              |
| SEMESTER      | VARCHAR   | 20   | NOT NULL                     | Semester of import                   |
| IMPORT_DATE   | DATETIME  | -    | NOT NULL                     | Date and time of import              |
| SUMMARY       | TEXT      | -    | DEFAULT NULL                 | Summary of import results            |
| STATUS        | VARCHAR   | 20   | DEFAULT 'completed'          | Import status                        |
| CREATED_AT    | DATETIME  | -    | DEFAULT CURRENT_TIMESTAMP    | Record creation timestamp            |
| UPDATED_AT    | DATETIME  | -    | ON UPDATE CURRENT_TIMESTAMP  | Record last update timestamp         |
=============================================================================
Relationships: None

Indexes:
- PRIMARY KEY (ID)
- INDEX idx_import_logs_date (IMPORT_DATE)
- INDEX idx_import_logs_academic (ACADEMIC_YEAR, SEMESTER)
=============================================================================
*/

/*
=============================================================================
                              END OF DOCUMENTATION
=============================================================================
Total Tables: 12
Total Attributes: 143
Total Primary Keys: 12
Total Foreign Key Relationships: 15

This documentation represents the complete database schema for the IoT Attendance
System at Liceo de Cagayan University, including all base tables and dynamically
added columns as implemented in the system.

Generated: January 2025
=============================================================================
*/
