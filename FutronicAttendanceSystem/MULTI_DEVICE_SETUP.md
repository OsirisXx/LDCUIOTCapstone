# Multi-Device Setup Guide

This guide explains how to set up multiple fingerprint scanners in different rooms using the FutronicAttendanceSystem.

## Overview

The system now supports multiple fingerprint devices across different rooms, with each device properly tracked in the database and attendance records showing the correct room information.

## Setup Instructions

### 1. Database Preparation

Ensure your database has the proper schema with the DEVICES and ROOMS tables. The system will automatically:
- Register devices in the DEVICES table
- Link devices to specific rooms
- Track device status and last seen timestamps

### 2. Device Configuration

For each physical device location, you need a separate configuration:

#### Main Device (appsettings.json)
```json
{
  "Device": {
    "DeviceId": "MainOffice_Device1",
    "Location": "Main Office - Room 1",
    "Building": "Building A"
  }
}
```

#### Lab Device (appsettings.device2.json)
```json
{
  "Device": {
    "DeviceId": "Lab1_Device",
    "Location": "Computer Lab 1", 
    "Building": "WAC Building"
  }
}
```

#### Cisco Lab Device (appsettings.device3.json)
```json
{
  "Device": {
    "DeviceId": "CiscoLab_Device",
    "Location": "Cisco Laboratory",
    "Building": "WAC Building"
  }
}
```

### 3. Device Deployment

#### Option A: Using Different Config Files
1. Copy the FutronicAttendanceSystem folder to each device location
2. Rename the appropriate config file to `appsettings.json` for each device
3. Run the application on each device

#### Option B: Using Device Management Tab
1. Run the application on the main computer
2. Go to the "Device Management" tab
3. Enter unique device names for each location
4. Select the room for each device
5. Click "Initialize Device" to register each device
6. Copy the application to each physical location with the correct DeviceId in appsettings.json

### 4. Room Assignment

#### Via UI (Recommended):
1. Open the "Attendance Tracking" tab
2. Use the "Room" dropdown to select the current room
3. Click "Change Room" to update the device's room assignment
4. The system will immediately start recording attendance for the new room

#### Via Device Management Tab:
1. Open the "Device Management" tab
2. View all registered devices and their current rooms
3. Initialize new devices or reassign existing ones to different rooms

### 5. Verification

#### Check Device Registration:
- Go to "Device Management" tab
- Verify all devices are listed with correct rooms
- Current device should be highlighted in green
- Check "Last Seen" timestamps are recent

#### Test Attendance Recording:
- Perform fingerprint scans on different devices
- Check your web interface attendance logs
- Verify that each scan shows the correct room information
- Attendance should show: Room Number, Building, and proper location

## Features

### Multi-Device Support:
- ✅ Multiple devices per room or building
- ✅ Unique device identification
- ✅ Room-based attendance tracking
- ✅ Device status monitoring
- ✅ Automatic device registration

### Room Management:
- ✅ Dynamic room switching
- ✅ Real-time room display
- ✅ Building-based organization
- ✅ Room capacity tracking

### Attendance Tracking:
- ✅ Device-specific attendance records
- ✅ Location-based scanning (inside/outside)
- ✅ Proper room information in logs
- ✅ Multi-device coordination

## Troubleshooting

### Device Not Appearing in Database:
1. Check database connection in appsettings.json
2. Verify DEVICES table exists in database
3. Check application logs for registration errors
4. Try using "Initialize Device" in Device Management tab

### Wrong Room Showing in Attendance:
1. Go to Attendance Tracking tab
2. Verify "Current Room" display shows correct room
3. Use "Change Room" button to update if needed
4. Check Device Management tab for device-room assignments

### Multiple Devices Same Name:
1. Each device must have a unique DeviceId
2. Update appsettings.json with unique names
3. Use Device Management tab to reassign if needed

## Database Schema

The system uses these key tables:
- `DEVICES` - Device registration and room assignments
- `ROOMS` - Available rooms and buildings
- `ATTENDANCERECORDS` - Attendance with proper room linkage
- `SESSIONS` - Active class sessions per room

## Configuration Examples

### Small Office (2-3 Devices):
- MainOffice_Device1 → Reception
- Lab_Device1 → Computer Lab
- Conference_Device1 → Conference Room

### Large Campus (10+ Devices):
- Building_Floor_Room format
- WAC_1_Lab01, WAC_1_Lab02, WAC_2_Classroom201
- Each building can have multiple devices per floor

## Support

For issues or questions:
1. Check application console output for errors
2. Review database logs in web interface
3. Use Device Management tab for device status
4. Verify room assignments match physical locations

