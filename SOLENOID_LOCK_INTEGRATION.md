# Solenoid Lock Integration Guide

## Overview
This guide explains how to integrate a solenoid lock with the IoT Fingerprint Attendance System. The system will automatically open the lock when an instructor or admin scans their fingerprint.

## Hardware Requirements

### Components Needed
1. **ESP32 Development Board**
2. **R307 Fingerprint Sensor**
3. **5V Relay Module** (for controlling 12V solenoid)
4. **12V Solenoid Lock** (normally closed type recommended)
5. **12V Power Supply** (for solenoid lock)
6. **Breadboard and Jumper Wires**
7. **Diode** (1N4007 or similar for relay protection)

### Wiring Diagram

```
ESP32 Pin Connections:
├── GPIO 21 → R307 TX (Fingerprint Sensor)
├── GPIO 22 → R307 RX (Fingerprint Sensor)
├── GPIO 5  → Relay Module IN (Solenoid Control)
├── GPIO 2  → Built-in LED (WiFi Status)
├── GPIO 4  → Green LED (Success Indicator)
├── GPIO 5  → Red LED (Error Indicator)
├── GPIO 18 → Buzzer
├── 3.3V    → R307 VCC
├── GND     → R307 GND, Relay GND

Relay Module Connections:
├── VCC → ESP32 5V
├── GND → ESP32 GND
├── IN  → ESP32 GPIO 5
├── COM → 12V Power Supply +
├── NO  → Solenoid Lock +
└── NC  → Not Connected

Solenoid Lock:
├── + → Relay NO terminal
└── - → 12V Power Supply -

12V Power Supply:
├── + → Relay COM terminal
└── - → Solenoid Lock -
```

## Software Features

### Automatic Lock Control
- **Instructors**: Lock opens automatically when fingerprint is scanned
- **Admins**: Lock opens automatically when fingerprint is scanned
- **Students**: No lock access (attendance only)
- **Unknown Users**: No lock access

### Lock Behavior
- **Duration**: Lock stays open for 3 seconds (configurable)
- **Auto-close**: Lock automatically closes after duration
- **Safety**: Lock closes if system loses power

### Manual Control Commands
Via Serial Monitor (115200 baud):
- `lock_open` - Manually open lock
- `lock_close` - Manually close lock
- `lock_test` - Test lock for 2 seconds

## Installation Steps

### 1. Hardware Setup
1. Connect ESP32 to R307 fingerprint sensor (GPIO 21/22)
2. Connect relay module to ESP32 (GPIO 5)
3. Connect 12V power supply to relay and solenoid
4. Test relay operation with manual commands

### 2. Software Setup
1. Upload the modified `iot_fingerprint_attendance.ino` to ESP32
2. Open Serial Monitor (115200 baud)
3. Test fingerprint sensor: Type `test`
4. Test solenoid lock: Type `lock_test`

### 3. Database Setup
1. Ensure instructor users have `USERTYPE = 'instructor'` in database
2. Assign fingerprint IDs to instructors via web interface
3. Enroll instructor fingerprints on ESP32

### 4. Testing
1. **Test with Student**: Should record attendance, no lock access
2. **Test with Instructor**: Should record attendance + open lock
3. **Test with Admin**: Should record attendance + open lock

## Configuration

### Lock Duration
To change how long the lock stays open, modify this line in the code:
```cpp
#define LOCK_DURATION 3000  // 3 seconds (3000ms)
```

### Relay Pin
To use a different GPIO pin for the relay:
```cpp
#define RELAY_PIN 5  // Change to desired pin
```

### Relay Logic
If your relay module works with inverted logic:
```cpp
// In openLock() function:
digitalWrite(RELAY_PIN, LOW);  // Instead of HIGH

// In closeLock() function:
digitalWrite(RELAY_PIN, HIGH); // Instead of LOW
```

## Troubleshooting

### Lock Not Opening
1. **Check Power**: Ensure 12V power supply is connected and working
2. **Check Wiring**: Verify relay connections to ESP32 and solenoid
3. **Test Relay**: Use `lock_test` command to test relay operation
4. **Check Logic**: Verify relay logic (HIGH/LOW) matches your module

### Lock Opening for Students
1. **Check Database**: Verify user has correct `USERTYPE` in database
2. **Check Response**: Monitor Serial output for user type detection
3. **Check Fingerprint**: Ensure correct fingerprint ID is assigned

### Lock Not Closing
1. **Check Duration**: Verify `LOCK_DURATION` setting
2. **Check Power**: Ensure ESP32 maintains power during operation
3. **Check Wiring**: Verify solenoid is properly connected to relay

## Safety Considerations

### Electrical Safety
- Use appropriate wire gauge for 12V solenoid
- Ensure proper grounding
- Use fuse protection for 12V circuit
- Keep 12V and 3.3V circuits separate

### Mechanical Safety
- Ensure solenoid stroke length is appropriate for your lock
- Test lock mechanism manually before powering
- Consider emergency override options
- Regular maintenance of mechanical components

### System Safety
- Lock closes automatically on power loss
- Manual override commands available
- System logs all access attempts
- Backup power considerations for critical applications

## API Response Format

The system expects this response format from the server:
```json
{
  "message": "Attendance recorded successfully",
  "attendance": {
    "user": {
      "type": "instructor"  // This field controls lock access
    }
  }
}
```

## Advanced Features

### Custom Lock Logic
You can modify the lock control logic in the `sendAttendanceData()` function:
```cpp
// Example: Only open lock during certain hours
if (response.indexOf("\"type\":\"instructor\"") != -1) {
  int currentHour = getCurrentHour();
  if (currentHour >= 8 && currentHour <= 18) {
    openLock();
  } else {
    Serial.println("🔒 Outside access hours - No lock access");
  }
}
```

### Multiple Lock Support
For systems with multiple doors, you can add additional relay pins:
```cpp
#define LOCK_PIN_1 5   // Main entrance
#define LOCK_PIN_2 6   // Side entrance
#define LOCK_PIN_3 7   // Emergency exit
```

## Support

For issues or questions:
1. Check Serial Monitor output for error messages
2. Verify all connections and power supplies
3. Test individual components separately
4. Review this documentation for troubleshooting steps






























