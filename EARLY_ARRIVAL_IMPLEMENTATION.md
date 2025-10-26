# Student Early Arrival Window - Implementation Complete

## Overview
Students can now scan at the outside sensor within a configurable window (default: 15 minutes) before class starts. Their attendance is marked as "Awaiting Confirmation" until they scan inside. If the instructor doesn't start the session and class time ends, early arrivals are automatically confirmed as "Present".

## Changes Made

### 1. Backend API (`backend/routes/logs.js`)
**New Endpoint:** `POST /api/logs/early-arrival-scan`

- Accepts `identifier`, `auth_method` (fingerprint/rfid), and `room_id`
- Validates user credentials via AUTHENTICATIONMETHODS table
- Checks if class starts within early arrival window (15 min default)
- Verifies student enrollment in the upcoming class
- Creates attendance record with STATUS = "Awaiting Confirmation"
- Logs access with type "early_arrival_scan"

**Key Logic:**
```javascript
// Find schedule starting within early arrival window
WHERE cs.STARTTIME > current_time
  AND cs.STARTTIME <= TIME_ADD(current_time, INTERVAL 15 MINUTE)
```

### 2. Auto-Confirmation Service (`backend/services/earlyArrivalService.js`)
**Purpose:** Automatically confirms early arrivals when instructors don't show

**Method:** `autoConfirmNoShowInstructor()`
- Runs every 5 minutes (scheduled in server.js)
- Finds schedules that ended without active sessions
- Updates all "Awaiting Confirmation" records to "Present" for those schedules
- Logs confirmation statistics

**Scheduled Job in `backend/server.js`:**
```javascript
setInterval(async () => {
    await earlyArrivalService.autoConfirmNoShowInstructor();
}, 5 * 60 * 1000); // Every 5 minutes
```

### 3. C# Application (`FutronicAttendanceSystem/MainForm.cs`)

#### RFID Early Arrival Logic (Line ~7613-7637)
When student scans at outside sensor with no active session:
```csharp
if (isDualSensorMode && currentScanLocation == "outside")
{
    // Try early arrival scan
    await RecordEarlyArrivalRfid(userName, userGuid, rfidTag);
}
```

#### New Method: `RecordEarlyArrivalRfid` (Line ~5728-5839)
- Calls backend API `/api/logs/early-arrival-scan`
- Sends: `{ identifier: rfidTag, auth_method: "rfid", room_id: roomId }`
- Handles success: Shows "Early arrival" message, triggers door unlock
- Handles errors: "Too early", "Not enrolled", etc.
- Logs to local attendance log

**API Payload:**
```csharp
{
    identifier = rfidTag,  // e.g., "0009157305"
    auth_method = "rfid",
    room_id = currentRoomId
}
```

#### Response Models (Line ~5841-5853)
```csharp
class EarlyArrivalResponse {
    public string Message { get; set; }
    public string Status { get; set; }
    public string Subject { get; set; }
}

class EarlyArrivalErrorResponse {
    public string Message { get; set; }
    public int EarlyArrivalWindow { get; set; }
}
```

### 4. Frontend Display (`frontend/src/pages/AttendanceLogs.jsx`)
**New Status:** "Awaiting Confirmation"

- Blue clock icon (ClockIcon)
- Blue badge styling: `bg-blue-100 text-blue-800`
- Displays in attendance logs list

### 5. Configuration (`backend/routes/settings.js`)
**New Setting:** `student_early_arrival_window`
- Default: 15 minutes
- Configurable via settings API

## How It Works

### Scenario 1: Normal Early Arrival
1. **13:00** - Student scans RFID at outside sensor
2. **Class scheduled:** 13:15
3. **System:** 
   - Checks: Is class within 15 minutes? ✓
   - Checks: Is student enrolled? ✓
   - Records: STATUS = "Awaiting Confirmation"
   - Unlocks door
   - Shows: "Early arrival. Please scan inside when class starts."
4. **13:15** - Instructor starts session
5. **13:16** - Student scans inside
6. **System:** Updates STATUS from "Awaiting Confirmation" → "Present"

### Scenario 2: Instructor No-Show
1. **13:00** - Student scans at outside sensor
2. **Status:** "Awaiting Confirmation"
3. **13:15** - Instructor never starts session
4. **14:00** - Class ends (scheduled end time)
5. **14:05** - Auto-confirmation service runs
6. **System:** Updates all "Awaiting Confirmation" → "Present" for that schedule

### Scenario 3: Too Early
1. **12:50** - Student scans at outside sensor
2. **Class scheduled:** 13:15 (25 minutes away)
3. **System:** 
   - Checks: Is class within 15 minutes? ✗
   - Denies access
   - Shows: "Too early. Please scan within 15 minutes of class start."

### Scenario 4: Not Enrolled
1. **13:00** - Student scans at outside sensor
2. **System:**
   - Checks: Is class within 15 minutes? ✓
   - Checks: Is student enrolled? ✗
   - Denies access
   - Shows: "You are not enrolled in [SUBJECT_CODE]"

