<!-- 16ed3e1a-21ab-49d5-8370-88e0c058660a ae2113f9-85be-49c8-a665-63f6836197a3 -->
# Early Arrival Timestamp and Late/Absent Marking

## Overview

When students scan early (RFID outside), record that timestamp. When they confirm later (fingerprint inside), preserve the early scan time for reports if confirmation happens within the grace period. Mark students as "Late" if they scan after the 15-minute grace period, and show "Absent" in reports UI if they never scan.

## Changes Required

### 1. Database - Track Early Arrival Scans

**File**: `database/schema.sql`

Verify `ATTENDANCERECORDS` has:
- `SCANDATETIME` - actual scan time
- `TIMEIN` - time to use for reports (earliest scan or confirmation scan)
- `ACTIONTYPE` - to identify "Early Arrival"

No schema changes needed - we'll use existing columns.

### 2. C# Backend - Preserve Early Arrival Timestamp

**File**: `FutronicAttendanceSystem/Database/DatabaseManager.cs`

**A) Update early arrival recording** (around line 1110-1180 in `InternalTryRecordAttendance`):

When recording "Early Arrival", store the actual scan time in both `SCANDATETIME` and `TIMEIN`:

```csharp
// For early arrivals, capture the scan time explicitly
DateTime scanTime = DateTime.Now;

// Existing INSERT logic
var insertCmd = new MySqlCommand(@"
    INSERT INTO ATTENDANCERECORDS
    (ATTENDANCEID, USERID, SCHEDULEID, SESSIONID, SCANTYPE, SCANDATETIME, DATE, TIMEIN, AUTHMETHOD, LOCATION, STATUS, ACTIONTYPE, ACADEMICYEAR, SEMESTER)
    VALUES (UUID(), @USERID, @SCHEDULEID, @SESSIONID, @SCANTYPE, @SCANDATETIME, CURRENT_DATE,
            @TIMEIN,
            @AUTHMETHOD, @LOCATION, @STATUS, @ACTIONTYPE,
            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY='current_academic_year' LIMIT 1),
            (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY='current_semester' LIMIT 1))
", connection);

// Add explicit time parameters
insertCmd.Parameters.AddWithValue("@SCANDATETIME", scanTime);
insertCmd.Parameters.AddWithValue("@TIMEIN", scanTime.TimeOfDay);
```

**B) Update confirmation scan logic**:

When a student scans inside for confirmation after early arrival, check if there's an existing "Early Arrival" record within the same session/schedule. If found and within grace period, keep the early arrival time:

```csharp
// Before recording new attendance, check for prior early arrival
string priorEarlyArrivalQuery = @"
    SELECT ATTENDANCEID, TIMEIN, SCANDATETIME, SCHEDULEID
    FROM ATTENDANCERECORDS
    WHERE USERID = @userGuid
      AND DATE = CURRENT_DATE
      AND ACTIONTYPE = 'Early Arrival'
      AND SCHEDULEID = @scheduleId
    ORDER BY SCANDATETIME DESC
    LIMIT 1";

DateTime? earlyArrivalTime = null;
string priorAttendanceId = null;

using (var cmdCheckEarly = new MySqlCommand(priorEarlyArrivalQuery, connection))
{
    cmdCheckEarly.Parameters.AddWithValue("@userGuid", userGuid);
    cmdCheckEarly.Parameters.AddWithValue("@scheduleId", scheduleId);
    
    using (var reader = cmdCheckEarly.ExecuteReader())
    {
        if (reader.Read())
        {
            priorAttendanceId = reader.GetString("ATTENDANCEID");
            earlyArrivalTime = reader.IsDBNull(reader.GetOrdinal("TIMEIN")) 
                ? null 
                : (DateTime?)TimeSpan.Parse(reader.GetString("TIMEIN"));
        }
    }
}

// If early arrival exists, update it instead of inserting new
if (priorAttendanceId != null && earlyArrivalTime.HasValue)
{
    // Update the existing record to reflect confirmation
    var updateCmd = new MySqlCommand(@"
        UPDATE ATTENDANCERECORDS
        SET ACTIONTYPE = @ACTIONTYPE,
            AUTHMETHOD = 'RFID + Fingerprint',
            LOCATION = @LOCATION,
            STATUS = @STATUS,
            SESSIONID = @SESSIONID
        WHERE ATTENDANCEID = @ATTENDANCEID
    ", connection);
    
    updateCmd.Parameters.AddWithValue("@ATTENDANCEID", priorAttendanceId);
    updateCmd.Parameters.AddWithValue("@ACTIONTYPE", "Sign In"); // or keep as "Early Arrival"
    updateCmd.Parameters.AddWithValue("@LOCATION", location);
    updateCmd.Parameters.AddWithValue("@STATUS", "Present");
    updateCmd.Parameters.AddWithValue("@SESSIONID", (object)sessionId ?? DBNull.Value);
    updateCmd.ExecuteNonQuery();
    
    return new AttendanceAttemptResult { Success = true, ... };
}
// Otherwise, insert new record as normal
```

