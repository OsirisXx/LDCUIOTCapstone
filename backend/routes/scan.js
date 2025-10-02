const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult, transaction } = require('../config/database');
const { authenticateToken } = require('../middleware/auth');
const moment = require('moment');
const ScheduleValidationService = require('../services/scheduleValidationService');

const router = express.Router();

// Helper function to log access attempts
const logAccess = async (userId, roomId, authMethod, location, action, result, reason = null) => {
    try {
        await executeQuery(
            `INSERT INTO ACCESSLOGS (USERID, ROOMID, AUTHMETHOD, LOCATION, ACCESSTYPE, RESULT, REASON)
             VALUES (?, ?, ?, ?, ?, ?, ?)`,
            [userId, roomId, authMethod, location, action, result, reason]
        );
    } catch (error) {
        console.error('Error logging access:', error);
    }
};

// Helper function to get current schedule with ±15 minute window for instructors
const getCurrentSchedule = async (instructorId, roomId) => {
    const now = moment();
    const currentDay = now.format('dddd');
    const currentTime = now.format('HH:mm:ss');
    
    // Calculate 15 minutes before start time and current time
    const fifteenMinutesEarlier = moment(currentTime, 'HH:mm:ss').subtract(15, 'minutes').format('HH:mm:ss');
    
    return await getSingleResult(
        `SELECT cs.SCHEDULEID as id, cs.SUBJECTID as subject_id, cs.ROOMID as room_id, 
                cs.DAYOFWEEK as day_of_week, cs.STARTTIME as start_time, cs.ENDTIME as end_time,
                sub.SUBJECTNAME as subject_name, sub.SUBJECTCODE as subject_code, 
                r.ROOMNUMBER as room_number, r.ROOMNAME as room_name
         FROM CLASSSCHEDULES cs
         JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
         JOIN ROOMS r ON cs.ROOMID = r.ROOMID
         WHERE sub.INSTRUCTORID = ? 
         AND cs.ROOMID = ? 
         AND cs.DAYOFWEEK = ? 
         AND cs.STARTTIME >= ? 
         AND cs.STARTTIME <= TIME_ADD(?, INTERVAL 15 MINUTE)
         AND cs.ENDTIME >= ?
         AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
         AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
        [instructorId, roomId, currentDay, fifteenMinutesEarlier, currentTime, currentTime]
    );
};

// Helper function to get schedule for early arrival (students can scan 15 minutes before scheduled start)
const getScheduleForEarlyArrival = async (roomId) => {
    const now = moment();
    const currentDay = now.format('dddd');
    const currentTime = now.format('HH:mm:ss');
    
    // Find schedule that starts within the next 15 minutes
    return await getSingleResult(
        `SELECT cs.SCHEDULEID as id, cs.SUBJECTID as subject_id, cs.ROOMID as room_id, 
                cs.DAYOFWEEK as day_of_week, cs.STARTTIME as start_time, cs.ENDTIME as end_time,
                sub.SUBJECTNAME as subject_name, sub.SUBJECTCODE as subject_code, 
                sub.INSTRUCTORID as instructor_id,
                r.ROOMNUMBER as room_number, r.ROOMNAME as room_name
         FROM CLASSSCHEDULES cs
         JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
         JOIN ROOMS r ON cs.ROOMID = r.ROOMID
         WHERE cs.ROOMID = ? 
         AND cs.DAYOFWEEK = ? 
         AND cs.STARTTIME > ?
         AND cs.STARTTIME <= TIME_ADD(?, INTERVAL 15 MINUTE)
         AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
         AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
        [roomId, currentDay, currentTime, currentTime]
    );
};

