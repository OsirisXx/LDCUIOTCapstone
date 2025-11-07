# IoT Attendance System - Complete Technical Documentation

## 1. System Architecture Overview

This system uses a three-tier architecture:

**Frontend**: React.js Single Page Application (SPA) with TailwindCSS
**Backend**: Node.js + Express.js REST API
**Database**: MySQL with UUID primary keys
**IoT Devices**:
  - ESP32 (Arduino-based) - Solenoid lock controller
  - C# Windows Application - Fingerprint/RFID scanner (Futronic SDK)
  - RFID readers (USB/HID keyboard emulation)

---

## 2. API Usage & Communication Patterns

### REST APIs (Backend ↔ Frontend/C#)

**Protocol**: HTTP/HTTPS
**Authentication**: JWT Bearer tokens
**Base URL**: http://localhost:5000/api

**Main API Endpoints**:
  - /api/auth/* - Authentication (login, verify, change password)
  - /api/scan/* - IoT scanning endpoints
  - /api/sessions/* - Session management
  - /api/lock-control/* - Door lock control
  - /api/users/*, /api/rooms/*, /api/subjects/* - CRUD operations
  - /api/archive/*, /api/backup/* - Data management

### WebSocket/UDP Communication

**UDP Discovery Server (Port 8888)**
  - ESP32 discovers backend server automatically
  - Broadcast message: IOT_ATTENDANCE_DISCOVERY
  - Response: IOT_ATTENDANCE_SERVER
  - Used for automatic IP discovery

### HTTP Client-Server (ESP32 ↔ Backend)

**ESP32 Web Server (Port 80)**
  - Receives lock control commands via POST /api/lock-control
  - Payload: {action: "open"/"close", user: "Name"}
  - Returns JSON responses

### Database Connection

**MySQL Connection Pool**
  - Library: mysql2 (Node.js)
  - Connection pooling (10 connections)
  - Transactions supported
  - Character set: utf8mb4

---

## 3. System Flows

### Flow 1: Instructor Check-In (Outside Door)

1. Instructor scans RFID/Fingerprint (outside sensor)

2. C# App captures scan → Validates against AUTHENTICATIONMETHODS

3. C# App calls POST /api/scan/instructor-outside

4. Backend validates:
   - User exists & is instructor
   - Has scheduled class within ±15 minutes
   - No active session exists

5. Backend creates SESSION (status='active')

6. Backend calls POST /api/lock-control/request

7. Backend finds ESP32 device for room

8. Backend sends HTTP POST to ESP32: /api/lock-control

9. ESP32 activates relay (GPIO 5) for 3 seconds

10. Door unlocks (solenoid lock opens)

11. Records logged to:
    - ATTENDANCERECORDS (instructor time_in)
    - ACCESSLOGS (session_start, door_unlock)
    - SESSIONS (status='active')

### Flow 2: Student Attendance (Inside Room)

1. Student scans fingerprint (inside sensor)

2. C# App captures scan → Validates against AUTHENTICATIONMETHODS

3. C# App calls POST /api/scan/student

4. Backend validates:
   - User exists & is student
   - Active session exists in room
   - Student enrolled in subject
   - Not already recorded today

5. Backend calculates: Late if >15 minutes after session start

6. Backend inserts ATTENDANCERECORDS:
   - STATUS: 'Present' or 'Late'
   - SCANTYPE: 'time_in'
   - LOCATION: 'inside'

7. Response returned to C# App

8. C# App displays success message

### Flow 3: Early Arrival (Student Outside, 15min Before Class)

1. Student scans fingerprint/RFID (outside sensor, before class)

2. C# App calls POST /api/scan/student-outside

3. Backend validates:
   - Class starts within next 15 minutes
   - Student enrolled in subject

4. Backend inserts ATTENDANCERECORDS:
   - STATUS: 'Early Arrival'
   - SCANTYPE: 'early_arrival'
   - LOCATION: 'outside'

5. When instructor starts session:
   - All 'Early Arrival' records → Upgraded to 'Present'
   - SCANTYPE: 'early_arrival_upgraded'

6. Auto-confirmation service (runs every 5 minutes):
   - If session ends without instructor starting:
   - 'Early Arrival' → 'Early Scan | Absent'

### Flow 4: Instructor Check-Out (End Session)

1. Instructor scans RFID/Fingerprint (outside or inside)

2. C# App calls POST /api/scan/instructor-outside (or instructor-inside)

3. Backend finds active session

4. Backend updates SESSION:
   - STATUS: 'ended'
   - ENDTIME: CURRENT_TIMESTAMP
   - DOORLOCKEDAT: CURRENT_TIMESTAMP (if outside)

5. If outside: Backend sends lock command to ESP32

6. ESP32 closes lock (relay deactivated)

7. Door locks

8. Records logged:
   - ATTENDANCERECORDS (instructor time_out)
   - ACCESSLOGS (session_end, door_lock)

### Flow 5: Authentication Flow (Web Login)

1. User enters email/password in React frontend

2. Frontend calls POST /api/auth/login

3. Backend queries USERS table (email or student_id/faculty_id)

4. Backend validates password using bcrypt.compare()

5. Backend generates JWT token (expires in 7 days)

6. Frontend stores token in localStorage

7. Frontend includes token in Authorization header:
   Authorization: Bearer <token>

8. Protected routes validate token via authenticateToken middleware

---

## 4. Technical Terms & Concepts

### Authentication & Authorization

**JWT (JSON Web Token)**: Stateless authentication tokens used for session management
**bcrypt**: Password hashing algorithm (salt rounds: 10)
**Bearer Token**: Authentication header format: Authorization: Bearer <token>
**Role-Based Access Control (RBAC)**: User roles: admin, instructor, student, custodian, dean

### Database Concepts

**UUID (Universally Unique Identifier)**: CHAR(36) primary keys for all tables
**Connection Pooling**: Reusable database connections (10 connections max)
**Transactions**: Atomic database operations (BEGIN/COMMIT/ROLLBACK)
**Foreign Keys**: Referential integrity between tables
**ENUM types**: Predefined value sets (e.g., STATUS, USERTYPE)
**BLOB**: Binary Large Object for storing fingerprint templates

### IoT & Hardware

**ESP32**: WiFi-enabled microcontroller for lock control
**GPIO (General Purpose Input/Output)**: Digital pins for hardware control
**Solenoid Lock**: Electromagnetic door lock mechanism
**Relay Module**: Electronic switch for controlling high-voltage devices
**OLED Display**: Small screen (128x64) for status display
**HID (Human Interface Device)**: USB keyboard emulation for RFID readers

### Software Patterns

**RESTful API**: HTTP methods (GET, POST, PUT, DELETE) for resource manipulation
**Middleware**: Request processing functions (authentication, validation)
**Service Layer**: Business logic separation from routes
**Transaction Management**: Database consistency guarantees
**Event-Driven**: Asynchronous processing patterns

### Security

**Rate Limiting**: Prevent API abuse (1000 requests per 15 minutes)
**Helmet**: Security headers middleware
**CORS**: Cross-Origin Resource Sharing configuration
**Input Validation**: express-validator for request validation
**SQL Injection Prevention**: Parameterized queries

### Frontend Concepts

**SPA (Single Page Application)**: React router for client-side navigation
**Context API**: Global state management (AuthContext)
**React Query**: Data fetching and caching library
**TailwindCSS**: Utility-first CSS framework
**Toast Notifications**: react-hot-toast for user feedback

### Network Protocols

**UDP (User Datagram Protocol)**: Discovery service for IoT devices
**HTTP/HTTPS**: REST API communication
**WiFi**: 2.4GHz network requirement for ESP32
**TCP/IP**: Standard networking stack

---

## 5. Key Database Tables & Relationships

### Core Tables

**1. USERS (18 attributes)**
   - Stores all user information
   - User types: student, instructor, admin, custodian, dean
   - Contains: STUDENTID, FACULTYID, EMPLOYEEID

**2. AUTHENTICATIONMETHODS (10 attributes)**
   - Stores RFID tags and fingerprint templates
   - Links to USERS via USERID
   - Methods: RFID, Fingerprint

**3. SUBJECTS (13 attributes)**
   - Course catalog
   - Links to instructor via INSTRUCTORID

**4. CLASSSCHEDULES (14 attributes)**
   - Recurring class schedules
   - Links to SUBJECTS and ROOMS

**5. SESSIONS (17 attributes)**
   - Active class sessions
   - Tracks door unlock/lock times
   - Status: waiting, active, ended, cancelled

**6. ATTENDANCERECORDS (19 attributes)**
   - All attendance scans
   - Status: Present, Late, Absent, Early Arrival
   - Links to SESSIONS and SCHEDULES

**7. ACCESSLOGS (12 attributes)**
   - All access attempts (success/denied)
   - Tracks: session_start, session_end, attendance_scan, door_override

**8. ROOMS (13 attributes)**
   - Physical rooms
   - Door status: locked/unlocked

**9. DEVICES (13 attributes)**
   - IoT device registry
   - Types: RFID_Reader, Fingerprint_Scanner, Door_Controller

**10. SETTINGS (8 attributes)**
    - System configuration
    - Stores: current_academic_year, current_semester, late_tolerance_minutes

---

## 6. Important Technical Details

### Schedule Validation Service

- Validates instructor schedules within ±15 minutes of start time
- Validates student enrollment in subjects
- Checks for active sessions
- Handles early arrival (15 minutes before class)

### Early Arrival Auto-Confirmation

- Runs every 5 minutes (cron job)
- Auto-confirms early arrivals if instructor doesn't show
- Converts "Early Arrival" → "Present" when session starts
- Converts "Early Arrival" → "Early Scan | Absent" if session ends

### Dual Authentication (Cross-Type Verification)

- Instructors can use RFID + Fingerprint (two-step verification)
- 20-second timeout for cross-verification
- Prevents unauthorized access

### Lock Control Integration

- ESP32 receives HTTP POST commands
- Lock opens for 3 seconds (non-blocking timer)
- Auto-closes after duration
- OLED display shows status messages

### Backup & Archive System

**Backup Service**: Creates ZIP archives
  - Includes: Database dump (SQL/JSON), uploaded files, config files

**Archive System**: Moves old data to archive tables
  - Supports: Subjects, Schedules, Attendance, Rooms, Users
  - Tracks: ARCHIVED_AT, ARCHIVED_BY, ARCHIVE_REASON

### UDP Discovery Protocol

ESP32 sends: "IOT_ATTENDANCE_DISCOVERY"
Backend responds: "IOT_ATTENDANCE_SERVER"
ESP32 extracts server IP from response

### Academic Year/Semester Scoping

- All queries filtered by current_academic_year and current_semester
- Stored in SETTINGS table
- Prevents data mixing between terms

### Late Detection Logic

- Default tolerance: 15 minutes after session start time
- Calculated: SCANDATETIME > (SESSION.STARTTIME + 15 minutes)
- Status: 'Late' vs 'Present'

---

## 7. Dependencies & Technologies

### Backend (Node.js)

- express: Web framework
- mysql2: MySQL driver
- jsonwebtoken: JWT tokens
- bcryptjs: Password hashing
- axios: HTTP client
- moment: Date/time handling
- archiver: ZIP file creation
- multer: File uploads
- pdf-parse: PDF parsing
- xlsx: Excel file handling

### Frontend (React)

- react: UI library
- react-router-dom: Routing
- axios: HTTP client
- @tanstack/react-query: Data fetching
- tailwindcss: CSS framework
- react-hot-toast: Notifications

### C# Application

- Futronic SDK: Fingerprint scanner API
- .NET Framework 4.8 / .NET 8.0
- Windows Forms: UI framework
- HttpClient: REST API calls

### ESP32 (Arduino)

- WiFi: Network connectivity
- WebServer: HTTP server
- ArduinoJson: JSON parsing
- Adafruit_SSD1306: OLED display
- Wire: I2C communication

---

## 8. File Structure Overview

IOTCapstone/
├── backend/
│   ├── config/          # Database configuration
│   ├── middleware/      # Auth middleware
│   ├── routes/          # API route handlers
│   ├── services/        # Business logic services
│   ├── backups/         # Backup files
│   ├── uploads/         # Uploaded files
│   └── server.js        # Main server file
├── frontend/
│   ├── src/
│   │   ├── components/  # React components
│   │   ├── contexts/    # React contexts
│   │   ├── pages/       # Page components
│   │   └── App.jsx      # Main app
│   └── build/           # Production build
├── database/
│   └── schema.sql       # Database schema
├── FutronicAttendanceSystem/
│   ├── MainForm.cs      # Main C# application
│   ├── Database/        # Database models
│   └── Utils/           # Utility classes
└── sketch_feb18a_copy_20250803144501/
    └── iot_fingerprint_attendance.ino  # ESP32 code

---

## 9. Critical Business Rules

1. Instructors must scan outside to start session (unlocks door)
2. Students can only scan inside (after session starts)
3. Early arrival allowed 15 minutes before scheduled start
4. Late tolerance: 15 minutes after session start
5. Only one active session per schedule per day
6. Students must be enrolled to record attendance
7. Instructors need scheduled class within ±15 minutes
8. Door auto-locks after 3 seconds (ESP32)
9. Sessions must be active for student attendance
10. Custodians/Deans have unrestricted access

---

## 10. Error Handling & Logging

- Access logs: All attempts logged (success/denied)
- Console logging: Detailed debug information
- Error responses: Standardized JSON error format
- Transaction rollback: Database consistency
- Retry logic: WiFi reconnection (ESP32)
- Watchdog timers: Detect stuck operations (C#)

---

This system integrates multiple technologies to automate classroom attendance and access control. The architecture separates concerns: frontend for management, backend for business logic, IoT devices for physical control, and MySQL for data persistence.