### 3. C# Backend - Add Late Marking Logic

**File**: `FutronicAttendanceSystem/Database/DatabaseManager.cs`

**A) Update status determination** (around line 1110):

Calculate if scan is late:

```csharp
// Determine status based on timing
string status = "Present";
string actionType = "Sign In";

// Get class start time from schedule
TimeSpan classStartTime = TimeSpan.Zero;
using (var cmdGetStart = new MySqlCommand(
    "SELECT STARTTIME FROM CLASSSCHEDULES WHERE SCHEDULEID = @scheduleId", connection))
{
    cmdGetStart.Parameters.AddWithValue("@scheduleId", scheduleId);
    var startObj = cmdGetStart.ExecuteScalar();
    if (startObj != null && TimeSpan.TryParse(startObj.ToString(), out var st))
    {
        classStartTime = st;
    }
}

var currentTime = DateTime.Now.TimeOfDay;
var minutesAfterStart = (currentTime - classStartTime).TotalMinutes;

// Check if this is an early arrival
if (isEarlyArrival || minutesAfterStart < 0)
{
    actionType = "Early Arrival";
    status = "Present";
}
// Check if scan is within grace period (15 minutes after start)
else if (minutesAfterStart > 15)
{
    status = "Late";
    actionType = "Sign In";
}
else
{
    status = "Present";
    actionType = "Sign In";
}
```

### 4. Node.js Backend - Update Reports Endpoint

**File**: `backend/routes/logs.js` or create new `backend/routes/reports.js`

**A) Modify attendance query for reports**:

```javascript
router.get('/reports/attendance', async (req, res) => {
    try {
        const { startDate, endDate, scheduleId, subjectId } = req.query;
        
        const query = `
            SELECT 
                ar.ATTENDANCEID,
                ar.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.IDNUMBER,
                ar.SCHEDULEID,
                ar.SCANDATETIME,
                ar.TIMEIN as TIME_SCANNED,  -- Use TIMEIN which preserves early arrival time
                ar.STATUS,
                ar.ACTIONTYPE,
                ar.AUTHMETHOD,
                ar.LOCATION,
                s.SUBJECTNAME,
                s.SUBJECTCODE,
                r.ROOMNUMBER,
                cs.STARTTIME,
                cs.ENDTIME,
                ar.DATE as ATTENDANCE_DATE
            FROM ATTENDANCERECORDS ar
            JOIN USERS u ON ar.USERID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE ar.ARCHIVED_AT IS NULL
              ${startDate ? 'AND ar.DATE >= ?' : ''}
              ${endDate ? 'AND ar.DATE <= ?' : ''}
              ${scheduleId ? 'AND ar.SCHEDULEID = ?' : ''}
              ${subjectId ? 'AND cs.SUBJECTID = ?' : ''}
            ORDER BY ar.DATE DESC, ar.TIMEIN ASC
        `;
        
        const params = [];
        if (startDate) params.push(startDate);
        if (endDate) params.push(endDate);
        if (scheduleId) params.push(scheduleId);
        if (subjectId) params.push(subjectId);
        
        const attendance = await db.query(query, params);
        
        // Enrich with "Early" status display
        const enriched = attendance.map(record => {
            // Calculate if student was early
            if (record.STARTTIME && record.TIME_SCANNED) {
                const startTime = parseTime(record.STARTTIME);
                const scanTime = parseTime(record.TIME_SCANNED);
                const minutesDiff = (scanTime - startTime) / (1000 * 60);
                
                if (minutesDiff < 0 && record.ACTIONTYPE === 'Early Arrival') {
                    record.DISPLAY_STATUS = 'Early';
                } else {
                    record.DISPLAY_STATUS = record.STATUS;
                }
            } else {
                record.DISPLAY_STATUS = record.STATUS;
            }
            
            return record;
        });
        
        res.json({ success: true, data: enriched });
    } catch (error) {
        console.error('Reports error:', error);
        res.status(500).json({ success: false, error: error.message });
    }
});

function parseTime(timeStr) {
    const [hours, minutes, seconds] = timeStr.split(':').map(Number);
    const date = new Date();
    date.setHours(hours, minutes, seconds || 0, 0);
    return date.getTime();
}
```

**B) Add enrolled students query for absent detection**:

