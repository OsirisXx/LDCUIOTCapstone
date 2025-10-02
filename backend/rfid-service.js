#!/usr/bin/env node

/**
 * RFID Service Launcher
 * 
 * This service captures RFID card scans and sends them directly to the database
 * without showing the input as keyboard typing.
 * 
 * Usage:
 *   node rfid-service.js
 * 
 * Features:
 * - Captures RFID input globally (works even when window is not focused)
 * - Prevents RFID data from appearing as keyboard input
 * - Automatically sends attendance data to backend
 * - Works with R10D and similar USB HID RFID readers
 */

const RFIDService = require('./services/rfidService');

console.log('🔖 IoT Attendance System - RFID Service');
console.log('==========================================');
console.log('');
console.log('📋 Instructions:');
console.log('1. Make sure your backend server is running (npm start)');
console.log('2. Connect your R10D RFID reader via USB');
console.log('3. Scan an RFID card to test');
console.log('4. Press Ctrl+C to stop the service');
console.log('');
console.log('🔧 Troubleshooting:');
console.log('- If RFID data still appears as typing, try running as administrator');
console.log('- Make sure no text editor or input field is focused');
console.log('- Check that RFID reader is configured as HID keyboard device');
console.log('');

// Create and start the RFID service
const rfidService = new RFIDService();
rfidService.start();

// Keep the process running
process.stdin.resume();
