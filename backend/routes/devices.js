const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin, requireInstructor } = require('../middleware/auth');

const router = express.Router();
const deviceRegistry = require('../services/deviceRegistry');

// Get all devices
router.get('/', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { ROOMID, DEVICETYPE, STATUS, page = 1, limit = 50 } = req.query;

        let query = `
            SELECT 
                d.DEVICEID,
                d.DEVICETYPE,
                d.LOCATION,
                d.IPADDRESS,
                d.LASTMAINTENANCE,
                d.STATUS,
                d.CREATED_AT,
                r.ROOMNUMBER,
                r.BUILDING
            FROM DEVICES d
            JOIN ROOMS r ON d.ROOMID = r.ROOMID
            WHERE 1=1
        `;
        const params = [];

        if (ROOMID) {
            query += ' AND d.ROOMID = ?';
            params.push(ROOMID);
        }

        if (DEVICETYPE) {
            query += ' AND d.DEVICETYPE = ?';
            params.push(DEVICETYPE);
        }

        if (STATUS) {
            query += ' AND d.STATUS = ?';
            params.push(STATUS);
        }

        query += ' ORDER BY r.ROOMNUMBER, d.DEVICETYPE';

        const offset = (page - 1) * limit;
        query += ' LIMIT ? OFFSET ?';
        params.push(parseInt(limit), offset);

        const devices = await executeQuery(query, params);

        res.json({ devices });

    } catch (error) {
        console.error('Get devices error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Create device
router.post('/', [
    authenticateToken,
    requireAdmin,
    body('DEVICETYPE').isIn(['RFID_Reader', 'Fingerprint_Scanner', 'Door_Controller']),
    body('LOCATION').trim().notEmpty(),
    body('ROOMID').isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { DEVICETYPE, LOCATION, ROOMID, IPADDRESS } = req.body;

        const result = await executeQuery(
            'INSERT INTO DEVICES (DEVICETYPE, LOCATION, ROOMID, IPADDRESS) VALUES (?, ?, ?, ?)',
            [DEVICETYPE, LOCATION, ROOMID, IPADDRESS]
        );

        res.status(201).json({
            message: 'Device created successfully',
            deviceId: result.insertId
        });

    } catch (error) {
        console.error('Create device error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Test endpoint to verify route is working
router.get('/test', (req, res) => {
    res.json({ message: 'Devices route is working!' });
});

// Trigger solenoid lock via ESP32
router.post('/trigger-lock', [
    authenticateToken,
    body('action').isIn(['open', 'close']),
    body('user').notEmpty(),
    body('timestamp').isISO8601()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { action, user, timestamp } = req.body;

        console.log(`🔓 Solenoid lock trigger request: ${action} by ${user} at ${timestamp}`);

        // Check if user has permission to trigger lock (instructor or admin only)
        if (req.user.role !== 'instructor' && req.user.role !== 'admin') {
            return res.status(403).json({ 
                message: 'Access denied. Only instructors and admins can control the lock.' 
            });
        }

        // Get ESP32 IP address (you can store this in database or environment)
        const esp32IP = process.env.ESP32_IP || '192.168.1.12'; // Default to ESP32 IP
        
        try {
            // Send command to ESP32 to control solenoid lock
            const axios = require('axios');
            
            console.log(`📡 Sending lock control to ESP32 at http://${esp32IP}/api/lock-control`);
            console.log(`📤 Payload:`, { action, user, timestamp });
            
            const response = await axios.post(`http://${esp32IP}/api/lock-control`, {
                action: action,
                user: user,
                timestamp: timestamp
            }, {
                timeout: 15000, // Increased to 15 second timeout
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            console.log('✅ Solenoid lock command sent successfully to ESP32');
            console.log('📨 ESP32 Response:', response.data);
            
            // Log the lock action - Skip logging to avoid ROOMID constraint
            console.log('📋 Skipping access log creation (ROOMID constraint)');

            res.json({
                message: `Solenoid lock ${action}ed successfully`,
                action: action,
                user: user,
                timestamp: timestamp
            });

        } catch (esp32Error) {
            console.error('❌ ESP32 communication error:', esp32Error.message);
            
            // Skip logging failed attempt to avoid ROOMID constraint
            console.log('📋 Skipping failed access log creation (ROOMID constraint)');

            res.status(503).json({
                message: 'Solenoid lock control failed - ESP32 not responding',
                error: esp32Error.message
            });
        }

    } catch (error) {
        console.error('Trigger lock error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Device heartbeat from Futronic or ESP32 devices
router.post('/heartbeat', [
    // API key protected; no user JWT required
    body('deviceType').optional().isString(),
    body('deviceId').optional().isString(),
    body('location').optional().isString(),
    body('roomId').optional(),
    body('roomNumber').optional().isString(),
    body('ipAddress').optional().isString(),
    body('hostname').optional().isString(),
    body('appVersion').optional().isString(),
    body('capabilities').optional().isArray()
], async (req, res) => {
    try {
        const apiKey = req.header('x-device-api-key');
        console.log('🔑 Backend API Key from .env:', process.env.DEVICE_API_KEY);
        console.log('🔑 API Key from request:', apiKey);
        console.log('🔑 Keys match:', apiKey === process.env.DEVICE_API_KEY);
        if (!process.env.DEVICE_API_KEY || apiKey !== process.env.DEVICE_API_KEY) {
            return res.status(401).json({ message: 'Unauthorized device' });
        }

        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const record = deviceRegistry.upsertHeartbeat({
            deviceType: req.body.deviceType,
            deviceId: req.body.deviceId,
            location: req.body.location,
            roomId: req.body.roomId,
            roomNumber: req.body.roomNumber,
            ipAddress: req.body.ipAddress || req.ip,
            hostname: req.body.hostname,
            appVersion: req.body.appVersion,
            capabilities: req.body.capabilities,
        });

        res.status(201).json({ message: 'heartbeat recorded', device: record });
    } catch (error) {
        console.error('Heartbeat error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// List currently online devices based on heartbeat TTL
router.get('/online', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const devices = deviceRegistry.listOnline();
        res.json({ devices });
    } catch (error) {
        console.error('List online devices error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Debug endpoint to see all devices in registry
router.get('/debug/registry', (req, res) => {
    try {
        const allDevices = Array.from(deviceRegistry.deviceIdToDevice.values());
        res.json({ 
            total_devices: allDevices.length,
            devices: allDevices,
            registry_info: {
                heartbeat_ttl_ms: deviceRegistry.heartbeatTtlMs,
                cleanup_interval_ms: deviceRegistry.cleanupIntervalMs
            }
        });
    } catch (error) {
        console.error('Debug registry error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router; 