```javascript
router.get('/reports/attendance-with-absents', async (req, res) => {
    try {
        const { date, scheduleId } = req.query;
        
        // Get all enrolled students for the schedule
        const enrolledQuery = `
            SELECT DISTINCT
                u.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.IDNUMBER
            FROM SUBJECTENROLLMENT se
            JOIN USERS u ON se.USERID = u.USERID
            JOIN CLASSSCHEDULES cs ON se.SUBJECTID = cs.SUBJECTID
            WHERE cs.SCHEDULEID = ?
              AND se.STATUS = 'enrolled'
              AND u.STATUS = 'Active'
        `;
        
        const enrolled = await db.query(enrolledQuery, [scheduleId]);
        
        // Get actual attendance records
        const attendanceQuery = `
            SELECT 
                USERID,
                TIMEIN as TIME_SCANNED,
                STATUS,
                ACTIONTYPE
            FROM ATTENDANCERECORDS
            WHERE SCHEDULEID = ?
              AND DATE = ?
              AND ARCHIVED_AT IS NULL
        `;
        
        const attendance = await db.query(attendanceQuery, [scheduleId, date]);
        
        // Build attendance map
        const attendanceMap = {};
        attendance.forEach(record => {
            attendanceMap[record.USERID] = record;
        });
        
        // Merge enrolled with attendance
        const report = enrolled.map(student => {
            const record = attendanceMap[student.USERID];
            
            if (record) {
                return {
                    ...student,
                    TIME_SCANNED: record.TIME_SCANNED,
                    DISPLAY_STATUS: record.ACTIONTYPE === 'Early Arrival' ? 'Early' : record.STATUS,
                    ACTIONTYPE: record.ACTIONTYPE
                };
            } else {
                return {
                    ...student,
                    TIME_SCANNED: null,
                    DISPLAY_STATUS: 'Absent',
                    ACTIONTYPE: null
                };
            }
        });
        
        res.json({ success: true, data: report });
    } catch (error) {
        console.error('Reports error:', error);
        res.status(500).json({ success: false, error: error.message });
    }
});
```

### 5. React Frontend - Update Reports Display

**File**: `frontend/src/pages/Reports.jsx`

**A) Update time display to use `TIME_SCANNED` field**:

```jsx
<td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
    {record.TIME_SCANNED ? formatTime(record.TIME_SCANNED) : 'N/A'}
</td>
```

**B) Update status display to show "Early" for early arrivals**:

```jsx
<td className="px-6 py-4 whitespace-nowrap">
    <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${
        record.DISPLAY_STATUS === 'Early' ? 'bg-blue-100 text-blue-800' :
        record.DISPLAY_STATUS === 'Present' ? 'bg-green-100 text-green-800' :
        record.DISPLAY_STATUS === 'Late' ? 'bg-yellow-100 text-yellow-800' :
        record.DISPLAY_STATUS === 'Absent' ? 'bg-red-100 text-red-800' :
        'bg-gray-100 text-gray-800'
    }`}>
        {record.DISPLAY_STATUS}
    </span>
</td>
```

### 6. Database Migration (if needed)

**File**: `database/migration_timein_logic.sql`

```sql
-- Ensure TIMEIN column exists and is properly typed
-- (Should already exist from schema, but verify)

-- Backfill any NULL TIMEIN values from SCANDATETIME
UPDATE ATTENDANCERECORDS
SET TIMEIN = TIME(SCANDATETIME)
WHERE TIMEIN IS NULL AND SCANDATETIME IS NOT NULL;
```

## Testing Scenarios

1. **Early Arrival + Confirmation Within Grace Period**:
   - Student scans RFID outside at 14:50 (class at 15:00)
   - Record created: ACTIONTYPE='Early Arrival', TIMEIN='14:50:00'
   - Student scans fingerprint inside at 15:05 (5 min after start)
   - Record updated: ACTIONTYPE='Sign In', TIMEIN='14:50:00' (preserved), STATUS='Present'
   - Reports show: TIME_SCANNED='14:50:00', DISPLAY_STATUS='Early'

2. **Late Arrival**:
   - Student scans at 15:16 (16 min after 15:00 start)
   - Record created: STATUS='Late', TIMEIN='15:16:00'
   - Reports show: TIME_SCANNED='15:16:00', DISPLAY_STATUS='Late'

3. **Absent**:
   - Student never scans
   - Reports query shows: DISPLAY_STATUS='Absent', TIME_SCANNED=null

4. **On-Time Arrival**:
   - Student scans at 15:10 (10 min after 15:00 start, within grace)
   - Record created: STATUS='Present', TIMEIN='15:10:00'
   - Reports show: TIME_SCANNED='15:10:00', DISPLAY_STATUS='Present'

## Summary

- Early arrival timestamp is preserved when student confirms within grace period
- Students scanning >15 minutes after start are marked "Late"
- Reports display earliest scan time (early arrival time if applicable)
- Reports UI shows "Absent" for students who never scanned (not written to DB)
- "Early" status shown in reports for early arrivals who confirmed on time
