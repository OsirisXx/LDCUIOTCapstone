# Quick Setup Guide - Door Lock Integration

## ✅ Build Status
**C# System**: Build succeeded with 0 errors

## 🚀 Quick Start

### 1. Start Backend Server

```bash
cd backend
npm install  # If not already installed
npm start
```

Expected output:
```
🚀 IoT Attendance System API Server running on port 5000
📊 Environment: development
🔗 Health check: http://localhost:5000/api/health
📡 UDP Discovery Server running on port 8888
```

### 2. Register ESP32 Device

Make sure your ESP32 is registered in the DEVICES table:

```sql
INSERT INTO DEVICES (
    DEVICEID, DEVICETYPE, IPADDRESS, PORT, 
    LOCATION, ROOMNUMBER, STATUS, LASTSEEN
) VALUES (
    'ESP32_LOCK_001',           -- Unique device ID
    'ESP32_Lock_Controller',    -- Device type
    '192.168.1.100',           -- ESP32 IP address
    80,                         -- Port (ESP32 web server)
    'outside',                  -- Location
    '101',                      -- Room number
    'online',                   -- Status
    NOW()                       -- Last seen
);
```

### 3. Upload Arduino Code to ESP32

1. Open Arduino IDE
2. Open `sketch_feb18a_copy_20250803144501/iot_fingerprint_attendance/iot_fingerprint_attendance.ino`
3. Set WiFi credentials:
   ```cpp
   const char* ssid = "YOUR_WIFI_SSID";
   const char* password = "YOUR_WIFI_PASSWORD";
   ```
4. Upload to ESP32
5. Open Serial Monitor (115200 baud)
6. Note the ESP32 IP address

### 4. Run C# Fingerprint/RFID System

1. Open `FutronicAttendanceSystem.sln` in Visual Studio or:
   ```bash
   cd FutronicAttendanceSystem
   dotnet run
   ```
2. Select a room from dropdown
3. Ensure room number matches the DEVICES table entry

### 5. Configure Backend URL

In `FutronicAttendanceSystem/appsettings.json`:
```json
{
  "Backend": {
    "BaseUrl": "http://localhost:5000"
  },
  "Device": {
    "DeviceId": "FINGERPRINT_SCANNER_01",
    "Location": "inside",
    "RoomNumber": "101"
  }
}
```

## 🧪 Testing

### Test 1: Backend Health Check

```bash
curl http://localhost:5000/api/health
```

Expected response:
```json
{
  "status": "OK",
  "timestamp": "2025-01-15T10:00:00.000Z",
  "uptime": 123.45,
  "environment": "development",
  "system": "IoT Attendance System"
}
```

### Test 2: ESP32 Lock Control

```bash
curl -X POST http://192.168.1.100/api/lock-control \
  -H "Content-Type: application/json" \
  -d '{"action":"open","user":"Test User"}'
```

Expected response:
```json
{
  "message": "Lock opened successfully",
  "action": "open",
  "user": "Test User"
}
```

### Test 3: Complete Flow

#### 3A. Instructor Check-In

1. **Setup:**
   - Ensure instructor has a schedule in Room 101
   - Schedule should be active now (±15 minutes)
   - No active session exists

2. **Action:**
   - Instructor scans fingerprint/RFID

3. **Expected Results:**
   - ✅ C# console shows: "Attendance recorded successfully"
   - ✅ C# console shows: "🔓 Door unlocked for instructor"
   - ✅ C# status bar shows: "🔓 Door unlocked"
   - ✅ ESP32 serial monitor shows: "🔓 Opening solenoid lock..."
   - ✅ ESP32 relay activates (lock opens for 3 seconds)
   - ✅ Database SESSIONS table: New row with STATUS='active'
   - ✅ Database ATTENDANCERECORDS: New row for instructor
   - ✅ Database ACCESSLOGS: New row with ACCESSTYPE='door_unlock', RESULT='success'

#### 3B. Student Check-In (After Session Started)

1. **Setup:**
   - Session is active (instructor checked in)
   - Student is enrolled in the subject

2. **Action:**
   - Student scans fingerprint inside room