// Helper function to check if student is enrolled
const checkStudentEnrollment = async (studentId, subjectId) => {
    return await getSingleResult(
        `SELECT se.ENROLLMENTID as id, se.USERID as student_id, se.SUBJECTID as subject_id,
                se.STATUS as status, se.ACADEMICYEAR as academic_year, se.SEMESTER as semester
         FROM SUBJECTENROLLMENT se
         WHERE se.USERID = ? 
         AND se.SUBJECTID = ?
         AND se.STATUS = 'enrolled'
         AND se.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
         AND se.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
        [studentId, subjectId]
    );
};

// Instructor scan outside (start/end session + unlock/lock door)
router.post('/instructor-outside', [
    body('identifier').notEmpty(),
    body('auth_method').isIn(['rfid', 'fingerprint']),
    body('room_id').isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { identifier, auth_method, room_id } = req.body;

        // Get user by authentication method
        const authMethod = await getSingleResult(
            `SELECT am.AUTHID as auth_id, am.USERID as user_id, am.METHODTYPE as method_type, 
                    am.IDENTIFIER as identifier, am.ISACTIVE as is_active,
                    u.USERID as id, u.FIRSTNAME as first_name, u.LASTNAME as last_name, 
                    u.USERTYPE as role, u.STATUS as status
             FROM AUTHENTICATIONMETHODS am
             JOIN USERS u ON am.USERID = u.USERID
             WHERE am.IDENTIFIER = ? AND am.METHODTYPE = ? AND am.ISACTIVE = TRUE AND u.STATUS = 'Active'`,
            [identifier, auth_method]
        );

        if (!authMethod) {
            await logAccess(null, room_id, auth_method, 'outside', 'door_unlock', 'denied', 'Invalid credentials');
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        if (authMethod.role !== 'instructor') {
            await logAccess(authMethod.user_id, room_id, auth_method, 'outside', 'door_unlock', 'denied', 'Not an instructor');
            return res.status(403).json({ message: 'Only instructors can access from outside' });
        }

        // Comprehensive schedule validation for instructor
        const validation = await ScheduleValidationService.validateAttendanceRecording(
            authMethod.user_id, 
            room_id, 
            authMethod.role, 
            'outside'
        );

        if (!validation.isValid) {
            await logAccess(authMethod.user_id, room_id, auth_method, 'outside', 'door_unlock', 'denied', validation.reason);
            return res.status(403).json({ 
                message: 'Door access not allowed',
                details: validation.reason
            });
        }

        const schedule = validation.schedule;

        // Check if there's already an active session
        const activeSession = await getSingleResult(
            `SELECT SESSIONID as id, SCHEDULEID as schedule_id, INSTRUCTORID as instructor_id, 
                    ROOMID as room_id, SESSIONDATE as session_date, STARTTIME as start_time,
                    ENDTIME as end_time, STATUS as status
             FROM SESSIONS 
             WHERE SCHEDULEID = ? AND SESSIONDATE = CURDATE() AND STATUS = 'active'`,
            [schedule.id]
        );

        await transaction(async (connection) => {
            let action, sessionStatus;

            if (activeSession) {
                // End session and lock door
                await connection.execute(
                    `UPDATE SESSIONS SET 
                     STATUS = 'ended', 
                     ENDTIME = CURRENT_TIMESTAMP,
                     DOORLOCKEDAT = CURRENT_TIMESTAMP,
                     UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE SESSIONID = ?`,
                    [activeSession.id]
                );

                await connection.execute(
                    'UPDATE ROOMS SET DOORSTATUS = "locked", UPDATED_AT = CURRENT_TIMESTAMP WHERE ROOMID = ?',
                    [room_id]
                );

                action = 'session_end';
                sessionStatus = 'ended';
            } else {
                // Start session and unlock door
                await connection.execute(
                    `INSERT INTO SESSIONS (SCHEDULEID, INSTRUCTORID, ROOMID, SESSIONDATE, STARTTIME, STATUS, DOORUNLOCKEDAT, ACADEMICYEAR, SEMESTER)
                     VALUES (?, ?, ?, CURDATE(), CURRENT_TIMESTAMP, 'active', CURRENT_TIMESTAMP,
                             (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'),
                             (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'))`,
                    [schedule.id, authMethod.user_id, room_id]
                );

                await connection.execute(
                    'UPDATE ROOMS SET DOORSTATUS = "unlocked", UPDATED_AT = CURRENT_TIMESTAMP WHERE ROOMID = ?',
                    [room_id]
                );

                // Upgrade all Early Arrival students to Present when session starts
                await connection.execute(
                    `UPDATE ATTENDANCERECORDS 
                     SET STATUS = 'Present', 
                         SCANTYPE = 'early_arrival_upgraded',
                         UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE SCHEDULEID = ? 
                     AND DATE(SCANDATETIME) = CURDATE() 
                     AND STATUS = 'Early Arrival'`,
                    [schedule.id]
                );

                action = 'session_start';
                sessionStatus = 'started';
            }

            // Log attendance for instructor
            await connection.execute(
                `INSERT INTO ATTENDANCERECORDS (USERID, SCHEDULEID, SCANTYPE, AUTHMETHOD, LOCATION, STATUS, ACADEMICYEAR, SEMESTER)
                 VALUES (?, ?, ?, ?, 'outside', 'Present',
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'),
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'))`,
                [authMethod.user_id, schedule.id, activeSession ? 'time_out' : 'time_in', auth_method]
            );
        });

        await logAccess(authMethod.user_id, room_id, auth_method, 'outside', action, 'success');

        res.json({
            message: `Session ${sessionStatus} successfully`,
            session_status: sessionStatus,
            door_status: sessionStatus === 'started' ? 'unlocked' : 'locked',
            instructor: `${authMethod.first_name} ${authMethod.last_name}`,
            course: `${schedule.course_code} - ${schedule.course_name}`,
            room: `${schedule.room_number} - ${schedule.room_name}`
        });

    } catch (error) {
        console.error('Instructor outside scan error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Instructor scan inside (end session only, no door lock)
router.post('/instructor-inside', [
    body('identifier').notEmpty(),
    body('auth_method').isIn(['rfid', 'fingerprint']),
    body('room_id').isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { identifier, auth_method, room_id } = req.body;

        // Get user by authentication method
        const authMethod = await getSingleResult(
            `SELECT am.AUTHID as auth_id, am.USERID as user_id, am.METHODTYPE as method_type, 
                    am.IDENTIFIER as identifier, am.ISACTIVE as is_active,
                    u.USERID as id, u.FIRSTNAME as first_name, u.LASTNAME as last_name, 
                    u.USERTYPE as role, u.STATUS as status
             FROM AUTHENTICATIONMETHODS am
             JOIN USERS u ON am.USERID = u.USERID
             WHERE am.IDENTIFIER = ? AND am.METHODTYPE = ? AND am.ISACTIVE = TRUE AND u.STATUS = 'Active'`,
            [identifier, auth_method]
        );

        if (!authMethod) {
            await logAccess(null, room_id, auth_method, 'inside', 'session_end', 'denied', 'Invalid credentials');
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        if (authMethod.role !== 'instructor') {
            await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'session_end', 'denied', 'Not an instructor');
            return res.status(403).json({ message: 'Only instructors can end sessions' });
        }

        // Check for active session
        const activeSession = await getSingleResult(
            `SELECT s.SESSIONID as id, s.SCHEDULEID as schedule_id, s.INSTRUCTORID as instructor_id, 
                    s.ROOMID as room_id, s.SESSIONDATE as session_date, s.STATUS as status,
                    cs.COURSEID as course_id
             FROM SESSIONS s
             JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
             WHERE s.INSTRUCTORID = ? AND s.ROOMID = ? AND s.SESSIONDATE = CURDATE() AND s.STATUS = 'active'`,
            [authMethod.user_id, room_id]
        );

        if (!activeSession) {
            await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'session_end', 'denied', 'No active session');
            return res.status(404).json({ message: 'No active session found' });
        }

        await transaction(async (connection) => {
            // End session (door remains unlocked when scanned from inside)
            await connection.execute(
                `UPDATE SESSIONS SET 
                 STATUS = 'ended', 
                 ENDTIME = CURRENT_TIMESTAMP,
                 UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE SESSIONID = ?`,
                [activeSession.id]
            );

            // Log attendance
            await connection.execute(
                `INSERT INTO ATTENDANCERECORDS (USERID, SCHEDULEID, SCANTYPE, AUTHMETHOD, LOCATION, STATUS, ACADEMICYEAR, SEMESTER)
                 VALUES (?, ?, 'time_out', ?, 'inside', 'Present',
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'),
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'))`,
                [authMethod.user_id, activeSession.schedule_id, auth_method]
            );
        });

        await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'session_end', 'success');

        res.json({
            message: 'Session ended successfully',
            session_status: 'ended',
            door_status: 'unlocked', // Door remains unlocked when ended from inside
            instructor: `${authMethod.first_name} ${authMethod.last_name}`
        });

    } catch (error) {
        console.error('Instructor inside scan error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Student scan (inside room only)
router.post('/student', [
    body('identifier').notEmpty(),
    body('auth_method').isIn(['fingerprint']), // Students use fingerprint only
    body('room_id').isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { identifier, auth_method, room_id } = req.body;

        // Get user by authentication method
        const authMethod = await getSingleResult(
            `SELECT am.AUTHID as auth_id, am.USERID as user_id, am.METHODTYPE as method_type, 
                    am.IDENTIFIER as identifier, am.ISACTIVE as is_active,
                    u.USERID as id, u.FIRSTNAME as first_name, u.LASTNAME as last_name, 
                    u.USERTYPE as role, u.STATUS as status
             FROM AUTHENTICATIONMETHODS am
             JOIN USERS u ON am.USERID = u.USERID
             WHERE am.IDENTIFIER = ? AND am.METHODTYPE = ? AND am.ISACTIVE = TRUE AND u.STATUS = 'Active'`,
            [identifier, auth_method]
        );

        if (!authMethod) {
            await logAccess(null, room_id, auth_method, 'inside', 'attendance_scan', 'denied', 'Invalid credentials');
            return res.status(401).json({ message: 'Invalid fingerprint' });
        }

        if (authMethod.role !== 'student') {
            await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'attendance_scan', 'denied', 'Not a student');
            return res.status(403).json({ message: 'Only students can scan for attendance' });
        }

        // Comprehensive schedule validation
        const validation = await ScheduleValidationService.validateAttendanceRecording(
            authMethod.user_id, 
            room_id, 
            authMethod.role, 
            'inside'
        );

        if (!validation.isValid) {
            await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'attendance_scan', 'denied', validation.reason);
            return res.status(403).json({ 
                message: 'Attendance recording not allowed',
                details: validation.reason
            });
        }

        const activeSession = validation.session;

        // Check if already recorded attendance for this session
        const existingAttendance = await getSingleResult(
            `SELECT ATTENDANCEID as id, USERID as user_id, SCHEDULEID as schedule_id, 
                    SCANTYPE as scan_type, SCANDATETIME as scan_datetime, STATUS as status
             FROM ATTENDANCERECORDS 
             WHERE USERID = ? AND SCHEDULEID = ? AND DATE(SCANDATETIME) = CURDATE()`,
            [authMethod.user_id, activeSession.schedule_id]
        );

        // Handle early arrival confirmation
        if (existingAttendance && existingAttendance.status === 'Early Arrival') {
            // This is a confirmation scan - upgrade to Present and preserve original timestamp
            await transaction(async (connection) => {
                await connection.execute(
                    `UPDATE ATTENDANCERECORDS 
                     SET STATUS = 'Present', 
                         SCANTYPE = 'time_in_confirmation',
                         LOCATION = 'inside',
                         UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ATTENDANCEID = ?`,
                    [existingAttendance.id]
                );
            });

            await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'early_arrival_confirmation', 'success');

            return res.json({
                message: 'Early arrival confirmed successfully',
                student: `${authMethod.first_name} ${authMethod.last_name}`,
                subject: `${activeSession.subject_code} - ${activeSession.subject_name}`,
                status: 'present',
                original_timestamp: existingAttendance.scan_datetime,
                confirmation_timestamp: new Date().toISOString(),
                note: 'Original early arrival timestamp preserved'
            });
        }

        // Check for other existing attendance statuses
        if (existingAttendance && existingAttendance.status !== 'Early Arrival') {
            await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'attendance_scan', 'denied', 'Already recorded');
            return res.status(409).json({ message: 'Attendance already recorded for today' });
        }

        // Get session start time for late calculation (15 minutes from when session actually started)
        const session = await getSingleResult(
            'SELECT STARTTIME as session_start_time FROM SESSIONS WHERE SESSIONID = ?',
            [activeSession.id]
        );

        const lateToleranceMinutes = parseInt(
            (await getSingleResult('SELECT SETTINGVALUE as setting_value FROM SETTINGS WHERE SETTINGKEY = "late_tolerance_minutes"'))?.setting_value || '15'
        );

        const now = moment();
        const sessionStart = moment(session.session_start_time);
        const isLate = now.isAfter(sessionStart.add(lateToleranceMinutes, 'minutes'));

        await transaction(async (connection) => {
            // Record attendance
            await connection.execute(
                `INSERT INTO ATTENDANCERECORDS (USERID, SCHEDULEID, SCANTYPE, AUTHMETHOD, LOCATION, STATUS, ACADEMICYEAR, SEMESTER)
                 VALUES (?, ?, 'time_in', ?, 'inside', ?,
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'),
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'))`,
                [authMethod.user_id, activeSession.schedule_id, auth_method, isLate ? 'Late' : 'Present']
            );
        });

        await logAccess(authMethod.user_id, room_id, auth_method, 'inside', 'attendance_scan', 'success');

        res.json({
            message: `Attendance recorded successfully${isLate ? ' (Late)' : ''}`,
            student: `${authMethod.first_name} ${authMethod.last_name}`,
            subject: `${activeSession.subject_code} - ${activeSession.subject_name}`,
            status: isLate ? 'late' : 'present',
            timestamp: new Date().toISOString()
        });

    } catch (error) {
        console.error('Student scan error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Student scan outside (early arrival - up to 15 minutes before scheduled start)
router.post('/student-outside', [
    body('identifier').notEmpty(),
    body('auth_method').isIn(['fingerprint', 'rfid']),
    body('room_id').isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { identifier, auth_method, room_id } = req.body;

        // Get user by authentication method
        const authMethod = await getSingleResult(
            `SELECT am.AUTHID as auth_id, am.USERID as user_id, am.METHODTYPE as method_type, 
                    am.IDENTIFIER as identifier, am.ISACTIVE as is_active,
                    u.USERID as id, u.FIRSTNAME as first_name, u.LASTNAME as last_name, 
                    u.USERTYPE as role, u.STATUS as status
             FROM AUTHENTICATIONMETHODS am
             JOIN USERS u ON am.USERID = u.USERID
             WHERE am.IDENTIFIER = ? AND am.METHODTYPE = ? AND am.ISACTIVE = TRUE AND u.STATUS = 'Active'`,
            [identifier, auth_method]
        );

        if (!authMethod) {
            await logAccess(null, room_id, auth_method, 'outside', 'early_arrival_scan', 'denied', 'Invalid credentials');
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        if (authMethod.role !== 'student') {
            await logAccess(authMethod.user_id, room_id, auth_method, 'outside', 'early_arrival_scan', 'denied', 'Not a student');
            return res.status(403).json({ message: 'Only students can scan for early arrival' });
        }

        // Comprehensive schedule validation for early arrival
        const validation = await ScheduleValidationService.validateAttendanceRecording(
            authMethod.user_id, 
            room_id, 
            authMethod.role, 
            'outside'
        );

        if (!validation.isValid) {
            await logAccess(authMethod.user_id, room_id, auth_method, 'outside', 'early_arrival_scan', 'denied', validation.reason);
            return res.status(403).json({ 
                message: 'Early arrival not allowed',
                details: validation.reason
            });
        }

        const schedule = validation.schedule;

        // Check if already recorded early arrival for this schedule today
        const existingEarlyArrival = await getSingleResult(
            `SELECT ATTENDANCEID as id, STATUS as status
             FROM ATTENDANCERECORDS 
             WHERE USERID = ? AND SCHEDULEID = ? AND DATE(SCANDATETIME) = CURDATE() 
             AND STATUS IN ('Early Arrival', 'Present', 'Late', 'Early Scan | Absent')`,
            [authMethod.user_id, schedule.id]
        );

        if (existingEarlyArrival) {
            let message = 'Attendance already recorded for today';
            if (existingEarlyArrival.status === 'Early Arrival') {
                message = 'Early arrival already recorded. Please scan inside when class starts.';
            }
            await logAccess(authMethod.user_id, room_id, auth_method, 'outside', 'early_arrival_scan', 'denied', 'Already recorded');
            return res.status(409).json({ message });
        }

        // Record early arrival attendance
        await transaction(async (connection) => {
            await connection.execute(
                `INSERT INTO ATTENDANCERECORDS (USERID, SCHEDULEID, SCANTYPE, AUTHMETHOD, LOCATION, STATUS, ACADEMICYEAR, SEMESTER)
                 VALUES (?, ?, 'early_arrival', ?, 'outside', 'Early Arrival',
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year'),
                         (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester'))`,
                [authMethod.user_id, schedule.id, auth_method]
            );
        });

        await logAccess(authMethod.user_id, room_id, auth_method, 'outside', 'early_arrival_scan', 'success');

        res.json({
            message: 'Early arrival recorded successfully. Please scan inside when class starts for confirmation.',
            student: `${authMethod.first_name} ${authMethod.last_name}`,
            course: `${schedule.subject_code} - ${schedule.subject_name}`,
            scheduled_start: schedule.start_time,
            status: 'early_arrival',
            timestamp: new Date().toISOString(),
            note: 'You must scan inside when class starts to confirm your attendance'
        });

    } catch (error) {
        console.error('Student outside scan error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Cleanup endpoint for marking unconfirmed early arrivals as absent
router.post('/cleanup-early-arrivals', authenticateToken, async (req, res) => {
    try {
        // Mark all Early Arrival records from ended sessions as "Early Scan | Absent"
        const result = await executeQuery(`
            UPDATE ATTENDANCERECORDS ar
            JOIN SESSIONS s ON ar.SCHEDULEID = s.SCHEDULEID 
            SET ar.STATUS = 'Early Scan | Absent',
                ar.SCANTYPE = 'early_arrival_expired',
                ar.UPDATED_AT = CURRENT_TIMESTAMP
            WHERE ar.STATUS = 'Early Arrival' 
            AND s.STATUS = 'ended'
            AND DATE(ar.SCANDATETIME) = CURDATE()
        `);

        res.json({
            message: 'Early arrival cleanup completed',
            records_updated: result.affectedRows || 0
        });

    } catch (error) {
        console.error('Early arrival cleanup error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Test/Simulation endpoints (for testing without physical devices)
router.post('/test/instructor-outside', authenticateToken, async (req, res) => {
    try {
        const { room_id } = req.body;
        
        if (req.user.role !== 'instructor') {
            return res.status(403).json({ message: 'Only instructors can use this endpoint' });
        }

        // Simulate RFID scan with user's ID as identifier
        return await router.handle({
            ...req,
            body: {
                identifier: req.user.id,
                auth_method: 'rfid',
                room_id
            }
        }, res);

    } catch (error) {
        console.error('Test instructor outside scan error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Simulate scan endpoint for testing
router.post('/simulate', [
    authenticateToken,
    body('user_id').isUUID(),
    body('room_id').isUUID(),
    body('subject_id').optional().isUUID(),
    body('auth_method').isIn(['rfid', 'fingerprint']),
    body('location').isIn(['inside', 'outside'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { user_id, room_id, subject_id, auth_method, location } = req.body;

        // Get user info
        const user = await getSingleResult(
            'SELECT USERTYPE, FIRSTNAME, LASTNAME FROM USERS WHERE USERID = ?',
            [user_id]
        );

        if (!user) {
            return res.status(404).json({ message: 'User not found' });
        }

        // Get room info
        const room = await getSingleResult(
            'SELECT ROOMNUMBER, ROOMNAME FROM ROOMS WHERE ROOMID = ?',
            [room_id]
        );

        if (!room) {
            return res.status(404).json({ message: 'Room not found' });
        }

        // Get subject info if provided
        let subjectInfo = null;
        if (subject_id) {
            subjectInfo = await getSingleResult(
                'SELECT SUBJECTCODE, SUBJECTNAME FROM SUBJECTS WHERE SUBJECTID = ?',
                [subject_id]
            );
        }

        // Validate scan configuration
        if (user.USERTYPE === 'student' && location === 'outside') {
            return res.status(400).json({ 
                message: 'Students can only scan inside rooms' 
            });
        }

        // Log the simulation attempt
        await logAccess(user_id, room_id, auth_method, location, 'simulation', 'success');

        // Create attendance record for students or when subject is specified
        if (user.USERTYPE === 'student' || subject_id) {
            // Check if student is enrolled in the subject (only for students)
            if (user.USERTYPE === 'student' && subject_id) {
                console.log(`Checking enrollment for student ${user_id} in subject ${subject_id}`);
                const enrollment = await checkStudentEnrollment(user_id, subject_id);
                console.log('Enrollment check result:', !!enrollment);
                
                if (!enrollment) {
                    console.log(`Student ${user_id} is not enrolled in subject ${subject_id}`);
                    await logAccess(user_id, room_id, auth_method, location, 'attendance', 'denied', 'Student not enrolled in subject');
                    return res.status(403).json({ 
                        message: 'Student is not enrolled in this subject. Please contact your instructor to be enrolled.' 
                    });
                }
                console.log(`Student ${user_id} is enrolled in subject ${subject_id}`);
            }
            
            const now = moment();
            const isLate = now.hour() > 8 || (now.hour() === 8 && now.minute() > 15);
            
            // Get current academic settings
            const academicYear = await getSingleResult(
                'SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = ?',
                ['current_academic_year']
            );
            const semester = await getSingleResult(
                'SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = ?',
                ['current_semester']
            );

            // Handle schedule ID for the attendance record
            let scheduleId = null;
            if (subject_id) {
                // If subject is provided, find or create a schedule for it
                const existingSchedule = await getSingleResult(
                    'SELECT SCHEDULEID FROM CLASSSCHEDULES WHERE SUBJECTID = ? LIMIT 1',
                    [subject_id]
                );
                
                if (existingSchedule) {
                    scheduleId = existingSchedule.SCHEDULEID;
                } else {
                    // Create a default schedule for the subject
                    const { v4: uuidv4 } = require('uuid');
                    scheduleId = uuidv4();
                    
                    await executeQuery(
                        `INSERT INTO CLASSSCHEDULES (
                            SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME,
                            ACADEMICYEAR, SEMESTER
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
                        [
                            scheduleId,
                            subject_id,
                            room_id,
                            'Monday',
                            '08:00:00',
                            '17:00:00',
                            academicYear?.SETTINGVALUE || '2024-2025',
                            semester?.SETTINGVALUE || 'First Semester'
                        ]
                    );
                }
            } else {
                // If no subject provided, find any existing schedule
                const defaultSchedule = await getSingleResult(
                    'SELECT SCHEDULEID FROM CLASSSCHEDULES LIMIT 1'
                );
                scheduleId = defaultSchedule?.SCHEDULEID || null;
            }

            // Create attendance record using ATTENDANCERECORDS table (not ATTENDANCELOGS)
            const { v4: uuidv4 } = require('uuid');
            const attendanceId = uuidv4();
            
            await executeQuery(
                `INSERT INTO ATTENDANCERECORDS (
                    ATTENDANCEID, USERID, SCHEDULEID, SCANTYPE, SCANDATETIME, DATE, TIMEIN,
                    AUTHMETHOD, LOCATION, STATUS, ACADEMICYEAR, SEMESTER, CREATED_AT, UPDATED_AT
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())`,
                [
                    attendanceId,
                    user_id, 
                    scheduleId,
                    'time_in',
                    now.format('YYYY-MM-DD HH:mm:ss'),
                    now.format('YYYY-MM-DD'),
                    now.format('HH:mm:ss'),
                    auth_method,
                    location,
                    isLate ? 'Late' : 'Present',
                    academicYear?.SETTINGVALUE || '2024-2025',
                    semester?.SETTINGVALUE || 'First Semester'
                ]
            );
        }

        // Return success response
        res.json({
            message: 'Scan simulation completed successfully',
            user: `${user.FIRSTNAME} ${user.LASTNAME}`,
            role: user.USERTYPE,
            room: `${room.ROOMNUMBER} - ${room.ROOMNAME}`,
            subject: subjectInfo ? `${subjectInfo.SUBJECTCODE} - ${subjectInfo.SUBJECTNAME}` : 'GENERAL - General Attendance',
            location: location,
            auth_method: auth_method,
            timestamp: new Date().toISOString()
        });

    } catch (error) {
        console.error('Simulate scan error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router; 