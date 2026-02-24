---
description: Client PC Futronic App Setup and Troubleshooting Guide
---

# Client PC Futronic App Setup Guide

## Current Problem
The Futronic Attendance System on the client PC is still using **old code** that attempts to discover ESP32 directly on the network instead of sending lock commands through the backend API. This causes:
- ❌ "No ESP32 device found on network" errors
- ❌ Attendance logs not being saved to the database
- ❌ Heartbeat sending to `localhost:5000` instead of `172.72.100.126:5000`
- ❌ Door lock not triggering

**Only the `lock_test` endpoint is currently functional** because it bypasses the Futronic app and sends commands directly to the backend.

## Root Cause
The client PC's local repository has uncommitted changes in compiled files (`bin/` and `obj/` folders) that are preventing git from pulling the latest source code changes. Even after running `git pull` and `dotnet build`, the app is still running the old compiled version.

## Required Changes
The latest code on GitHub (commits `74bb87e4` and `dac21795`) includes:
1. **Lock control via backend API** - `RequestLockControl` now calls `PostBackendLockControlAsync` instead of `DiscoverESP32`
2. **Anonymous lock control via backend API** - `RequestAnonymousLockControl` now uses backend API
3. **Backend URL fix** - `backendBaseUrl` changed from `localhost:5000` to `172.72.100.126:5000`
4. **RFID endpoint fix** - RFID scan endpoint now uses Server PC IP

## Solution Steps

### Step 1: Configure Git Identity
```bash
cd C:\Users\Administrator\Documents\CapstoneIOT\LDCUIOTCapstone
git config user.email "admin@ws31.local"
git config user.name "Administrator"
```

### Step 2: Force Clean Pull from GitHub
```bash
# Discard ALL local changes and force pull latest code
git reset --hard origin/RFID

# Verify you're on the latest commit (should show dac21795)
git log --oneline -3
```

**Expected output:**
```
dac21795 Fix Futronic app backend URLs for client PC access
74bb87e4 Update RequestAnonymousLockControl to use backend API
049237d6 Fix Futronic app to use backend API for ESP32 lock control
```

### Step 3: Clean Build
```bash
cd FutronicAttendanceSystem
dotnet clean
dotnet build
```

### Step 4: Run Updated App
```bash
bin\Debug\net48\FutronicAttendanceSystem.exe
```

## Verification

### Before Fix (OLD CODE)
```
🔍 Discovering ESP32 devices on network...
Found 2 valid network adapters:
  - 192.168.1.2
  - 172.72.100.250
Local IP: 192.168.1.2
Scanning network: 192.168.1.x
⚠️ WARNING: No ESP32 found in 192.168.1.x
❌ No ESP32 device found on network
🌐 Sending to: http://localhost:5000/api/devices/heartbeat
❌ SendApiHeartbeatAsync error: An error occurred while sending the request.
```

### After Fix (NEW CODE)
```
=== LOCK CONTROL REQUEST START ===
User GUID: 1d28a527-6284-4366-9c88-f76e65d0bf4e
Action: Door Override
📡 Sending lock command through backend API...
Backend Response Status: 200
🔓 Door unlocked
🌐 Sending to: http://172.72.100.126:5000/api/devices/heartbeat
✅ Heartbeat sent successfully
```

## Key Indicators of Success
- ✅ No more "Discovering ESP32 devices on network" messages
- ✅ "📡 Sending lock command through backend API..." appears
- ✅ Heartbeat sends to `172.72.100.126:5000` instead of `localhost:5000`
- ✅ "Backend Response Status: 200" appears
- ✅ Attendance logs saved to database
- ✅ Door lock triggers successfully

## Network Configuration
- **Client PC IP**: `192.168.1.2` (on local network)
- **Server PC IP (Ethernet)**: `172.72.100.126`
- **Server PC IP (Hotspot)**: `192.168.137.1`
- **Backend API Port**: `5000`
- **ESP32 IP (on hotspot)**: `192.168.137.131`

## Important Notes
1. The client PC and ESP32 are on **different networks**, which is why direct ESP32 discovery fails
2. The Server PC acts as a **bridge** between the client PC and ESP32
3. All lock commands must go through the backend API at `http://172.72.100.126:5000/api/lock-control/request`
4. The backend API will then forward commands to the ESP32 at `192.168.137.131`

## Troubleshooting

### If still seeing "Discovering ESP32" after pulling
1. Verify git commit: `git log --oneline -1` should show `dac21795`
2. Check source code: Open `MainForm.cs` and search for `PostBackendLockControlAsync` - it should exist
3. Delete compiled files: `rm -r bin/`, `rm -r obj/`, then rebuild
4. Close all running instances of the app before running the new version

### If heartbeat still goes to localhost
1. Check `MainForm.cs` line 435: should be `http://172.72.100.126:5000`
2. Rebuild after confirming source code is correct
3. Ensure you're running the newly built executable from `bin\Debug\net48\`

### If attendance logs still not saving
1. Verify backend is running on Server PC: `http://172.72.100.126:5000/api/health`
2. Check backend logs for incoming requests
3. Verify MySQL is accessible from client PC
4. Check firewall rules on Server PC (port 5000 should be open)

## Related Files
- **Client PC Futronic App**: `C:\Users\Administrator\Documents\CapstoneIOT\LDCUIOTCapstone\FutronicAttendanceSystem\`
- **Server PC Backend**: `c:\Users\Harley\Desktop\Capstone\attendanceiot\IOTCapstone\backend\`
- **Main Source File**: `FutronicAttendanceSystem\MainForm.cs`
- **Config File**: `FutronicAttendanceSystem\bin\Debug\net48\device_config.json`

## GitHub Repository
- **Branch**: `RFID`
- **Latest Commit**: `dac21795`
- **Repository**: `https://github.com/OsirisXx/LDCUIOTCapstone.git`