3. **Expected Results:**
   - ✅ C# console shows: "Attendance recorded successfully"
   - ❌ NO lock control messages (students don't control lock)
   - ✅ Database ATTENDANCERECORDS: New row with STATUS='Present' or 'Late'
   - ✅ No entry in ACCESSLOGS (students don't access door)

#### 3C. Instructor Check-Out

1. **Setup:**
   - Active session exists
   - Instructor scans fingerprint/RFID

2. **Action:**
   - Instructor scans fingerprint/RFID

3. **Expected Results:**
   - ✅ C# console shows: "Attendance recorded successfully"
   - ✅ C# console shows: "🔒 Door locked by instructor"
   - ✅ C# status bar shows: "🔒 Door locked"
   - ✅ ESP32 serial monitor shows: "🔒 Closing solenoid lock..."
   - ✅ ESP32 relay deactivates (lock closes)
   - ✅ Database SESSIONS table: STATUS='ended', ENDTIME updated
   - ✅ Database ATTENDANCERECORDS: New row for instructor time_out
   - ✅ Database ACCESSLOGS: New row with ACCESSTYPE='door_lock', RESULT='success'

## 📊 Monitoring

### Backend Logs
Watch for these messages:
```
Sending lock command to http://192.168.1.100/api/lock-control
Lock control request processed successfully
```

### C# Console Logs
Watch for these messages:
```
Recording attendance for user {guid}, action Check In
Attendance recorded successfully
🔓 Door unlocked for instructor
Lock action: open
```

### ESP32 Serial Monitor
Watch for these messages:
```
🌐 Web Server Status:
  IP Address: 192.168.1.100
  Port: 80
🔓 Lock control request received: {"action":"open","user":"John Doe"}
🔓 Opening solenoid lock...
✅ Lock opened - Relay activated
🔒 Closing solenoid lock...
✅ Lock closed - Relay deactivated
```

### Database Queries

**Check Active Sessions:**
```sql
SELECT s.*, u.FIRSTNAME, u.LASTNAME, r.ROOMNUMBER
FROM SESSIONS s
JOIN USERS u ON s.INSTRUCTORID = u.USERID
JOIN ROOMS r ON s.ROOMID = r.ROOMID
WHERE s.STATUS = 'active';
```

**Check Recent Access Logs:**
```sql
SELECT l.*, u.FIRSTNAME, u.LASTNAME, r.ROOMNUMBER
FROM ACCESSLOGS l
LEFT JOIN USERS u ON l.USERID = u.USERID
LEFT JOIN ROOMS r ON l.ROOMID = r.ROOMID
ORDER BY l.TIMESTAMP DESC
LIMIT 10;
```

**Check Today's Attendance:**
```sql
SELECT a.*, u.FIRSTNAME, u.LASTNAME, 
       s.SUBJECTCODE, s.SUBJECTNAME
FROM ATTENDANCERECORDS a
JOIN USERS u ON a.USERID = u.USERID
JOIN CLASSSCHEDULES cs ON a.SCHEDULEID = cs.SCHEDULEID
JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
WHERE DATE(a.SCANDATETIME) = CURDATE()
ORDER BY a.SCANDATETIME DESC;
```

## 🔧 Troubleshooting

### Issue: "No room selected - skipping lock control"

**Cause:** Room not selected in C# application

**Solution:**
1. Open C# application
2. Select room from dropdown
3. Room must be configured in database

### Issue: "No lock controller found for this room"

**Cause:** ESP32 device not registered or offline

**Solution:**
1. Check DEVICES table for ESP32 entry
2. Verify ROOMNUMBER matches
3. Check STATUS is 'online'
4. Ping ESP32 IP address
5. Update LASTSEEN timestamp

### Issue: "No scheduled class at this time"

**Cause:** Instructor doesn't have schedule in room

**Solution:**
1. Check CLASSSCHEDULES table
2. Verify day of week matches
3. Verify time is within ±15 minutes
4. Verify ACADEMICYEAR and SEMESTER match settings

### Issue: Lock doesn't activate

**Cause:** Multiple possible causes

**Solutions:**
1. **Check ESP32 connection:**
   ```bash
   curl http://192.168.1.100/api/health
   ```

2. **Check relay wiring:**
   - Relay control pin: GPIO 5
   - Verify relay module power supply
   - Check relay LED indicator

3. **Check backend API:**
   ```bash
   curl -X POST http://localhost:5000/api/lock-control/request \
     -H "Content-Type: application/json" \
     -d '{
       "user_id": "instructor-uuid",
       "room_id": "room-uuid",
       "auth_method": "fingerprint",
       "action": "check_in"
     }'
   ```

4. **Check C# logs:**
   - Look for "Lock control request failed" messages
   - Verify backend URL is correct

### Issue: Students can't record attendance

**Cause:** No active session

**Solution:**
1. Instructor must check in first
2. Verify session is active:
   ```sql
   SELECT * FROM SESSIONS 
   WHERE STATUS = 'active' 
   AND SESSIONDATE = CURDATE();
   ```

## 📋 Pre-Flight Checklist

Before testing the complete system:

- [ ] Backend server running on port 5000
- [ ] MySQL database accessible
- [ ] ESP32 uploaded with latest code
- [ ] ESP32 connected to WiFi
- [ ] ESP32 IP address noted
- [ ] ESP32 registered in DEVICES table
- [ ] Room configured with matching ROOMNUMBER
- [ ] Instructor has schedule in database
- [ ] C# application built successfully (0 errors)
- [ ] C# application running
- [ ] Room selected in C# application
- [ ] Fingerprint scanner connected
- [ ] RFID reader connected (if using RFID)
- [ ] Solenoid lock connected to relay
- [ ] Relay connected to ESP32 GPIO 5

## 🎯 Next Steps

1. Test with actual instructor and student fingerprints/RFID
2. Monitor all logs during testing
3. Verify attendance records in database
4. Verify session management works correctly
5. Test edge cases (late arrival, early arrival, etc.)
6. Document any issues or unexpected behavior

## 📞 Support

Check these files for detailed information:
- `DOOR_LOCK_INTEGRATION.md` - Complete system architecture
- Backend console logs
- C# console output
- ESP32 serial monitor
- Database ACCESSLOGS table

---

**Status:** ✅ System Ready for Testing
**Build:** ✅ Successful (0 errors, 8 warnings)
**Date:** 2025-01-15

