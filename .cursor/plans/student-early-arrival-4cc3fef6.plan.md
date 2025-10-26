<!-- 4cc3fef6-1044-40e1-9335-e6c6ec172d63 6a1a4067-bdda-4d7c-b45d-5ce926caf4b7 -->
# Implement Student Early Arrival Window

## Overview

Enable students to scan at the outside sensor within the configured early arrival window (e.g., 15 minutes before class). Their attendance will be marked as "Awaiting Confirmation" and displayed in the web frontend. They must scan inside to confirm attendance. If the instructor never starts the session and the scheduled class time ends, all "Awaiting Confirmation" scans automatically become "Present".

## Implementation Details

### 1. FutronicAttendanceSystem - Outside Sensor Logic

**File**: `FutronicAttendanceSystem/MainForm.cs` (lines 3984-4027)

Currently, the outside sensor logic blocks students when there's no active session. We need to add early arrival detection:

- Check if student is scanning within the early arrival window (configured minutes before scheduled class start)
- If yes, send attendance record to backend with "Awaiting Confirmation" status
- If no, deny access as currently implemented

**Key Changes**:

- Add logic to query upcoming schedules from backend API
- Calculate if current time is within `numStudentEarlyArrivalWindow` minutes before class start
- Create attendance record with special status
- Still grant door access for early arrivals

### 2. Backend API - Early Arrival Endpoint

**File**: `backend/routes/scan.js` or `backend/routes/logs.js`

Add/modify endpoint to handle outside sensor scans with early arrival logic:

- Accept fingerprint/RFID scans from outside sensor
- Check if scan is within configured early arrival window
- Create ATTENDANCERECORDS entry with STATUS = "Awaiting Confirmation"
- Store LOCATION = "outside" and SCANTYPE = "early_arrival"

**New/Modified Endpoint**:

```
POST /api/logs/early-arrival-scan
- Validate user authentication method
- Check schedule: is there a class starting soon in this room?
- Calculate: currentTime >= (classStartTime - earlyArrivalWindow) && currentTime < classStartTime
- Insert attendance record with "Awaiting Confirmation" status
```

### 3. Backend API - Confirmation Scan

**File**: `backend/routes/scan.js` (lines 389-414)

Already has logic to handle "Early Arrival" confirmation - needs to be updated to handle "Awaiting Confirmation" status:

- When student scans inside and has existing "Awaiting Confirmation" record
- Update STATUS from "Awaiting Confirmation" to "Present"
- Update LOCATION to "inside", SCANTYPE to "time_in_confirmation"
- Preserve original early arrival timestamp in SCANDATETIME

### 4. Backend - Auto-Confirmation Service

**New File**: `backend/services/earlyArrivalService.js`

Create a scheduled job that runs every 5 minutes to:

- Find all sessions where scheduled end time has passed
- Check if instructor never started the session (no active session record)
- Find all ATTENDANCERECORDS with STATUS = "Awaiting Confirmation" for those schedules
- Update those records to STATUS = "Present" (auto-confirmed due to no-show instructor)

**Logic**:

```javascript
// Find schedules that ended without an active session
SELECT cs.SCHEDULEID, cs.ENDTIME 
FROM CLASSSCHEDULES cs
LEFT JOIN SESSIONS s ON cs.SCHEDULEID = s.SCHEDULEID AND s.SESSIONDATE = CURDATE()
WHERE cs.ENDTIME < CURTIME() 
  AND s.SESSIONID IS NULL

// Update awaiting confirmation records for those schedules
UPDATE ATTENDANCERECORDS 
SET STATUS = 'Present', 
    UPDATED_AT = NOW()
WHERE SCHEDULEID IN (...)
  AND STATUS = 'Awaiting Confirmation'
  AND DATE = CURDATE()
```

### 5. Backend - Settings Configuration

**File**: `backend/routes/settings.js`

Add new setting to SETTINGS table:

- SETTINGKEY = "student_early_arrival_window"
- SETTINGVALUE = "15" (default 15 minutes)
- SETTINGDESCRIPTION = "Minutes before class start that students can scan for early arrival"

This should be retrievable via existing settings API and configurable in the UI.

### 6. Frontend - Attendance Logs Display

**File**: `frontend/src/pages/AttendanceLogs.jsx` (lines 221-245)

Add support for "Awaiting Confirmation" status:

```javascript
case 'awaiting confirmation':
  return <ClockIcon className="h-5 w-5 text-blue-500" />;

// In getStatusColor:
case 'awaiting confirmation':
  return 'bg-blue-100 text-blue-800';
```

This ensures the status displays properly with a distinct blue color and clock icon.

### 7. FutronicAttendanceSystem - API Integration

**File**: `FutronicAttendanceSystem/MainForm.cs`

Add new API call method for early arrival scans:

```csharp
private async Task RecordEarlyArrival(string userId, string userName, string authMethod)
{
    // POST to backend API /api/logs/early-arrival-scan
    // Include: userId, roomId, authMethod, timestamp
}
```

Integrate this into the `ProcessStudentScan` outside sensor logic.

### 8. Backend - Scheduled Job Setup

**File**: `backend/server.js`

Register the auto-confirmation service to run periodically:

```javascript
const earlyArrivalService = require('./services/earlyArrivalService');

// Run every 5 minutes
setInterval(() => {
    earlyArrivalService.autoConfirmNoShowInstructor();
}, 5 * 60 * 1000);
```

## Expected Behavior

### Scenario 1: Student Arrives Early, Instructor Shows Up

1. Student scans outside 10 minutes before class (within 15-min window)
2. Status: "Awaiting Confirmation" (displayed in web UI)
3. Instructor arrives and starts session
4. Student scans inside
5. Status changes to "Present" (preserving early arrival time)

### Scenario 2: Student Arrives Early, Instructor No-Show

1. Student scans outside 10 minutes before class
2. Status: "Awaiting Confirmation"
3. Instructor never starts session
4. Scheduled class end time passes (e.g., 10:00 AM class ends at 11:30 AM)
5. Auto-confirmation service runs, detects no session was created
6. Status automatically changes to "Present"

### Scenario 3: Student Arrives Too Early

1. Student scans outside 20 minutes before class (outside 15-min window)
2. Access denied with message: "Too early. Please scan within 15 minutes of class start."

### Scenario 4: Student Scans Outside But Never Enters

1. Student scans outside (early arrival)
2. Status: "Awaiting Confirmation"
3. Class ends, no inside scan
4. If instructor showed: remains "Awaiting Confirmation" → manually review/mark absent
5. If instructor no-show: auto-confirms to "Present"

### To-dos

- [ ] Add early arrival window detection logic to ProcessStudentScan outside sensor in MainForm.cs
- [ ] Create backend API endpoint for recording early arrival scans with 'Awaiting Confirmation' status
- [ ] Update inside scanner confirmation logic to handle 'Awaiting Confirmation' status
- [ ] Create earlyArrivalService.js for auto-confirming attendance when instructor doesn't show
- [ ] Add student_early_arrival_window setting to database and settings API
- [ ] Add 'Awaiting Confirmation' status display to AttendanceLogs.jsx with blue styling
- [ ] Register auto-confirmation service as scheduled job in server.js