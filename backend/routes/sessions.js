const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireInstructor, requireAdmin } = require('../middleware/auth');

const router = express.Router();

// Get all sessions with filtering
router.get('/', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { 
            status, 
            instructor_id, 
            room_id, 
            start_date, 
            end_date, 
            academic_year, 
            semester,
            page = 1, 
            limit = 50 
        } = req.query;

        let query = `
            SELECT 
                s.SESSIONID as id,
                s.SESSIONDATE as session_date,
                s.STARTTIME as start_time,
                s.ENDTIME as end_time,
                s.STATUS as status,
                s.DOORUNLOCKEDAT as door_unlocked_at,
                s.DOORLOCKEDAT as door_locked_at,
                s.ACADEMICYEAR as academic_year,
                s.SEMESTER as semester,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                u.FACULTYID as employee_id,
                sub.SUBJECTCODE as course_code,
                sub.SUBJECTNAME as course_name,
                r.ROOMNUMBER as room_number,
                r.ROOMNAME as room_name,
                r.DOORSTATUS as door_status,
                cs.DAYOFWEEK as day_of_week,
                cs.STARTTIME as scheduled_start,
                cs.ENDTIME as scheduled_end
            FROM SESSIONS s
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN ROOMS r ON s.ROOMID = r.ROOMID
            WHERE 1=1
        `;
        const params = [];

        if (status) {
            query += ' AND s.STATUS = ?';
            params.push(status);
        }

        if (instructor_id) {
            query += ' AND s.INSTRUCTORID = ?';
            params.push(instructor_id);
        }

        if (room_id) {
            query += ' AND s.ROOMID = ?';
            params.push(room_id);
        }

        if (start_date) {
            query += ' AND s.SESSIONDATE >= ?';
            params.push(start_date);
        }

        if (end_date) {
            query += ' AND s.SESSIONDATE <= ?';
            params.push(end_date);
        }

        if (academic_year) {
            query += ' AND s.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            query += ' AND s.SEMESTER = ?';
            params.push(semester);
        }

        query += ' ORDER BY s.SESSIONDATE DESC, s.STARTTIME DESC';

        const offset = (page - 1) * limit;
        query += ' LIMIT ? OFFSET ?';
        params.push(parseInt(limit), offset);

        const sessions = await executeQuery(query, params);

        // Get total count
        let countQuery = 'SELECT COUNT(*) as total FROM SESSIONS s WHERE 1=1';
        const countParams = [];

        if (status) {
            countQuery += ' AND s.STATUS = ?';
            countParams.push(status);
        }
        if (instructor_id) {
            countQuery += ' AND s.INSTRUCTORID = ?';
            countParams.push(instructor_id);
        }
        if (room_id) {
            countQuery += ' AND s.ROOMID = ?';
            countParams.push(room_id);
        }
        if (start_date) {
            countQuery += ' AND s.SESSIONDATE >= ?';
            countParams.push(start_date);
        }
        if (end_date) {
            countQuery += ' AND s.SESSIONDATE <= ?';
            countParams.push(end_date);
        }
        if (academic_year) {
            countQuery += ' AND s.ACADEMICYEAR = ?';
            countParams.push(academic_year);
        }
        if (semester) {
            countQuery += ' AND s.SEMESTER = ?';
            countParams.push(semester);
        }

        const [{ total }] = await executeQuery(countQuery, countParams);

        res.json({
            sessions,
            pagination: {
                page: parseInt(page),
                limit: parseInt(limit),
                total,
                pages: Math.ceil(total / limit)
            }
        });

    } catch (error) {
        console.error('Get sessions error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get active sessions
router.get('/active', authenticateToken, requireInstructor, async (req, res) => {
    try {
        // First, let's check if there are any active sessions at all
        const activeSessionCount = await executeQuery(`
            SELECT COUNT(*) as count FROM SESSIONS WHERE STATUS = 'active'
        `);
        console.log('Total active sessions in database:', activeSessionCount[0].count);
        
        // Check for BSBA201 sessions specifically
        const bsba201Sessions = await executeQuery(`
            SELECT 
                s.SESSIONID,
                s.SESSIONDATE,
                s.STARTTIME,
                s.ENDTIME,
                s.STATUS,
                sub.SUBJECTCODE,
                sub.SUBJECTNAME,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name
            FROM SESSIONS s
            LEFT JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            WHERE sub.SUBJECTCODE = 'BSBA201'
            ORDER BY s.SESSIONDATE DESC
        `);
        console.log('BSBA201 sessions found:', JSON.stringify(bsba201Sessions, null, 2));
        
        // Get current day name
        const currentDay = new Date().toLocaleDateString('en-US', { weekday: 'long' });
        const currentTime = new Date().toTimeString().slice(0, 8); // HH:MM:SS format
        const currentDate = new Date().toISOString().split('T')[0]; // YYYY-MM-DD format
        
        console.log('Current day:', currentDay);
        console.log('Current time:', currentTime);
        console.log('Current date:', currentDate);

        // Get today's schedules that should be active based on current time
        const activeSessions = await executeQuery(`
            SELECT 
                cs.SCHEDULEID as id,
                ? as session_date,
                CONCAT(?, ' ', cs.STARTTIME) as start_time,
                CONCAT(?, ' ', cs.ENDTIME) as end_time,
                CASE 
                    WHEN TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME THEN 'active'
                    WHEN TIME(NOW()) < cs.STARTTIME THEN 'waiting'
                    WHEN TIME(NOW()) > cs.ENDTIME THEN 'ended'
                    ELSE 'active'
                END as status,
                CASE 
                    WHEN TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME THEN CONCAT(?, ' ', cs.STARTTIME)
                    ELSE NULL
                END as door_unlocked_at,
                CASE 
                    WHEN TIME(NOW()) > cs.ENDTIME THEN CONCAT(?, ' ', cs.ENDTIME)
                    ELSE NULL
                END as door_locked_at,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                u.FACULTYID as employee_id,
                sub.SUBJECTCODE as course_code,
                sub.SUBJECTNAME as course_name,
                r.ROOMNUMBER as room_number,
                r.ROOMNAME as room_name,
                CASE 
                    WHEN TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME THEN 'unlocked'
                    ELSE 'locked'
                END as door_status,
                cs.DAYOFWEEK as day_of_week,
                cs.STARTTIME as scheduled_start,
                cs.ENDTIME as scheduled_end,
                COALESCE(attendance_count.count, 0) as attendance_count
            FROM CLASSSCHEDULES cs
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN USERS u ON sub.INSTRUCTORID = u.USERID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            LEFT JOIN (
                SELECT 
                    ar.SCHEDULEID,
                    COUNT(*) as count
                FROM ATTENDANCERECORDS ar
                WHERE DATE(ar.SCANDATETIME) = ?
                AND ar.SCANTYPE = 'time_in'
                GROUP BY ar.SCHEDULEID
            ) attendance_count ON cs.SCHEDULEID = attendance_count.SCHEDULEID
            WHERE cs.DAYOFWEEK = ?
            AND cs.ACADEMICYEAR = '2024-2025'
            AND cs.SEMESTER = 'First Semester'
            AND (
                TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME OR
                TIME(NOW()) < cs.ENDTIME -- Show sessions that haven't ended yet today
            )
            ORDER BY cs.STARTTIME ASC
        `, [currentDate, currentDate, currentDate, currentDate, currentDate, currentDate, currentDay]);

        console.log('Main active sessions query result:', JSON.stringify(activeSessions, null, 2));
        console.log('Number of active sessions found:', activeSessions.length);
        
        // Debug each session individually
        activeSessions.forEach((session, index) => {
            console.log(`\n--- Session ${index + 1} Details ---`);
            console.log('Session ID:', session.id);
            console.log('Session Date:', session.session_date);
            console.log('Start Time:', session.start_time);
            console.log('End Time:', session.end_time);
            console.log('Status:', session.status);
            console.log('Course Code:', session.course_code);
            console.log('Course Name:', session.course_name);
            console.log('Instructor:', session.instructor_name);
            console.log('Room:', session.room_number, session.room_name);
            console.log('Scheduled Start:', session.scheduled_start);
            console.log('Scheduled End:', session.scheduled_end);
            console.log('Day of Week:', session.day_of_week);
        });
        
        res.json(activeSessions);

    } catch (error) {
        console.error('Get active sessions error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Create new session
router.post('/', [
    authenticateToken,
    requireInstructor,
    body('schedule_id').notEmpty(),
    body('instructor_id').notEmpty(),
    body('room_id').notEmpty(),
    body('location').isIn(['inside', 'outside']),
    body('auth_method').isIn(['fingerprint', 'rfid'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { schedule_id, instructor_id, room_id, location, auth_method } = req.body;

        // Check if there's already an active session for this schedule/room today
        const today = new Date().toISOString().split('T')[0];
        const existingSession = await getSingleResult(`
            SELECT * FROM SESSIONS 
            WHERE SCHEDULEID = ? AND ROOMID = ? AND SESSIONDATE = ? AND STATUS = 'active'
        `, [schedule_id, room_id, today]);

        if (existingSession) {
            return res.status(400).json({ message: 'Session already active for this schedule and room' });
        }

        // Get schedule details
        const schedule = await getSingleResult(`
            SELECT cs.*, sub.SUBJECTCODE, sub.SUBJECTNAME
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            WHERE cs.SCHEDULEID = ?
        `, [schedule_id]);

        if (!schedule) {
            return res.status(404).json({ message: 'Schedule not found' });
        }

        // Generate UUID for session
        const { v4: uuidv4 } = require('uuid');
        const sessionId = uuidv4();

        // Create session
        await executeQuery(`
            INSERT INTO SESSIONS (
                SESSIONID, SCHEDULEID, INSTRUCTORID, ROOMID, SESSIONDATE, 
                STARTTIME, STATUS, ACADEMICYEAR, SEMESTER,
                ${location === 'outside' ? 'DOORUNLOCKEDAT,' : ''}
                CREATED_AT, UPDATED_AT
            ) VALUES (
                ?, ?, ?, ?, ?, 
                CURRENT_TIMESTAMP, 'active', ?, ?,
                ${location === 'outside' ? 'CURRENT_TIMESTAMP,' : ''}
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            )
        `, [
            sessionId, schedule_id, instructor_id, room_id, today,
            schedule.ACADEMICYEAR, schedule.SEMESTER
        ]);

        // Update door status if outside scan
        if (location === 'outside') {
            await executeQuery(
                'UPDATE ROOMS SET DOORSTATUS = "unlocked", UPDATED_AT = CURRENT_TIMESTAMP WHERE ROOMID = ?',
                [room_id]
            );
        }

        // Log the access
        const logId = uuidv4();
        await executeQuery(`
            INSERT INTO ACCESSLOGS (
                LOGID, USERID, ROOMID, ACCESSTYPE, AUTHMETHOD, LOCATION, RESULT, 
                REASON, CREATED_AT
            ) VALUES (
                ?, ?, ?, 'session_start', ?, ?, 'success',
                'Session started', CURRENT_TIMESTAMP
            )
        `, [
            logId, instructor_id, room_id, auth_method.toUpperCase(), location
        ]);

        res.status(201).json({
            message: 'Session started successfully',
            session_id: sessionId,
            door_unlocked: location === 'outside'
        });

    } catch (error) {
        console.error('Create session error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// End session
router.put('/:id/end', [
    authenticateToken,
    requireInstructor,
    body('location').isIn(['inside', 'outside']),
    body('auth_method').isIn(['fingerprint', 'rfid'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { id } = req.params;
        const { location, auth_method } = req.body;

        // Check if session exists and is active
        const session = await getSingleResult(
            'SELECT * FROM SESSIONS WHERE SESSIONID = ? AND STATUS = "active"',
            [id]
        );

        if (!session) {
            return res.status(404).json({ message: 'Active session not found' });
        }

        // End session
        await executeQuery(`
            UPDATE SESSIONS SET 
                STATUS = 'ended', 
                ENDTIME = CURRENT_TIMESTAMP,
                ${location === 'outside' ? 'DOORLOCKEDAT = CURRENT_TIMESTAMP,' : ''}
                UPDATED_AT = CURRENT_TIMESTAMP 
            WHERE SESSIONID = ?
        `, [id]);

        // Update door status if outside scan
        if (location === 'outside') {
            await executeQuery(
                'UPDATE ROOMS SET DOORSTATUS = "locked", UPDATED_AT = CURRENT_TIMESTAMP WHERE ROOMID = ?',
                [session.ROOMID]
            );
        }

        // Log the access
        const { v4: uuidv4 } = require('uuid');
        const logId = uuidv4();
        await executeQuery(`
            INSERT INTO ACCESSLOGS (
                LOGID, USERID, ROOMID, ACCESSTYPE, AUTHMETHOD, LOCATION, RESULT, 
                REASON, CREATED_AT
            ) VALUES (
                ?, ?, ?, 'session_end', ?, ?, 'success',
                'Session ended', CURRENT_TIMESTAMP
            )
        `, [
            logId, session.INSTRUCTORID, session.ROOMID, auth_method.toUpperCase(), location
        ]);

        res.json({ 
            message: 'Session ended successfully',
            door_locked: location === 'outside'
        });

    } catch (error) {
        console.error('End session error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get session details
router.get('/:id', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;

        const session = await getSingleResult(`
            SELECT 
                s.*,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                u.FACULTYID,
                sub.SUBJECTCODE,
                sub.SUBJECTNAME,
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.DOORSTATUS,
                cs.DAYOFWEEK,
                cs.STARTTIME as scheduled_start,
                cs.ENDTIME as scheduled_end
            FROM SESSIONS s
            JOIN USERS u ON s.INSTRUCTORID = u.USERID
            JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
            JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            JOIN ROOMS r ON s.ROOMID = r.ROOMID
            WHERE s.SESSIONID = ?
        `, [id]);

        if (!session) {
            return res.status(404).json({ message: 'Session not found' });
        }

        // Get attendance records for this session
        const attendance = await executeQuery(`
            SELECT 
                ar.SCANDATETIME as scan_datetime,
                ar.STATUS as status,
                ar.SCANTYPE as scan_type,
                ar.AUTHMETHOD as auth_method,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as student_name,
                u.STUDENTID
            FROM ATTENDANCERECORDS ar
            JOIN USERS u ON ar.USERID = u.USERID
            WHERE ar.SCHEDULEID = ? AND DATE(ar.SCANDATETIME) = ?
            ORDER BY ar.SCANDATETIME DESC
        `, [session.SCHEDULEID, session.SESSIONDATE]);

        res.json({
            session,
            attendance
        });

    } catch (error) {
        console.error('Get session details error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Manual session end (admin override)
router.post('/:id/end', [
    authenticateToken,
    requireAdmin
], async (req, res) => {
    try {
        const { id } = req.params;
        const { lock_door = false } = req.body;

        // Check if session exists and is active
        const session = await getSingleResult(
            'SELECT * FROM SESSIONS WHERE SESSIONID = ? AND STATUS = "active"',
            [id]
        );

        if (!session) {
            return res.status(404).json({ message: 'Active session not found' });
        }

        // End session
        await executeQuery(
            `UPDATE SESSIONS SET 
             STATUS = 'ended', 
             ENDTIME = CURRENT_TIMESTAMP,
             ${lock_door ? 'DOORLOCKEDAT = CURRENT_TIMESTAMP,' : ''}
             UPDATED_AT = CURRENT_TIMESTAMP 
             WHERE SESSIONID = ?`,
            [id]
        );

        // Update door status if requested
        if (lock_door) {
            await executeQuery(
                'UPDATE ROOMS SET DOORSTATUS = "locked", UPDATED_AT = CURRENT_TIMESTAMP WHERE ROOMID = ?',
                [session.ROOMID]
            );
        }

        res.json({ 
            message: 'Session ended successfully',
            door_locked: lock_door
        });

    } catch (error) {
        console.error('End session error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Manual door lock/unlock (admin override)
router.post('/manual-lock', [
    authenticateToken,
    requireAdmin,
    body('room_id').notEmpty(),
    body('action').isIn(['lock', 'unlock'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { room_id, action } = req.body;

        // Check if room exists
        const room = await getSingleResult('SELECT * FROM ROOMS WHERE ROOMID = ?', [room_id]);
        if (!room) {
            return res.status(404).json({ message: 'Room not found' });
        }

        const doorStatus = action === 'lock' ? 'locked' : 'unlocked';

        // Update door status
        await executeQuery(
            'UPDATE ROOMS SET DOORSTATUS = ?, UPDATED_AT = CURRENT_TIMESTAMP WHERE ROOMID = ?',
            [doorStatus, room_id]
        );

        // Log the manual action
        const { v4: uuidv4 } = require('uuid');
        const logId = uuidv4();
        await executeQuery(
            `INSERT INTO ACCESSLOGS (LOGID, USERID, ROOMID, AUTHMETHOD, LOCATION, ACCESSTYPE, RESULT, REASON, CREATED_AT)
             VALUES (?, ?, ?, 'Manual', 'inside', ?, 'success', 'Manual override by admin', CURRENT_TIMESTAMP)`,
            [logId, req.user.id, room_id, action === 'lock' ? 'door_lock' : 'door_unlock']
        );

        res.json({
            message: `Door ${action}ed successfully`,
            room: room.ROOMNUMBER,
            door_status: doorStatus
        });

    } catch (error) {
        console.error('Manual door control error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get session statistics
router.get('/stats/overview', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { academic_year, semester, start_date, end_date } = req.query;

        let whereClause = '';
        const params = [];

        if (academic_year) {
            whereClause += ' AND s.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            whereClause += ' AND s.SEMESTER = ?';
            params.push(semester);
        }

        if (start_date) {
            whereClause += ' AND s.SESSIONDATE >= ?';
            params.push(start_date);
        }

        if (end_date) {
            whereClause += ' AND s.SESSIONDATE <= ?';
            params.push(end_date);
        }

        const stats = await executeQuery(`
            SELECT 
                s.STATUS as status,
                COUNT(*) as count,
                AVG(TIMESTAMPDIFF(MINUTE, s.STARTTIME, s.ENDTIME)) as avg_duration_minutes
            FROM SESSIONS s
            WHERE 1=1 ${whereClause}
            GROUP BY s.STATUS
            ORDER BY s.STATUS
        `, params);

        // Get today's active sessions
        const todayActive = await executeQuery(`
            SELECT COUNT(*) as count
            FROM SESSIONS s
            WHERE s.STATUS = 'active' AND s.SESSIONDATE = CURDATE()
        `);

        res.json({
            session_stats: stats,
            today_active: todayActive[0].count,
            total_sessions: stats.reduce((sum, stat) => sum + stat.count, 0)
        });

    } catch (error) {
        console.error('Get session stats error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router; 