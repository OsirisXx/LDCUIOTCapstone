# Lab Demonstration Setup Guide
## IoT Fingerprint Attendance System

This guide will help you set up the complete system for demonstration in a laboratory environment with 4 PCs.

---

## 📋 System Architecture Overview

**Components:**
1. **Backend Server** (Node.js) - Runs on 1 PC (Server PC)
2. **MySQL Database** - Runs on the same Server PC
3. **Frontend (React)** - Accessible from all 4 PCs via browser
4. **Futronic App (C#)** - Runs on all 4 PCs for fingerprint scanning
5. **ESP32 Hardware** - Solenoid lock controller (1 device)

**Network Setup:**
- All PCs must be on the **same local network** (lab WiFi or switch)
- Server PC will host the backend and database
- All devices will connect to the Server PC's IP address

---

## 🖥️ PC Roles

### **Server PC (1 PC)**
- Runs MySQL database
- Runs Node.js backend server (port 5000)
- Can also run Futronic app and access frontend

### **Client PCs (3 PCs)**
- Access frontend via browser
- Run Futronic app for fingerprint scanning
- Connect to Server PC's backend

---

## 📝 Pre-Setup Checklist

### Required Software (Install on ALL PCs):
- [ ] **Node.js** (v16 or higher) - [Download](https://nodejs.org/)
- [ ] **MySQL** (v8.0 or higher) - Only on Server PC
- [ ] **Google Chrome** or **Microsoft Edge** browser
- [ ] **.NET Framework 4.8** - For Futronic app
- [ ] **Git** (optional, for cloning)

### Required Hardware:
- [ ] 4 PCs connected to same network
- [ ] 1 Futronic fingerprint scanner (can be moved between PCs)
- [ ] 1 ESP32 with OLED, relay, and solenoid lock
- [ ] Network router/switch or WiFi access point

---

## 🚀 Step-by-Step Setup

### STEP 1: Prepare the Server PC

#### 1.1 Find Server PC's IP Address

**On Windows:**
```cmd
ipconfig
```
Look for `IPv4 Address` under your active network adapter (e.g., `192.168.1.100`)

**Write down this IP address - you'll need it for all other PCs!**

Example: `192.168.1.100` (replace with your actual IP)

---

#### 1.2 Setup MySQL Database (Server PC Only)

1. **Install MySQL** if not already installed
2. **Start MySQL service**
3. **Create the database:**

```cmd
cd C:\Users\Harley\Desktop\Capstone\attendanceiot\IOTCapstone\backend
npm install
npm run setup-db
```

4. **Set admin password:**
```cmd
npm run set-admin-password
```
Follow prompts to set admin password.

5. **Verify MySQL is accessible:**
   - Open MySQL Workbench or command line
   - Connect to `localhost:3306`
   - Database `iot_attendance` should exist

---

#### 1.3 Configure Backend (Server PC Only)

1. **Create `.env` file** in `backend` folder:

```cmd
cd C:\Users\Harley\Desktop\Capstone\attendanceiot\IOTCapstone\backend
copy env.example .env
```

2. **Edit `.env` file** with these settings:

```env
# Database Configuration
DB_HOST=localhost
DB_PORT=3306
DB_USER=root
DB_PASSWORD=YOUR_MYSQL_PASSWORD
DB_NAME=iot_attendance

# Server Configuration
PORT=5000
NODE_ENV=development

# JWT Configuration
JWT_SECRET=iot_attendance_super_secret_jwt_key_2024
JWT_EXPIRES_IN=7d

# CORS Configuration - IMPORTANT: Use Server PC's IP
CORS_ORIGIN=http://192.168.1.100:3000
FRONTEND_URL=http://192.168.1.100:3000

# Device API Key
DEVICE_API_KEY=0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567
```

**⚠️ IMPORTANT:** Replace `192.168.1.100` with your actual Server PC IP address!

3. **Start the backend server:**

```cmd
npm start
```

You should see:
```
✅ Server running on port 5000
✅ Database connected
✅ UDP Discovery server started
```

**Keep this terminal window open!**

---

#### 1.4 Configure Frontend (Server PC Only)

1. **Create `.env` file** in `frontend` folder:

```cmd
cd C:\Users\Harley\Desktop\Capstone\attendanceiot\IOTCapstone\frontend
```

Create a file named `.env` with this content:

```env
REACT_APP_API_URL=http://192.168.1.100:5000
```

**⚠️ IMPORTANT:** Replace `192.168.1.100` with your actual Server PC IP address!

2. **Install dependencies and start frontend:**

```cmd
npm install
npm start
```

The frontend will open at `http://localhost:3000` or `http://192.168.1.100:3000`

**Keep this terminal window open!**

---

### STEP 2: Configure Client PCs (3 PCs)

#### 2.1 Access Frontend from Browser

On each Client PC:

1. Open **Google Chrome** or **Microsoft Edge**
2. Navigate to: `http://192.168.1.100:3000`
   - Replace `192.168.1.100` with Server PC's IP
3. You should see the login page
4. Login with admin credentials

**Tip:** Bookmark this URL for easy access!

---

#### 2.2 Configure Futronic App (All 4 PCs)

Each PC needs the Futronic app configured to connect to the Server PC.

1. **Copy the Futronic app folder** to each PC:
   - Copy `C:\Users\Harley\Desktop\Capstone\attendanceiot\IOTCapstone\FutronicAttendanceSystem`
   - Or build from source on each PC

2. **Edit `appsettings.json`** on each PC:

```json
{
  "Database": {
    "Server": "192.168.1.100",
    "Database": "iot_attendance",
    "Username": "root",
    "Password": "YOUR_MYSQL_PASSWORD",
    "Port": 3306,
    "ConnectionTimeout": 30,
    "CommandTimeout": 60
  },
  "Device": {
    "DeviceId": "Lab_PC1",
    "Location": "Computer Lab - Station 1",
    "Building": "Building A"
  },
  "Application": {
    "AlwaysOnAttendance": true,
    "MaxSecondScanAttempts": 3,
    "HeartbeatInterval": 30000,
    "SyncInterval": 300000
  },
  "ESP32": {
    "Enabled": true,
    "APIKey": "0f5e4c2a1b3d4f6e8a9c0b1d2e3f4567a8b9c0d1e2f3456789abcdef01234567"
  }
}
```

**⚠️ IMPORTANT Changes for Each PC:**
- Replace `192.168.1.100` with Server PC's IP
- Replace `YOUR_MYSQL_PASSWORD` with actual MySQL password
- **Change `DeviceId` and `Location` for each PC:**
  - PC1: `"DeviceId": "Lab_PC1"`, `"Location": "Computer Lab - Station 1"`
  - PC2: `"DeviceId": "Lab_PC2"`, `"Location": "Computer Lab - Station 2"`
  - PC3: `"DeviceId": "Lab_PC3"`, `"Location": "Computer Lab - Station 3"`
  - PC4: `"DeviceId": "Lab_PC4"`, `"Location": "Computer Lab - Station 4"`

3. **Allow MySQL remote connections** (Server PC):

Edit MySQL config file (`my.ini` or `my.cnf`):
```ini
bind-address = 0.0.0.0
```

Restart MySQL service.

4. **Grant remote access** (Server PC MySQL):

```sql
CREATE USER 'root'@'%' IDENTIFIED BY 'YOUR_MYSQL_PASSWORD';
GRANT ALL PRIVILEGES ON iot_attendance.* TO 'root'@'%';
FLUSH PRIVILEGES;
```

5. **Test Futronic app** on each PC:
   - Run `FutronicAttendanceSystem.exe`
   - Should connect to database
   - Should show "Device Online" status

---

### STEP 3: Configure ESP32 Hardware

#### 3.1 Connect ESP32 to Lab WiFi

1. **Edit the Arduino sketch:**

Open: `C:\Users\Harley\Desktop\Capstone\attendanceiot\IOTCapstone\sketch_feb18a_copy_20250803144501\iot_fingerprint_attendance\iot_fingerprint_attendance.ino`

2. **Update WiFi credentials:**

```cpp
// WiFi credentials - 2.4GHz Network (ESP32 Compatible)
const char* ssid = "LAB_WIFI_NAME";         // Your lab WiFi network
const char* password = "LAB_WIFI_PASSWORD"; // Your lab WiFi password
```

3. **Set manual server IP:**

```cpp
// Manual IP override
const char* manualServerIP = "192.168.1.100"; // Server PC's IP address
```

**⚠️ IMPORTANT:** Replace `192.168.1.100` with your actual Server PC IP!

4. **Upload to ESP32:**
   - Connect ESP32 via USB
   - Select correct COM port in Arduino IDE
   - Click Upload
   - Open Serial Monitor (115200 baud)
   - Verify WiFi connection and server discovery

#### 3.2 Verify ESP32 Wiring

**Current Configuration:**
- **Relay** → GPIO 5 (D5)
- **OLED VCC** → 3.3V
- **OLED GND** → GND
- **OLED SCL** → GPIO 21
- **OLED SDA** → GPIO 23
- **Relay Power** → External battery (recommended)

**Test the relay:**
1. Open Serial Monitor
2. Type: `lock_test`
3. Press Enter
4. Relay should click and solenoid should unlock for 2 seconds

---

### STEP 4: Firewall Configuration

**On Server PC**, allow incoming connections:

#### Windows Firewall:
1. Open **Windows Defender Firewall**
2. Click **Advanced Settings**
3. Click **Inbound Rules** → **New Rule**
4. Create rules for:
   - **Port 5000** (Backend API)
   - **Port 3306** (MySQL)
   - **Port 3000** (Frontend - if accessing from other PCs)

Or temporarily disable firewall for testing (not recommended for production).

---

## 🧪 Testing the Complete System

### Test 1: Backend API
From any PC, open browser and visit:
```
http://192.168.1.100:5000/api/health
```
Should return: `{"status":"ok"}`

### Test 2: Frontend Access
From any PC, open browser and visit:
```
http://192.168.1.100:3000
```
Should show login page.

### Test 3: Futronic App Connection
1. Run Futronic app on any PC
2. Should show "Database Connected"
3. Should show "Device Online"

### Test 4: ESP32 Connection
1. Check ESP32 Serial Monitor
2. Should show:
   - WiFi connected
   - Server discovered
   - Heartbeat messages

### Test 5: End-to-End Fingerprint Scan
1. Enroll a user via frontend
2. Scan fingerprint on Futronic app
3. Should see:
   - Attendance logged in frontend
   - ESP32 opens solenoid lock
   - OLED displays user info

---

## 🎯 Demonstration Flow

### Setup Before Demonstration:
1. **Server PC**: Start backend and frontend servers
2. **All PCs**: Open frontend in browser (`http://SERVER_IP:3000`)
3. **All PCs**: Run Futronic app
4. **ESP32**: Power on and verify connection
5. **Login**: Admin login on all browsers

### During Demonstration:

#### Scenario 1: User Enrollment
1. **Frontend (any PC)**: Navigate to Users → Add User
2. **Futronic App (any PC)**: Click "Enroll" → Scan fingerprint
3. **Show**: User appears in frontend with "Enrolled" status

#### Scenario 2: Attendance Logging
1. **Futronic App (any PC)**: Scan enrolled user's fingerprint
2. **Show**: 
   - Attendance log appears in frontend
   - ESP32 unlocks solenoid
   - OLED displays user name

#### Scenario 3: Multi-Device Sync
1. **PC1**: Scan fingerprint
2. **PC2-4**: Show attendance log appears on all PCs simultaneously
3. **Demonstrates**: Real-time synchronization

#### Scenario 4: Hardware Integration
1. **Frontend**: Trigger lock via "Lock Control" page
2. **ESP32**: Relay activates, solenoid unlocks
3. **Show**: Web → Hardware communication

---

## 🐛 Troubleshooting

### Issue: "Cannot connect to backend"
**Solution:**
- Verify Server PC IP address
- Check backend is running (`npm start`)
- Check firewall allows port 5000
- Ping Server PC from Client PC

### Issue: "Database connection failed"
**Solution:**
- Verify MySQL is running on Server PC
- Check MySQL allows remote connections
- Verify password in `appsettings.json`
- Check firewall allows port 3306

### Issue: "ESP32 not connecting"
**Solution:**
- Verify WiFi credentials
- Check Server PC IP in sketch
- Verify ESP32 and Server PC on same network
- Check Serial Monitor for error messages

### Issue: "OLED not displaying"
**Solution:**
- Verify wiring: SDA=GPIO23, SCL=GPIO21
- Check OLED power (3.3V)
- Upload latest sketch

### Issue: "Relay not working"
**Solution:**
- Verify relay on GPIO 5
- Check relay power (external battery recommended)
- Test with `lock_test` command
- Verify Active-LOW logic

---

## 📊 Network Diagram

```
┌─────────────────────────────────────────────────────┐
│                  Lab Network (WiFi/Switch)          │
│                    192.168.1.x                      │
└─────────────────────────────────────────────────────┘
         │           │           │           │         │
    ┌────┴────┐ ┌───┴────┐ ┌───┴────┐ ┌───┴────┐ ┌──┴───┐
    │ Server  │ │ Client │ │ Client │ │ Client │ │ESP32 │
    │  PC     │ │  PC 1  │ │  PC 2  │ │  PC 3  │ │      │
    │         │ │        │ │        │ │        │ │      │
    │ MySQL   │ │Frontend│ │Frontend│ │Frontend│ │Relay │
    │ Backend │ │Futronic│ │Futronic│ │Futronic│ │OLED  │
    │Frontend │ │        │ │        │ │        │ │      │
    └─────────┘ └────────┘ └────────┘ └────────┘ └──────┘
```

---

## 📝 Quick Reference

### Server PC IP: `_________________` (fill in)

### URLs:
- Backend API: `http://SERVER_IP:5000`
- Frontend: `http://SERVER_IP:3000`
- Health Check: `http://SERVER_IP:5000/api/health`

### Credentials:
- Admin Email: `admin@ldcu.edu.ph`
- Admin Password: `(set during setup)`

### Ports:
- Backend: `5000`
- Frontend: `3000`
- MySQL: `3306`
- ESP32 Web: `80`

---

## ✅ Pre-Demonstration Checklist

- [ ] Server PC backend running
- [ ] Server PC frontend running
- [ ] MySQL database accessible
- [ ] All Client PCs can access frontend via browser
- [ ] All PCs have Futronic app running and connected
- [ ] ESP32 connected to WiFi and server
- [ ] ESP32 relay and OLED working
- [ ] Test user enrolled with fingerprint
- [ ] Test attendance scan successful
- [ ] All PCs showing real-time sync
- [ ] Firewall configured correctly
- [ ] Network stable and all devices connected

---

## 🎓 Tips for Smooth Demonstration

1. **Pre-enroll test users** before the demonstration
2. **Keep Serial Monitor open** on ESP32 to show real-time logs
3. **Have backup plan** if WiFi fails (use mobile hotspot)
4. **Test everything 30 minutes before** the demonstration
5. **Have this guide printed** for quick reference
6. **Prepare talking points** for each component
7. **Show the code** if asked about implementation

---

## 📞 Support

If you encounter issues during setup, check:
1. This guide's Troubleshooting section
2. Serial Monitor output (ESP32)
3. Browser Console (F12) for frontend errors
4. Backend terminal for API errors
5. MySQL logs for database issues

Good luck with your demonstration! 🚀
