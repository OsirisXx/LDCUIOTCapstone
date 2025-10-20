# Troubleshooting: Relay Not Switching

## Problem
Instructor attendance is recorded successfully, but the relay/solenoid lock is not activating.

## Diagnostic Steps

### Step 1: Check C# Console Output

When instructor scans, you should see:

```
=== LOCK CONTROL REQUEST START ===
User GUID: {some-guid}
Action: Check In
Room ID: {room-guid}
Room Number: 101
Action Type: check_in
Auth Method: fingerprint
Payload: {"user_id":"...","room_id":"...","auth_method":"fingerprint","action":"check_in"}
Sending to: http://localhost:5000/api/lock-control/request
Response Status: OK
Response Body: {...}
Lock Action: open
Device Available: True
Command Sent to ESP32: True
ESP32 Result: {...}
🔓 Door unlocked for instructor
=== LOCK CONTROL REQUEST END ===
```

**If you DON'T see this output:**
- The `RequestLockControl()` method is not being called
- Check if attendance was recorded successfully
- Check if user is an instructor (not student)

**If you see "❌ No room selected":**
- Select a room in the C# application dropdown
- Make sure a room is selected before scanning

**If you see "Response Status: NotFound" or other error:**
- Backend server is not running
- Backend route is not registered
- Check backend console for errors

### Step 2: Check Backend Console Output

Backend should show:

```
POST /api/lock-control/request
Instructor: John Doe
Schedule: CS101 - Computer Science
Lock Action: open
Sending lock command to http://192.168.1.100/api/lock-control
Lock command sent successfully
```

**If you DON'T see this:**
- Backend server not running: `cd backend && npm start`
- Route not registered: Check `backend/server.js` line 42

**If you see "No lock controller found for this room":**
- ESP32 not registered in DEVICES table
- See Step 3 below

**If you see "No scheduled class at this time":**
- Instructor doesn't have a schedule now
- Check CLASSSCHEDULES table
- See Step 4 below

### Step 3: Check ESP32 Device Registration

**Query the database:**

```sql
SELECT * FROM DEVICES 
WHERE DEVICETYPE = 'ESP32_Lock_Controller';
```

**Expected result:**
```
DEVICEID: ESP32_LOCK_001
DEVICETYPE: ESP32_Lock_Controller
IPADDRESS: 192.168.1.100
PORT: 80
LOCATION: outside
ROOMNUMBER: 101
STATUS: online
LASTSEEN: (recent timestamp)
```

**If no results:**

```sql
INSERT INTO DEVICES (
    DEVICEID, DEVICETYPE, IPADDRESS, PORT, 
    LOCATION, ROOMNUMBER, STATUS, LASTSEEN
) VALUES (
    'ESP32_LOCK_001',
    'ESP32_Lock_Controller',
    '192.168.1.100',  -- CHANGE TO YOUR ESP32 IP
    80,
    'outside',
    '101',  -- MUST MATCH ROOM NUMBER
    'online',
    NOW()
);
```

**CRITICAL:** The `ROOMNUMBER` in DEVICES must match the `ROOMNUMBER` in ROOMS table!

**Check room number:**

```sql
SELECT ROOMID, ROOMNUMBER, ROOMNAME 
FROM ROOMS 
WHERE ROOMID = 'your-room-guid';
```

### Step 4: Check Instructor Schedule

**Query the database:**

```sql
SELECT 
    cs.SCHEDULEID,
    cs.DAYOFWEEK,
    cs.STARTTIME,
    cs.ENDTIME,
    sub.SUBJECTNAME,
    sub.SUBJECTCODE,
    r.ROOMNUMBER,
    u.FIRSTNAME,
    u.LASTNAME
FROM CLASSSCHEDULES cs
JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
JOIN ROOMS r ON cs.ROOMID = r.ROOMID
JOIN USERS u ON sub.INSTRUCTORID = u.USERID
WHERE u.USERID = 'instructor-guid'  -- Replace with your instructor GUID
AND cs.DAYOFWEEK = 'Monday'  -- Replace with current day
AND cs.ROOMID = 'room-guid';  -- Replace with your room GUID
```

