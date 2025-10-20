const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult } = require('../config/database');
const axios = require('axios');

const router = express.Router();

// Helper function to find ESP32 device for a room
const findDeviceForRoom = async (roomId) => {
    try {
        // Get room information
        const room = await getSingleResult(
            'SELECT ROOMID, ROOMNUMBER, ROOMNAME FROM ROOMS WHERE ROOMID = ?',
            [roomId]
        );

        if (!room) {
            console.log('Room not found:', roomId);
            return null;
        }

        console.log('Looking for device in room:', room.ROOMNUMBER);

        // Find active device registered for this room
        const device = await getSingleResult(
            `SELECT DEVICEID, IPADDRESS, PORT, LOCATION, ROOMNUMBER 
             FROM DEVICES 
             WHERE ROOMNUMBER = ? 
             AND DEVICETYPE = 'ESP32_Lock_Controller' 
             AND STATUS = 'online'
             ORDER BY LASTSEEN DESC 
             LIMIT 1`,
            [room.ROOMNUMBER]
        );

        if (device) {
            console.log('Found device:', device.DEVICEID, 'at', device.IPADDRESS);
        } else {
            console.log('No device found for room number:', room.ROOMNUMBER);
        }

        return device;
    } catch (error) {
        console.error('Error finding device for room:', error);
        return null;
    }
};

// Helper function to send lock command to ESP32
const sendLockCommand = async (deviceIp, action, userName) => {
    try {
        const url = `http://${deviceIp}/api/lock-control`;
        const payload = {
            action: action, // 'open' or 'close'
            user: userName
        };

        console.log(`Sending lock command to ${url}:`, payload);

        const response = await axios.post(url, payload, {
            timeout: 5000,
            headers: { 'Content-Type': 'application/json' }
        });

        console.log('ESP32 response:', response.data);

        return {
            success: true,
            message: response.data.message || 'Lock command sent successfully'
        };
    } catch (error) {
        console.error('Error sending lock command:', error.message);
        return {
            success: false,
            message: error.message
        };
    }
};

// POST /api/lock-control/request
// Simple version: Just toggle the lock when instructor scans
router.post('/request', [
    body('user_id').isUUID(),
    body('room_id').isUUID(),
    body('auth_method').isIn(['fingerprint', 'rfid']),
    body('action').isIn(['check_in', 'check_out'])
], async (req, res) => {
    try {
        console.log('\n=== LOCK CONTROL REQUEST ===');
        console.log('Request body:', req.body);

        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            console.log('Validation errors:', errors.array());
            return res.status(400).json({ errors: errors.array() });
        }

        const { user_id, room_id, auth_method, action } = req.body;

        // Get user information
        const user = await getSingleResult(
            `SELECT USERID as id, FIRSTNAME as first_name, LASTNAME as last_name, 
                    USERTYPE as role, STATUS as status
             FROM USERS 
             WHERE USERID = ? AND STATUS = 'Active'`,
            [user_id]
        );

        if (!user) {
            console.log('User not found:', user_id);
            return res.status(404).json({ 
                message: 'User not found',
                lock_action: 'none'
            });
        }

        console.log('User:', user.first_name, user.last_name, '- Role:', user.role);

        // Only instructors can control the lock
        if (user.role !== 'instructor') {
            console.log('User is not an instructor');
            return res.json({
                message: 'Lock control only available for instructors',
                lock_action: 'none',
                user_role: user.role
            });
        }

        // Simple toggle: check_in = open, check_out = close
        const lockAction = action === 'check_in' ? 'open' : 'close';
        console.log('Lock action determined:', lockAction);

        // Find ESP32 device for this room
        const device = await findDeviceForRoom(room_id);

        if (!device) {
            console.log('No ESP32 device found for room:', room_id);
            return res.json({
                message: 'No lock controller found for this room',
                lock_action: lockAction,
                device_available: false,
                hint: 'Register ESP32 in DEVICES table'
            });
        }

        console.log('Sending command to ESP32:', device.IPADDRESS);

        // Send lock command to ESP32
        const userName = `${user.first_name} ${user.last_name}`;
        const lockCommandResult = await sendLockCommand(device.IPADDRESS, lockAction, userName);

        console.log('Lock command result:', lockCommandResult);

        // Log the lock control action
        await executeQuery(
            `INSERT INTO ACCESSLOGS (USERID, ROOMID, AUTHMETHOD, LOCATION, ACCESSTYPE, RESULT, REASON)
             VALUES (?, ?, ?, 'outside', ?, ?, ?)`,
            [
                user_id,
                room_id,
                auth_method,
                lockAction === 'open' ? 'door_unlock' : 'door_lock',
                lockCommandResult.success ? 'success' : 'failed',
                lockCommandResult.message
            ]
        );

        res.json({
            message: `Lock ${lockAction} command sent`,
            lock_action: lockAction,
            device_available: true,
            lock_command_sent: true,
            lock_command_result: lockCommandResult,
            instructor: userName,
            device_ip: device.IPADDRESS
        });

        console.log('=== LOCK CONTROL REQUEST COMPLETE ===\n');

    } catch (error) {
        console.error('Lock control request error:', error);
        res.status(500).json({ 
            message: 'Internal server error',
            error: error.message 
        });
    }
});

module.exports = router;
