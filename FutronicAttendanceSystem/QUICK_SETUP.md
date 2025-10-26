# ⚡ Quick Setup Guide - Dual Sensor System

## 🚀 5-Minute Setup

### Step 1: Connect Hardware (1 min)
```
✓ Plug in Fingerprint Sensor #1 (Front USB port)
✓ Plug in Fingerprint Sensor #2 (Back USB port)
✓ Wait for Windows to recognize devices
```

### Step 2: Launch Application (1 min)
```
✓ Double-click FutronicAttendanceSystem.exe
✓ Wait for configuration dialog to appear
```

### Step 3: Configure (2 min)
```
✓ Select Room: [Room 101 ▼]
✓ Inside Sensor: [Sensor #1 ▼]
✓ Outside Sensor: [Sensor #2 ▼]
✓ ☑ Remember this configuration
✓ ☐ Enable Test Mode (only if testing with 1 sensor)
✓ Click "Connect & Start"
```

### Step 4: Verify (1 min)
```
✓ Check both panels show "● Active"
✓ Scan a test fingerprint on inside sensor
✓ Check activity feed shows the scan
✓ Done! System is ready.
```

---

## 🎯 Quick Reference

### Panel Status Indicators
| Indicator | Meaning |
|-----------|---------|
| ● Active (Green) | Sensor working normally |
| ● Disabled (Red) | Sensor turned off by admin |
| ● Scanning... (Yellow) | Fingerprint being read |
| ● Error (Red) | Problem with sensor |

### Admin Controls
| Control | Action |
|---------|--------|
| Enable Sensor Checkbox | Turn sensor on/off |
| Change Config Button | Reconfigure room/sensors |
| Test Scan Button | Simulate a fingerprint scan |

### Activity Feed Icons
| Icon | Meaning |
|------|---------|
| 🟢 | Inside sensor scan |
| 🔴 | Outside sensor scan |
| ✓ | Successful scan |
| ✗ | Failed scan |

---

## 🛠️ Troubleshooting (30 seconds each)

### No Sensors Detected
```
1. Check USB cables
2. Try different USB ports
3. Restart application
```

### Sensor Not Working
```
1. Uncheck "Enable Sensor"
2. Wait 3 seconds
3. Check "Enable Sensor" again
```

### Both Using Same Sensor
```
1. Enable "Test Mode" (if testing)
   OR
2. Plug in second physical sensor
3. Click "Change Config"
4. Reassign sensors
```

### Can't See Rooms
```
1. Check database connection in appsettings.json
2. Verify MySQL server running
3. Test web frontend connection
4. Restart application
```

---

## 📝 Configuration File

**Location:** `device_config.json` (next to .exe)

**Sample:**
```json
{
  "roomId": "abc-123-def-456",
  "roomName": "Room 101",
  "building": "Main Building",
  "insideSensor": {
    "deviceId": "Room101_Inside",
    "enabled": true,
    "sensorIndex": 0
  },
  "outsideSensor": {
    "deviceId": "Room101_Outside",
    "enabled": true,
    "sensorIndex": 1
  },
  "testMode": false
}
```

**To Reconfigure:**
- Delete this file and restart app
- OR click "Change Config" button

---

## 🎓 Testing with 1 Sensor

### Enable Test Mode
```
1. Launch application
2. In config dialog, check "☑ Enable Test Mode"
3. Both sensors will use same physical device
4. Use "Test Scan" buttons to simulate activity
```

### Switching Between Sensors
```
Test mode allows same physical sensor to record 
as either "inside" or "outside" based on which
panel you interact with.
```

---

## 📊 Verifying Database Records

### Check Attendance Table
```sql
SELECT 
    SCANDATETIME, 
    LOCATION, 
    SCANTYPE 
FROM ATTENDANCERECORDS 
ORDER BY SCANDATETIME DESC 
LIMIT 10;
```

**Expected:**
- `LOCATION` should show 'inside' or 'outside'
- `SCANTYPE` should show 'time_in' or 'time_out'
- Recent scans should appear immediately

### Check Device Registration
```sql
SELECT 
    DEVICENAME, 
    LOCATION, 
    STATUS 
FROM DEVICES 
WHERE DEVICETYPE = 'Fingerprint_Scanner'
ORDER BY CREATED_AT DESC;
```

**Expected:**
- Two devices per room: `RoomXXX_Inside` and `RoomXXX_Outside`
- `LOCATION` field: 'inside' or 'outside'
- `STATUS`: 'Active'

---

## ⚙️ Windows Startup (Optional)

### Auto-Launch on Boot

**Create Shortcut:**
1. Right-click `FutronicAttendanceSystem.exe`
2. Select "Create Shortcut"
3. Press `Win+R` and type `shell:startup`
4. Move shortcut to Startup folder

**System will:**
- Launch on Windows boot
- Load saved configuration
- Start both sensors automatically
- No interaction needed

---

## 🔐 Security Best Practices

✓ **Physical Security:** Keep PC in secure room  
✓ **Access Control:** Only authorized personnel can disable sensors  
✓ **Configuration Lock:** Use "Remember configuration" to prevent accidental changes  
✓ **Regular Monitoring:** Check activity feed periodically  
✓ **Backup Config:** Keep copy of `device_config.json`  

---

## 💡 Pro Tips

### Sensor Labels
```
Physically label your sensors:
- Sensor #1 → Stick "INSIDE" label
- Sensor #2 → Stick "OUTSIDE" label
Prevents confusion during maintenance.
```

### USB Port Memory
```
Keep sensors in same USB ports:
- Sensor #1 always in front port
- Sensor #2 always in back port
Windows remembers device-to-port mapping.
```

### Activity Feed Monitoring
```
Keep activity feed visible:
- Shows real-time system health
- Catch errors immediately
- Verify attendance recording
- Audit trail for troubleshooting
```

### Test Daily
```
Each morning:
1. Scan test finger on inside sensor
2. Scan test finger on outside sensor
3. Check activity feed shows both
4. Ready for the day!
```

---

## 📞 Quick Support

**Most Common Issues:**

| Issue | Fix |
|-------|-----|
| Gray screen | Database connection issue |
| One sensor works | Check USB on non-working sensor |
| Both sensors same | Enable test mode or add 2nd sensor |
| No room list | Database not accessible |
| Config resets | File permissions issue |

**Always check console output first!**
- Errors show as ❌ with red text
- Success shows as ✅ with green text
- Warnings show as ⚠️ with yellow text

---

## ✅ Success Checklist

Before going live:

```
☐ Both sensors physically installed
☐ Sensors labeled (inside/outside)
☐ Configuration completed
☐ Test scans successful on both
☐ Activity feed updating
☐ Database records visible
☐ Web frontend shows devices
☐ Staff trained on usage
☐ Backup of config file created
☐ Support contact info posted
```

---

**Remember:** The system is designed to "just work". If you see both panels showing "● Active", you're ready to go!

For detailed information, see `DUAL_SENSOR_GUIDE.md`