**Requirements:**
- `DAYOFWEEK` must match current day (Monday, Tuesday, etc.)
- `STARTTIME` must be within ±15 minutes of current time
- `ENDTIME` must be after current time
- `ACADEMICYEAR` and `SEMESTER` must match current settings

**If no results, create a schedule:**

```sql
INSERT INTO CLASSSCHEDULES (
    SCHEDULEID, SUBJECTID, ROOMID, 
    DAYOFWEEK, STARTTIME, ENDTIME,
    ACADEMICYEAR, SEMESTER
) VALUES (
    UUID(),
    'subject-guid',  -- Subject taught by instructor
    'room-guid',     -- Room with ESP32
    'Monday',        -- Current day
    '08:00:00',      -- Start time
    '10:00:00',      -- End time
    (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'),
    (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')
);
```

### Step 5: Check ESP32 Connection

**Test ESP32 health endpoint:**

```bash
curl http://192.168.1.100/api/health
```

**Expected response:**
```json
{
  "status": "OK",
  "device": "ESP32 Solenoid Lock Controller",
  "ip": "192.168.1.100",
  "uptime": 123456
}
```

**If no response:**
- ESP32 not powered on
- ESP32 not connected to WiFi
- Wrong IP address
- Firewall blocking connection

**Check ESP32 serial monitor:**

```
=== IoT Solenoid Lock Control System ===
✓ WiFi connected!
IP address: 192.168.1.100
🔧 Solenoid lock initialized on pin 5
🌐 Web server started on port 80
System ready! Solenoid lock controller initialized.
```

