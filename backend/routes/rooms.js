const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin, requireInstructor } = require('../middleware/auth');

const router = express.Router();

// Get all rooms
router.get('/', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { room_type, building, status, page = 1, limit = 50, search } = req.query;

        let query = `
            SELECT 
                r.ROOMID,
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.BUILDING,
                r.CAPACITY,
                r.STATUS,
                r.DOORSTATUS,
                COUNT(d.DEVICEID) as device_count,
                COUNT(CASE WHEN d.STATUS = 'Active' THEN 1 END) as active_devices
            FROM ROOMS r
            LEFT JOIN DEVICES d ON r.ROOMID = d.ROOMID
            WHERE 1=1 AND r.ARCHIVED_AT IS NULL
        `;
        const params = [];

        if (room_type) {
            query += ' AND r.ROOMTYPE = ?';
            params.push(room_type);
        }

        if (building) {
            query += ' AND r.BUILDING = ?';
            params.push(building);
        }

        if (status) {
            query += ' AND r.STATUS = ?';
            params.push(status);
        }

        if (search) {
            query += ' AND (r.ROOMNUMBER LIKE ? OR r.ROOMNAME LIKE ? OR r.BUILDING LIKE ?)';
            const searchTerm = `%${search}%`;
            params.push(searchTerm, searchTerm, searchTerm);
        }

        query += ' GROUP BY r.ROOMID ORDER BY r.BUILDING, r.ROOMNUMBER';

        const offset = (page - 1) * limit;
        query += ` LIMIT ${parseInt(limit)} OFFSET ${offset}`;

        const rooms = await executeQuery(query, params);

        // Get total count
        let countQuery = 'SELECT COUNT(*) as total FROM ROOMS r WHERE 1=1 AND r.ARCHIVED_AT IS NULL';
        const countParams = [];

        if (room_type) {
            countQuery += ' AND r.ROOMTYPE = ?';
            countParams.push(room_type);
        }
        if (building) {
            countQuery += ' AND r.BUILDING = ?';
            countParams.push(building);
        }
        if (status) {
            countQuery += ' AND r.STATUS = ?';
            countParams.push(status);
        }
        if (search) {
            countQuery += ' AND (r.ROOMNUMBER LIKE ? OR r.ROOMNAME LIKE ? OR r.BUILDING LIKE ?)';
            const searchTerm = `%${search}%`;
            countParams.push(searchTerm, searchTerm, searchTerm);
        }

        const [{ total }] = await executeQuery(countQuery, countParams);

        res.json({
            rooms,
            pagination: {
                page: parseInt(page),
                limit: parseInt(limit),
                total,
                pages: Math.ceil(total / limit)
            }
        });

    } catch (error) {
        console.error('Get rooms error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get single room with details
router.get('/:id', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;

        const room = await getSingleResult(`
            SELECT r.*
            FROM ROOMS r
            WHERE r.ROOMID = ?
        `, [id]);

        if (!room) {
            return res.status(404).json({ message: 'Room not found' });
        }

        // Get devices in this room
        const devices = await executeQuery(`
            SELECT 
                d.DEVICEID,
                d.DEVICENAME,
                d.DEVICETYPE,
                d.LOCATION,
                d.STATUS,
                d.LASTSEEN,
                d.IPADDRESS,
                d.FIRMWAREVERSION
            FROM DEVICES d
            WHERE d.ROOMID = ?
            ORDER BY d.DEVICETYPE, d.LOCATION
        `, [id]);

        // Get current session if any
        const currentSession = await getSingleResult(`
            SELECT 
                s.id,
                s.status,
                s.start_time,
                CONCAT(u.first_name, ' ', u.last_name) as instructor_name,
                c.course_code,
                c.course_name
            FROM SESSIONS s
            JOIN USERS u ON s.instructor_id = u.id
            JOIN CLASSSCHEDULES cs ON s.schedule_id = cs.id
            JOIN COURSES c ON cs.course_id = c.id
            WHERE s.room_id = ? AND s.session_date = CURDATE() AND s.status = 'active'
        `, [id]);

        // Get today's schedule
        const todaySchedule = await executeQuery(`
            SELECT 
                cs.id,
                cs.day_of_week,
                cs.start_time,
                cs.end_time,
                CONCAT(u.first_name, ' ', u.last_name) as instructor_name,
                c.course_code,
                c.course_name,
                s.status as session_status
            FROM CLASSSCHEDULES cs
            JOIN USERS u ON cs.instructor_id = u.id
            JOIN COURSES c ON cs.course_id = c.id
            LEFT JOIN SESSIONS s ON cs.id = s.schedule_id AND s.session_date = CURDATE()
            WHERE cs.room_id = ? 
            AND cs.day_of_week = DAYNAME(CURDATE())
            AND cs.is_active = TRUE
            ORDER BY cs.start_time
        `, [id]);

        res.json({
            room,
            devices,
            current_session: currentSession,
            today_schedule: todaySchedule
        });

    } catch (error) {
        console.error('Get room details error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Create new room
router.post('/', [
    authenticateToken,
    requireAdmin,
    body('room_number').trim().notEmpty(),
    body('room_name').trim().notEmpty(),
    body('building').optional().trim(),
    body('capacity').optional().custom((value) => {
        if (value === '' || value === null || value === undefined) return true; // Allow empty
        const num = parseInt(value);
        if (isNaN(num) || num < 1) {
            throw new Error('Capacity must be a positive integer');
        }
        return true;
    }),
    body('room_type').optional().isIn(['classroom', 'laboratory', 'office'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { room_number, room_name, building, capacity, room_type } = req.body;

        // Check if room number already exists
        const existingRoom = await getSingleResult(
            'SELECT ROOMID FROM ROOMS WHERE ROOMNUMBER = ?',
            [room_number]
        );

        if (existingRoom) {
            return res.status(409).json({ message: 'Room number already exists' });
        }

        // Convert capacity to integer
        const capacityValue = capacity && capacity !== '' ? parseInt(capacity) : 0;

        const { v4: uuidv4 } = require('uuid');
        const roomId = uuidv4();

        await executeQuery(
            `INSERT INTO ROOMS (ROOMID, ROOMNUMBER, ROOMNAME, BUILDING, CAPACITY)
             VALUES (?, ?, ?, ?, ?)`,
            [roomId, room_number, room_name, building || '', capacityValue]
        );

        const newRoom = await getSingleResult(
            'SELECT * FROM ROOMS WHERE ROOMID = ?',
            [roomId]
        );

        res.status(201).json({
            message: 'Room created successfully',
            room: newRoom
        });

    } catch (error) {
        console.error('Create room error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Update room
router.put('/:id', [
    authenticateToken,
    requireAdmin,
    body('room_number').optional().trim().notEmpty(),
    body('room_name').optional().trim().notEmpty(),
    body('building').optional().trim(),
    body('capacity').optional().custom((value) => {
        if (value === '' || value === null || value === undefined) return true; // Allow empty
        const num = parseInt(value);
        if (isNaN(num) || num < 1) {
            throw new Error('Capacity must be a positive integer');
        }
        return true;
    }),
    body('room_type').optional().isIn(['classroom', 'laboratory', 'office']),
    body('status').optional().isIn(['Available', 'Maintenance', 'Occupied'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { id } = req.params;
        const updateFields = req.body;

        // Remove undefined fields and room_type (since ROOMTYPE column doesn't exist)
        Object.keys(updateFields).forEach(key => {
            if (updateFields[key] === undefined || key === 'room_type') {
                delete updateFields[key];
            }
        });

        // Convert capacity to integer if provided
        if (updateFields.capacity && updateFields.capacity !== '') {
            updateFields.capacity = parseInt(updateFields.capacity);
        } else if (updateFields.capacity === '') {
            delete updateFields.capacity; // Remove empty capacity
        }

        if (Object.keys(updateFields).length === 0) {
            return res.status(400).json({ message: 'No fields to update' });
        }

        // Check if room exists
        const existingRoom = await getSingleResult('SELECT ROOMID FROM ROOMS WHERE ROOMID = ?', [id]);
        if (!existingRoom) {
            return res.status(404).json({ message: 'Room not found' });
        }

        // Check if room number is being changed and already exists
        if (updateFields.room_number) {
            const duplicateRoom = await getSingleResult(
                'SELECT ROOMID FROM ROOMS WHERE ROOMNUMBER = ? AND ROOMID != ?',
                [updateFields.room_number, id]
            );
            if (duplicateRoom) {
                return res.status(409).json({ message: 'Room number already exists' });
            }
        }

        // Build update query - map frontend field names to database column names
        const fieldMap = {
            room_number: 'ROOMNUMBER',
            room_name: 'ROOMNAME',
            building: 'BUILDING',
            capacity: 'CAPACITY',
            status: 'STATUS'
        };

        const setClause = Object.keys(updateFields).map(key => `${fieldMap[key]} = ?`).join(', ');
        const values = Object.values(updateFields);
        values.push(id);

        await executeQuery(
            `UPDATE ROOMS SET ${setClause}, UPDATED_AT = CURRENT_TIMESTAMP WHERE ROOMID = ?`,
            values
        );

        const updatedRoom = await getSingleResult('SELECT * FROM ROOMS WHERE ROOMID = ?', [id]);

        res.json({
            message: 'Room updated successfully',
            room: updatedRoom
        });

    } catch (error) {
        console.error('Update room error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Bulk delete rooms
router.delete('/bulk-delete', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { ids } = req.body;
        
        if (!ids || !Array.isArray(ids) || ids.length === 0) {
            return res.status(400).json({ message: 'Room IDs are required' });
        }

        // Validate that all IDs are valid UUIDs
        const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
        const invalidIds = ids.filter(id => !uuidRegex.test(id));
        if (invalidIds.length > 0) {
            return res.status(400).json({ message: 'Invalid room ID format' });
        }

        let deletedCount = 0;
        const errors = [];

        for (const roomId of ids) {
            try {
                // Check if room exists
                const room = await getSingleResult(
                    'SELECT ROOMID, ROOMNUMBER FROM ROOMS WHERE ROOMID = ?',
                    [roomId]
                );

                if (!room) {
                    errors.push(`Room with ID ${roomId} not found`);
                    continue;
                }

                // Delete related data first (cascade delete)
                // Delete sessions
                await executeQuery('DELETE FROM SESSIONS WHERE ROOMID = ?', [roomId]);
                
                // Delete class schedules
                await executeQuery('DELETE FROM CLASSSCHEDULES WHERE ROOMID = ?', [roomId]);
                
                // Delete devices
                await executeQuery('DELETE FROM DEVICES WHERE ROOMID = ?', [roomId]);
                
                // Delete the room
                await executeQuery('DELETE FROM ROOMS WHERE ROOMID = ?', [roomId]);
                
                deletedCount++;
                console.log(`✅ Room ${room.ROOMNUMBER} deleted successfully`);
                
            } catch (error) {
                console.error(`❌ Error deleting room ${roomId}:`, error);
                errors.push(`Failed to delete room ${roomId}: ${error.message}`);
            }
        }

        if (deletedCount === 0) {
            return res.status(400).json({ 
                message: 'No rooms were deleted', 
                details: errors.join('; ') 
            });
        }

        const message = `${deletedCount} room${deletedCount > 1 ? 's' : ''} deleted successfully`;
        
        res.json({ 
            message,
            deletedCount,
            errors: errors.length > 0 ? errors : undefined
        });

    } catch (error) {
        console.error('Bulk delete rooms error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Delete room with cascade deletion
router.delete('/:id', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const roomId = req.params.id;
        console.log(`🚀 DELETE /api/rooms/${roomId} - CASCADE DELETION PROCESS STARTING`);

        // Check if room exists
        const room = await getSingleResult('SELECT ROOMID, ROOMNUMBER, ROOMNAME FROM ROOMS WHERE ROOMID = ?', [roomId]);
        if (!room) {
            return res.status(404).json({ message: 'Room not found' });
        }

        // Start transaction-like operations
        let deletedSchedules = 0;
        let deletedDevices = 0;
        let deletedSessions = 0;

        // Get counts before deletion for response message
        const scheduleCount = await executeQuery(
            'SELECT COUNT(*) as count FROM CLASSSCHEDULES WHERE ROOMID = ?',
            [roomId]
        );
        deletedSchedules = scheduleCount[0].count;

        const deviceCount = await executeQuery(
            'SELECT COUNT(*) as count FROM DEVICES WHERE ROOMID = ?',
            [roomId]
        );
        deletedDevices = deviceCount[0].count;

        const sessionCount = await executeQuery(
            'SELECT COUNT(*) as count FROM SESSIONS WHERE ROOMID = ?',
            [roomId]
        );
        deletedSessions = sessionCount[0].count;

        console.log(`Deleting room ${roomId}: ${deletedSchedules} schedules, ${deletedDevices} devices, ${deletedSessions} sessions`);

        // Cascade delete: First delete sessions
        if (deletedSessions > 0) {
            console.log('Deleting sessions...');
            try {
                await executeQuery('DELETE FROM SESSIONS WHERE ROOMID = ?', [roomId]);
                console.log('Sessions deleted successfully');
            } catch (sessionError) {
                console.error('Error deleting sessions:', sessionError);
                throw sessionError;
            }
        }

        // Cascade delete: Then delete schedules
        if (deletedSchedules > 0) {
            console.log('Deleting schedules...');
            try {
                await executeQuery('DELETE FROM CLASSSCHEDULES WHERE ROOMID = ?', [roomId]);
                console.log('Schedules deleted successfully');
            } catch (scheduleError) {
                console.error('Error deleting schedules:', scheduleError);
                throw scheduleError;
            }
        }

        // Cascade delete: Then delete devices
        if (deletedDevices > 0) {
            console.log('Deleting devices...');
            try {
                await executeQuery('DELETE FROM DEVICES WHERE ROOMID = ?', [roomId]);
                console.log('Devices deleted successfully');
            } catch (deviceError) {
                console.error('Error deleting devices:', deviceError);
                // Mark devices as inactive instead of deleting
                await executeQuery('UPDATE DEVICES SET STATUS = "Inactive" WHERE ROOMID = ?', [roomId]);
                console.log('Devices marked as inactive');
            }
        }

        // Finally delete the room itself
        console.log('Deleting room...');
        try {
            await executeQuery('DELETE FROM ROOMS WHERE ROOMID = ?', [roomId]);
            console.log('Room deleted successfully');
        } catch (roomError) {
            console.error('Error deleting room:', roomError);
            throw roomError;
        }

        // Prepare response message
        let message = `Room "${room.ROOMNUMBER} - ${room.ROOMNAME}" deleted successfully`;
        const deletedItems = [];
        
        if (deletedSchedules > 0) {
            deletedItems.push(`${deletedSchedules} schedule${deletedSchedules !== 1 ? 's' : ''}`);
        }
        if (deletedDevices > 0) {
            deletedItems.push(`${deletedDevices} device${deletedDevices !== 1 ? 's' : ''}`);
        }
        if (deletedSessions > 0) {
            deletedItems.push(`${deletedSessions} session${deletedSessions !== 1 ? 's' : ''}`);
        }
        
        if (deletedItems.length > 0) {
            message += ` (also removed ${deletedItems.join(', ')})`;
        }

        res.json({ 
            message,
            deletedSchedules,
            deletedDevices,
            deletedSessions
        });

    } catch (error) {
        console.error('Delete room error:', error);
        console.error('Error details:', error.message);
        console.error('SQL State:', error.sqlState);
        console.error('Error Code:', error.code);
        res.status(500).json({ 
            message: 'Internal server error',
            details: error.message 
        });
    }
});

// Get buildings list for filter
router.get('/buildings/list', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const buildings = await executeQuery(
            'SELECT DISTINCT BUILDING FROM ROOMS WHERE BUILDING IS NOT NULL AND BUILDING != "" ORDER BY BUILDING'
        );

        res.json({ buildings: buildings.map(b => b.BUILDING) });

    } catch (error) {
        console.error('Get buildings error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get room statistics
router.get('/stats/overview', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const stats = await executeQuery(`
            SELECT 
                room_type,
                COUNT(*) as total_rooms,
                COUNT(CASE WHEN is_active = TRUE THEN 1 END) as active_rooms,
                COUNT(CASE WHEN door_status = 'unlocked' THEN 1 END) as unlocked_rooms,
                AVG(capacity) as avg_capacity
            FROM ROOMS
            GROUP BY room_type
            ORDER BY room_type
        `);

        const deviceStats = await executeQuery(`
            SELECT 
                d.device_type,
                COUNT(*) as total_devices,
                COUNT(CASE WHEN d.status = 'online' THEN 1 END) as online_devices,
                COUNT(CASE WHEN d.status = 'offline' THEN 1 END) as offline_devices
            FROM DEVICES d
            WHERE d.is_active = TRUE
            GROUP BY d.device_type
            ORDER BY d.device_type
        `);

        const totalRooms = await getSingleResult('SELECT COUNT(*) as total FROM ROOMS WHERE is_active = TRUE');
        const activeRooms = await getSingleResult('SELECT COUNT(*) as total FROM ROOMS WHERE is_active = TRUE AND door_status = "unlocked"');

        res.json({
            room_stats: stats,
            device_stats: deviceStats,
            summary: {
                total_rooms: totalRooms.total,
                unlocked_rooms: activeRooms.total,
                locked_rooms: totalRooms.total - activeRooms.total
            }
        });

    } catch (error) {
        console.error('Get room stats error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get room usage analytics
router.get('/:id/analytics', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;
        const { start_date, end_date, academic_year, semester } = req.query;

        let whereClause = 'WHERE s.room_id = ?';
        const params = [id];

        if (start_date) {
            whereClause += ' AND s.session_date >= ?';
            params.push(start_date);
        }

        if (end_date) {
            whereClause += ' AND s.session_date <= ?';
            params.push(end_date);
        }

        if (academic_year) {
            whereClause += ' AND s.academic_year = ?';
            params.push(academic_year);
        }

        if (semester) {
            whereClause += ' AND s.semester = ?';
            params.push(semester);
        }

        // Session statistics
        const sessionStats = await executeQuery(`
            SELECT 
                COUNT(*) as total_sessions,
                COUNT(CASE WHEN status = 'active' THEN 1 END) as active_sessions,
                COUNT(CASE WHEN status = 'ended' THEN 1 END) as completed_sessions,
                AVG(TIMESTAMPDIFF(MINUTE, start_time, end_time)) as avg_duration_minutes
            FROM SESSIONS s
            ${whereClause}
        `, params);

        // Daily usage pattern
        const dailyUsage = await executeQuery(`
            SELECT 
                DAYNAME(s.session_date) as day_name,
                COUNT(*) as session_count,
                AVG(TIMESTAMPDIFF(MINUTE, s.start_time, s.end_time)) as avg_duration
            FROM SESSIONS s
            ${whereClause}
            GROUP BY DAYNAME(s.session_date), DAYOFWEEK(s.session_date)
            ORDER BY DAYOFWEEK(s.session_date)
        `, params);

        // Attendance statistics
        const attendanceStats = await executeQuery(`
            SELECT 
                COUNT(DISTINCT ar.user_id) as unique_students,
                COUNT(*) as total_attendance_records,
                COUNT(CASE WHEN ar.status = 'present' THEN 1 END) as present_count,
                COUNT(CASE WHEN ar.status = 'late' THEN 1 END) as late_count
            FROM SESSIONS s
            JOIN ATTENDANCERECORDS ar ON s.schedule_id = ar.schedule_id 
                AND DATE(ar.scan_datetime) = s.session_date
            ${whereClause}
        `, params);

        res.json({
            session_stats: sessionStats[0],
            daily_usage: dailyUsage,
            attendance_stats: attendanceStats[0]
        });

    } catch (error) {
        console.error('Get room analytics error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});


module.exports = router; 