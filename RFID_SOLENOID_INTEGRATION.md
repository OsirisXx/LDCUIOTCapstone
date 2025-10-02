# RFID Scanner + Solenoid Lock Integration Guide

## Overview
This guide explains how to integrate your USB-based RFID scanner with the solenoid lock control system. The RFID scanner is connected to your PC and communicates with the ESP32 to control the door lock.

## System Architecture

```
USB RFID Scanner → PC (Web Interface) → Backend Server → ESP32 → Solenoid Lock
```

### Components:
1. **USB RFID Scanner** - Connected to PC
2. **Web Interface** - `http://localhost:3000/rfid-scanner`
3. **Backend Server** - Processes RFID scans and controls lock
4. **ESP32** - Receives commands and controls solenoid
5. **Solenoid Lock** - Physical door lock mechanism

## Features

### 🔓 **Automatic Lock Control**
- **Instructors**: RFID scan opens solenoid lock automatically
- **Admins**: RFID scan opens solenoid lock automatically
- **Students**: RFID scan records attendance only (no lock access)

### 📊 **Real-time Feedback**
- Visual indicators for lock status
- Success/error messages
- Scan history with lock control status

### 🔐 **Security Features**
- Role-based access control
- Comprehensive logging
- Authentication required for lock control

## Setup Instructions

### 1. **Hardware Setup**
```
ESP32 Pin Connections:
├── GPIO 5  → Relay Module IN (Solenoid Control)
├── GPIO 21 → R307 TX (Fingerprint Sensor)
├── GPIO 22 → R307 RX (Fingerprint Sensor)
├── GPIO 2  → Built-in LED (WiFi Status)
├── GPIO 4  → Green LED (Success Indicator)
├── GPIO 5  → Red LED (Error Indicator)
├── GPIO 18 → Buzzer
├── 3.3V    → R307 VCC
└── GND     → R307 GND, Relay GND

Relay Module:
├── VCC → ESP32 5V
├── GND → ESP32 GND
├── IN  → ESP32 GPIO 5
├── COM → 12V Power Supply +
├── NO  → Solenoid Lock +
└── NC  → Not Connected

Solenoid Lock:
├── + → Relay NO terminal
└── - → 12V Power Supply -
```

### 2. **Software Setup**

#### **Backend Server**
The backend now includes a lock control endpoint:
```javascript
POST /api/devices/trigger-lock
{
  "action": "open|close",
  "user": "user_name",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

#### **ESP32 Web Server**
The ESP32 now runs a web server on port 80 with endpoints:
- `POST /api/lock-control` - Control solenoid lock
- `GET /api/health` - Health check

### 3. **Configuration**

#### **Environment Variables**
Add to your `.env` file:
```env
ESP32_IP=192.168.1.12  # Your ESP32's IP address
```

#### **ESP32 Settings**
The ESP32 automatically:
- Starts web server on port 80
- Listens for lock control commands
- Logs all lock operations

## Usage Instructions

### **For RFID Scanning with Lock Control:**

1. **Open RFID Scanner Page**
   - Go to `http://localhost:3000/rfid-scanner`
   - Keep the page focused and active

2. **Scan RFID Card**
   - Place RFID card near reader
   - System automatically detects and processes scan

3. **Automatic Lock Control**
   - **Instructors/Admins**: Lock opens automatically
   - **Students**: Attendance recorded only
   - Visual feedback shows lock status

### **Testing Commands**

#### **ESP32 Serial Commands:**
```
webstatus    - Show web server status and endpoints
lock_test    - Test solenoid lock (2 seconds)
lock_open    - Manually open lock
lock_close   - Manually close lock
testserver   - Test backend server connection
```

#### **Manual Testing:**
```bash
# Test ESP32 web server
curl -X GET http://192.168.1.12/api/health

# Test lock control
curl -X POST http://192.168.1.12/api/lock-control \
  -H "Content-Type: application/json" \
  -d '{"action":"open","user":"test_user"}'
```

## API Endpoints

### **Backend Lock Control**
```http
POST /api/devices/trigger-lock
Authorization: Bearer <token>
Content-Type: application/json

{
  "action": "open",
  "user": "John Doe",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### **ESP32 Lock Control**
```http
POST http://<esp32-ip>/api/lock-control
Content-Type: application/json

{
  "action": "open|close",
  "user": "user_name"
}
```

## Troubleshooting

### **Lock Not Opening**
1. **Check ESP32 Connection**
   - Verify ESP32 is connected to WiFi
   - Check IP address with `webstatus` command
   - Test with manual lock commands

2. **Check Network Communication**
   - Ensure PC and ESP32 are on same network
   - Test ESP32 web server: `curl http://192.168.1.12/api/health`
   - Check firewall settings

3. **Check Hardware**
   - Verify relay connections
   - Check 12V power supply
   - Test solenoid manually

### **RFID Not Detected**
1. **Check Web Page Focus**
   - Ensure RFID scanner page is active
   - Check browser console for errors
   - Verify USB RFID reader is connected

2. **Check Backend Server**
   - Ensure server is running
   - Check authentication token
   - Verify user has proper permissions

### **ESP32 Web Server Issues**
1. **Check Serial Output**
   - Look for web server startup messages
   - Check for error messages
   - Verify endpoint registration

2. **Test Web Server**
   - Use `webstatus` command
   - Test with curl commands
   - Check network connectivity

## Security Considerations

### **Access Control**
- Only authenticated users can trigger lock
- Role-based permissions (instructor/admin only)
- All lock operations are logged

### **Network Security**
- ESP32 web server on local network only
- Backend authentication required
- HTTPS recommended for production

### **Physical Security**
- Lock closes automatically after timeout
- Manual override available
- Power loss protection

## Monitoring and Logging

### **Access Logs**
All lock operations are logged in the database:
- User who triggered lock
- Timestamp of operation
- Success/failure status
- Error messages if applicable

### **Real-time Monitoring**
- Web interface shows lock status
- Serial monitor shows ESP32 operations
- Backend logs show server operations

## Advanced Features

### **Custom Lock Logic**
You can modify the lock control logic in the backend:
```javascript
// Example: Only open lock during certain hours
if (user.type === 'instructor') {
  const currentHour = new Date().getHours();
  if (currentHour >= 8 && currentHour <= 18) {
    await triggerSolenoidLock(user.name);
  }
}
```

### **Multiple Lock Support**
For systems with multiple doors:
```javascript
// Add room-specific lock control
await triggerSolenoidLock(user.name, roomId);
```

## Support

For issues or questions:
1. Check Serial Monitor for ESP32 errors
2. Check browser console for web interface errors
3. Check backend logs for server errors
4. Test individual components separately
5. Review this documentation for troubleshooting steps





























