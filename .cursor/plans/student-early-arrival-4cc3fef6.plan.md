<!-- 4cc3fef6-1044-40e1-9335-e6c6ec172d63 60824456-3082-4232-8c0c-729f72374f4c -->
# Instructor Early Arrival Window Implementation

## Overview

Allow instructors to start attendance sessions within a configurable window BEFORE their scheduled class start time (e.g., 15 minutes early).

## Current Issue

- Instructor tries to scan at **13:28 PM**
- Class scheduled for **13:35 PM** (7 minutes early)
- System denies: "No scheduled class for instructor at this time"
- Configuration shows `InstructorEarlyWindow = 15` minutes

## Configuration Source

File: `attendance_scenarios.json` in application directory

```json
{
  "InstructorEarlyWindow": 15,
  "StudentGracePeriod": 15,
  "InstructorLateTolerance": 30,
  "AutoCloseDelay": 30,
  "StudentEarlyArrivalWindow": 15,
  "InstructorEndTolerance": 15
}
```

## Changes Needed

### 1. DatabaseManager.cs - Add Configuration Loading

**Location:** After `LoadAcademicSettings()` method (~line 99)

Add method to load scenarios configuration:

```csharp
private int LoadInstructorEarlyWindow()
{
    try
    {
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "attendance_scenarios.json");
        if (File.Exists(configPath))
        {
            string json = File.ReadAllText(configPath);
            var config = JsonDocument.Parse(json);
            if (config.RootElement.TryGetProperty("InstructorEarlyWindow", out var value))
            {
                return value.GetInt32();
            }
        }
    }
    catch { }
    return 15; // Default
}
```

### 2. DatabaseManager.cs - Update Instructor Schedule Query  

**Location:** Line ~1255 in `InternalTryRecordAttendance` method

**Current query:**

```sql
TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME
```

**Update to:**

```sql
TIME(NOW()) BETWEEN SUBTIME(cs.STARTTIME, '00:@earlyWindow:00') AND cs.ENDTIME
```

This allows instructors to scan from `(STARTTIME - InstructorEarlyWindow)` to `ENDTIME`.

### 3. Load Configuration at Runtime

Call `LoadInstructorEarlyWindow()` when validating instructor schedule to get current configured value.

## Expected Behavior After Fix

**Scenario:** Instructor Early Arrival

- Configured window: 15 minutes
- Class schedule: 13:35 PM - 11:59 PM  
- Instructor scans at: 13:28 PM (7 minutes early)
- System checks: Is 13:28 within (13:35 - 15 min = 13:20) to 11:59?
- Result: ✅ **Allowed** - Instructor can start session
- Action: Start attendance session normally

**Scenario:** Too Early

- Configured window: 15 minutes
- Class schedule: 13:35 PM - 11:59 PM
- Instructor scans at: 13:10 PM (25 minutes early)  
- System checks: Is 13:10 within (13:20) to 11:59?
- Result: ❌ **Denied** - Outside early window

## Implementation Steps

1. Add `using System.Text.Json` to DatabaseManager.cs imports
2. Add `LoadInstructorEarlyWindow()` method
3. Update instructor schedule validation query to use early window
4. Test with current scenario (7 minutes early, should allow)

### To-dos

- [ ] Add early arrival window detection logic to ProcessStudentScan outside sensor in MainForm.cs
- [ ] Create backend API endpoint for recording early arrival scans with 'Awaiting Confirmation' status
- [ ] Update inside scanner confirmation logic to handle 'Awaiting Confirmation' status
- [ ] Create earlyArrivalService.js for auto-confirming attendance when instructor doesn't show
- [ ] Add student_early_arrival_window setting to database and settings API
- [ ] Add 'Awaiting Confirmation' status display to AttendanceLogs.jsx with blue styling
- [ ] Register auto-confirmation service as scheduled job in server.js