## Database Schema
**No changes needed** - Uses existing `ATTENDANCERECORDS` table

**New STATUS value:** `"Awaiting Confirmation"`

**Existing columns used:**
- `STATUS` - Stores "Awaiting Confirmation", later updated to "Present"
- `LOCATION` - Set to "outside" for early arrival scans
- `SCANDATETIME` - Original early arrival time (preserved when confirmed)
- `SCHEDULEID` - Links to upcoming class schedule

## Testing Checklist

### Prerequisites
1. ✓ Configure outside sensor in FutronicAttendanceSystem
2. ✓ Set `student_early_arrival_window` to 15 minutes (default)
3. ✓ Ensure student is enrolled in upcoming class
4. ✓ Close and restart FutronicAttendanceSystem (to load new code)
5. ✓ Backend server running with auto-confirmation service

### Test Cases
- [ ] **Early Arrival Success:** Student scans 10 minutes early at outside → "Awaiting Confirmation"
- [ ] **Confirmation:** Student scans inside after instructor starts → "Present"
- [ ] **Auto-Confirm:** No instructor session + class ends → Auto "Present"
- [ ] **Too Early:** Student scans 20 minutes early → Access denied
- [ ] **Not Enrolled:** Non-enrolled student scans early → Access denied
- [ ] **Duplicate Scan:** Student with early arrival scans again → "Already has record"
- [ ] **Web UI:** "Awaiting Confirmation" shows with blue badge in attendance logs

## API Endpoints

### POST `/api/logs/early-arrival-scan`
**Request:**
```json
{
  "identifier": "0009157305",
  "auth_method": "rfid",
  "room_id": "5d3225b6-2235-4434-bda3-fad3ef9fbfbc"
}
```

**Success Response (201):**
```json
{
  "message": "Early arrival recorded for BSIT4-1. Please scan inside when class starts.",
  "status": "Awaiting Confirmation",
  "subject": "BSIT4-1 - Capstone Project 2",
  "classTime": "13:15:00",
  "attendance_id": "uuid-here"
}
```

**Error Responses:**
- **401** - Invalid credentials
- **403** - Not a student / Too early / Not enrolled
- **409** - Already has attendance record for today

## Logs to Monitor

### Backend Console
```
⏰ Early arrival scan request: { identifier, auth_method, room_id }
📅 Checking for classes: Day=Sunday, Time=13:00:00
⏰ Early arrival window: 15 minutes
✅ Found upcoming class: BSIT4-1 starting at 13:15:00
✅ Student is enrolled in the subject
✅ Early arrival recorded for HARLEY S. BUSA
```

### C# Application Console
```
=== EARLY ARRIVAL RFID SCAN REQUEST START ===
Student: HARLEY S. BUSA
RFID Tag: 0009157305
API URL: http://localhost:5000/api/logs/early-arrival-scan
Response Status: 201
✅ Early arrival recorded: Awaiting Confirmation
=== EARLY ARRIVAL RFID SCAN REQUEST END ===
```

### Auto-Confirmation Service (Every 5 minutes)
```
🔍 Checking for auto-confirmation of early arrivals...
📋 Found 2 schedules without sessions
✅ Auto-confirmed 5 early arrival records
```

## Configuration

### Backend Settings
**File:** `backend/routes/settings.js` or Database `SETTINGS` table

```sql
INSERT INTO SETTINGS (SETTINGKEY, SETTINGVALUE) 
VALUES ('student_early_arrival_window', '15');
```

### C# Application
**File:** `FutronicAttendanceSystem/bin/Debug/net48/appsettings.json`

```json
{
  "Application": {
    "ApiBaseUrl": "http://localhost:5000"
  }
}
```

## Troubleshooting

### Issue: "Too early" error even when within window
**Cause:** Time mismatch or incorrect schedule
**Fix:** Check `CLASSSCHEDULES.STARTTIME` and current server time

### Issue: "Not enrolled" error
**Cause:** Missing enrollment record
**Fix:** Check `SUBJECTENROLLMENT` table for student + subject

### Issue: Early arrival not auto-confirming
**Cause:** Scheduled job not running
**Fix:** Check backend logs for "Early arrival auto-confirmation service started"

### Issue: "Invalid credentials" error
**Cause:** RFID tag not in `AUTHENTICATIONMETHODS` table
**Fix:** Verify `AUTHENTICATIONMETHODS.IDENTIFIER` = RFID tag and `METHODTYPE` = 'rfid'

## Files Modified

1. `backend/routes/logs.js` - Added early arrival endpoint
2. `backend/services/earlyArrivalService.js` - **NEW** Auto-confirmation service
3. `backend/server.js` - Registered scheduled job
4. `FutronicAttendanceSystem/MainForm.cs` - Added early arrival logic + API calls
5. `frontend/src/pages/AttendanceLogs.jsx` - Added "Awaiting Confirmation" display

## Implementation Status
✅ **COMPLETE** - All components implemented and ready for testing

**Next Step:** Close FutronicAttendanceSystem and restart it to load the new code, then test with a student scanning at the outside sensor 10 minutes before class.