**If WiFi not connected:**
- Check WiFi credentials in Arduino code
- Make sure using 2.4GHz network (ESP32 doesn't support 5GHz)
- Check WiFi signal strength

### Step 6: Test ESP32 Lock Control Directly

**Send command directly to ESP32:**

```bash
curl -X POST http://192.168.1.100/api/lock-control \
  -H "Content-Type: application/json" \
  -d '{"action":"open","user":"Test User"}'
```

**Expected ESP32 serial output:**
```
🔓 Lock control request received: {"action":"open","user":"Test User"}
🔓 Opening solenoid lock...
✅ Lock opened - Relay activated
🔒 Closing solenoid lock...
✅ Lock closed - Relay deactivated
```

**If relay doesn't activate:**
- Check relay wiring (GPIO 5)
- Check relay power supply
- Check relay module LED indicator
- Test with manual command: Type `lock_test` in serial monitor

### Step 7: Check Relay Hardware

**Test relay manually via serial:**

1. Open Arduino Serial Monitor (115200 baud)
2. Type: `lock_test`
3. Press Enter

**Expected output:**
```
🧪 Testing solenoid lock...
Opening lock for 2 seconds...
✅ Lock test complete
```

**Check:**
- Relay module LED should light up
- You should hear relay click
- Solenoid lock should activate

**If relay doesn't activate:**

1. **Check wiring:**
   - ESP32 GPIO 5 → Relay IN
   - ESP32 GND → Relay GND
   - ESP32 3.3V or 5V → Relay VCC

2. **Check relay module:**
   - Some relays are active LOW (activate when GPIO is LOW)
   - Some relays are active HIGH (activate when GPIO is HIGH)
   - Try changing in code:
     ```cpp
     // If relay is active LOW:
     digitalWrite(RELAY_PIN, LOW);  // Activate
     digitalWrite(RELAY_PIN, HIGH); // Deactivate
     ```

3. **Check power supply:**
   - Relay module needs sufficient power
   - ESP32 3.3V might not be enough for some relays
   - Use external 5V power supply

4. **Check solenoid lock:**
   - Solenoid needs 12V typically
   - Connect solenoid to relay output (COM and NO terminals)
   - Connect 12V power supply to solenoid through relay

### Step 8: Check Session Status

**Query active sessions:**

```sql
SELECT 
    s.SESSIONID,
    s.STATUS,
    s.SESSIONDATE,
    s.STARTTIME,
    u.FIRSTNAME,
    u.LASTNAME,
    r.ROOMNUMBER
FROM SESSIONS s
JOIN USERS u ON s.INSTRUCTORID = u.USERID
JOIN ROOMS r ON s.ROOMID = r.ROOMID
WHERE s.SESSIONDATE = CURDATE()
AND s.STATUS = 'active';
```

**If session already exists:**
- Lock action will be "none" (already unlocked)
- Instructor needs to check out first to close session
- Then check in again to open lock

**Clear old sessions if needed:**

```sql
UPDATE SESSIONS 
SET STATUS = 'ended', ENDTIME = NOW() 
WHERE STATUS = 'active' 
AND SESSIONDATE < CURDATE();
```

## Common Issues and Solutions

### Issue 1: "No room selected"
**Solution:** Select a room from the dropdown in C# application before scanning.

### Issue 2: "No lock controller found"
**Solution:** Register ESP32 in DEVICES table with matching ROOMNUMBER.

### Issue 3: "No scheduled class at this time"
**Solution:** Create a schedule for the instructor in the room at current time.

### Issue 4: Lock action is "none"
**Causes:**
- Session already active (already unlocked)
- No schedule found
- Wrong day/time

**Solution:** Check schedule and session status.

### Issue 5: ESP32 not responding
**Solutions:**
- Check ESP32 power
- Check WiFi connection
- Check IP address
- Check firewall settings

### Issue 6: Relay not activating
**Solutions:**
- Check wiring (GPIO 5)
- Check relay power supply
- Test with `lock_test` command
- Check if relay is active LOW or HIGH
- Use external power for relay module

### Issue 7: Backend not receiving request
**Solutions:**
- Start backend: `cd backend && npm start`
- Check backend console for errors
- Verify route is registered in server.js
- Check firewall/antivirus

## Complete Test Procedure

1. **Start Backend:**
   ```bash
   cd backend
   npm start
   ```
   Verify: `🚀 IoT Attendance System API Server running on port 5000`

2. **Upload ESP32 Code:**
   - Set WiFi credentials
   - Upload to ESP32
   - Open Serial Monitor
   - Note IP address

3. **Register ESP32:**
   ```sql
   INSERT INTO DEVICES (...) VALUES (...);
   ```

4. **Create Schedule:**
   ```sql
   INSERT INTO CLASSSCHEDULES (...) VALUES (...);
   ```

5. **Start C# Application:**
   - Select room from dropdown
   - Verify room number matches DEVICES table

6. **Test Instructor Scan:**
   - Scan instructor fingerprint/RFID
   - Watch C# console for detailed logs
   - Watch backend console for API calls
   - Watch ESP32 serial for lock commands
   - Verify relay activates

## Debug Checklist

- [ ] Backend server running
- [ ] Backend route registered (`/api/lock-control/request`)
- [ ] ESP32 powered and connected to WiFi
- [ ] ESP32 IP address known
- [ ] ESP32 registered in DEVICES table
- [ ] ROOMNUMBER in DEVICES matches ROOMS table
- [ ] Instructor has schedule at current time
- [ ] Schedule day/time matches current day/time
- [ ] No active session exists (for first check-in)
- [ ] Room selected in C# application
- [ ] User is instructor (not student)
- [ ] Relay wired correctly (GPIO 5)
- [ ] Relay power supply sufficient
- [ ] Solenoid lock connected to relay output
- [ ] C# console shows detailed logs
- [ ] Backend console shows API calls
- [ ] ESP32 serial shows lock commands

## Need Help?

If relay still not working after all checks:

1. **Capture all logs:**
   - C# console output
   - Backend console output
   - ESP32 serial monitor output

2. **Check database:**
   - DEVICES table entry
   - CLASSSCHEDULES for instructor
   - SESSIONS table
   - ROOMS table

3. **Test each component separately:**
   - Test ESP32 lock control directly with curl
   - Test backend API with curl
   - Test C# to backend connection
   - Test relay with serial commands

4. **Verify hardware:**
   - Relay module working
   - Solenoid lock working
   - Power supplies adequate
   - Wiring correct

