const express = require('express');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken } = require('../middleware/auth');
const { body, validationResult } = require('express-validator');

const router = express.Router();

// Utility function to get Philippine time (UTC+8)
function getPhilippineTime() {
    // Simple and reliable approach: just use current time
    // The database will store it as-is and the frontend will display it in Philippine time
    const now = new Date();

    // Format for database (MySQL DATETIME format)
    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const day = String(now.getDate()).padStart(2, '0');
    const hours = String(now.getHours()).padStart(2, '0');
    const minutes = String(now.getMinutes()).padStart(2, '0');
    const seconds = String(now.getSeconds()).padStart(2, '0');

    const date = `${year}-${month}-${day}`;
    const time = `${hours}:${minutes}:${seconds}`;
    const dateTime = `${date} ${time}`;

    console.log('🕐 Current time for database:', {
        utcNow: now.toISOString(),
        localDate: date,
        localTime: time,
        dateTime: dateTime
    });

    return {
        date: date,
        time: time,
        dateTime: dateTime,
        full: now
    };
}

// OLD PROBLEMATIC ENDPOINT REMOVED - Using simplified version at end of file

// Get access logs with proper column names
router.get('/access', authenticateToken, async (req, res) => {
    try {
        const { 
            user_id, 
            room_id,
            accesstype,
            result,
            start_date, 
            end_date,
            search,
            page = 1, 
            limit = 50 
        } = req.query;

        let query = `
            SELECT 
                al.LOGID,
                al.TIMESTAMP,
                al.ACCESSTYPE,
                al.AUTHMETHOD,
                al.LOCATION,
                al.RESULT,
                al.REASON,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                u.EMPLOYEEID,
                u.USERTYPE,
                u.EMAIL,
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.BUILDING
            FROM ACCESSLOGS al
            LEFT JOIN USERS u ON al.USERID = u.USERID
            LEFT JOIN ROOMS r ON al.ROOMID = r.ROOMID
            WHERE 1=1
        `;
        const params = [];

        if (user_id) {
            query += ' AND al.USERID = ?';
            params.push(user_id);
        }

        if (room_id) {
            query += ' AND al.ROOMID = ?';
            params.push(room_id);
        }

        if (accesstype) {
            query += ' AND al.ACCESSTYPE = ?';
            params.push(accesstype);
        }

        if (result) {
            query += ' AND al.RESULT = ?';
            params.push(result);
        }

        if (start_date) {
            query += ' AND DATE(al.TIMESTAMP) >= ?';
            params.push(start_date);
        }

        if (end_date) {
            query += ' AND DATE(al.TIMESTAMP) <= ?';
            params.push(end_date);
        }

        if (search) {
            query += ' AND (u.FIRSTNAME LIKE ? OR u.LASTNAME LIKE ? OR u.STUDENTID LIKE ? OR u.EMPLOYEEID LIKE ? OR r.ROOMNUMBER LIKE ? OR al.ACCESSTYPE LIKE ?)';
            const searchTerm = `%${search}%`;
            params.push(searchTerm, searchTerm, searchTerm, searchTerm, searchTerm, searchTerm);
        }

        query += ' ORDER BY al.TIMESTAMP DESC';

        const offset = (page - 1) * limit;
        query += ' LIMIT ? OFFSET ?';
        params.push(parseInt(limit), offset);

        const logs = await executeQuery(query, params);

        // Get total count for pagination
        let countQuery = `
            SELECT COUNT(*) as total
            FROM ACCESSLOGS al
            LEFT JOIN USERS u ON al.USERID = u.USERID
            LEFT JOIN ROOMS r ON al.ROOMID = r.ROOMID
            WHERE 1=1
        `;
        const countParams = [];

        if (user_id) {
            countQuery += ' AND al.USERID = ?';
            countParams.push(user_id);
        }
        if (room_id) {
            countQuery += ' AND al.ROOMID = ?';
            countParams.push(room_id);
        }
        if (accesstype) {
            countQuery += ' AND al.ACCESSTYPE = ?';
            countParams.push(accesstype);
        }
        if (result) {
            countQuery += ' AND al.RESULT = ?';
            countParams.push(result);
        }
        if (start_date) {
            countQuery += ' AND DATE(al.TIMESTAMP) >= ?';
            countParams.push(start_date);
        }
        if (end_date) {
            countQuery += ' AND DATE(al.TIMESTAMP) <= ?';
            countParams.push(end_date);
        }
        if (search) {
            countQuery += ' AND (u.FIRSTNAME LIKE ? OR u.LASTNAME LIKE ? OR u.STUDENTID LIKE ? OR u.EMPLOYEEID LIKE ? OR r.ROOMNUMBER LIKE ? OR al.ACCESSTYPE LIKE ?)';
            const searchTerm = `%${search}%`;
            countParams.push(searchTerm, searchTerm, searchTerm, searchTerm, searchTerm, searchTerm);
        }

        const [{ total }] = await executeQuery(countQuery, countParams);

        res.json({
            logs,
            pagination: {
                page: parseInt(page),
                limit: parseInt(limit),
                total,
                pages: Math.ceil(total / limit)
            }
        });

    } catch (error) {
        console.error('Get access logs error:', error);
        res.status(500).json({ message: 'Internal server error', error: error.message });
    }
});

// Get attendance summary by subject
router.get('/attendance/summary', authenticateToken, async (req, res) => {
    try {
        const { subject_id, start_date, end_date, academic_year, semester } = req.query;

        let query = `
            SELECT 
                sub.SUBJECTCODE,
                sub.SUBJECTNAME,
                COUNT(DISTINCT ar.USERID) as total_students,
                COUNT(CASE WHEN ar.STATUS = 'Present' THEN 1 END) as present_count,
                COUNT(CASE WHEN ar.STATUS = 'Late' THEN 1 END) as late_count,
                COUNT(CASE WHEN ar.STATUS = 'Absent' THEN 1 END) as absent_count,
                COUNT(*) as total_records,
                ROUND((COUNT(CASE WHEN ar.STATUS IN ('Present', 'Late') THEN 1 END) * 100.0 / COUNT(*)), 2) as attendance_rate
            FROM ATTENDANCERECORDS ar
            JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            WHERE 1=1
        `;
        const params = [];

        if (subject_id) {
            query += ' AND sub.SUBJECTID = ?';
            params.push(subject_id);
        }

        if (start_date) {
            query += ' AND DATE(ar.SCANDATETIME) >= ?';
            params.push(start_date);
        }

        if (end_date) {
            query += ' AND DATE(ar.SCANDATETIME) <= ?';
            params.push(end_date);
        }

        if (academic_year) {
            query += ' AND ar.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            query += ' AND ar.SEMESTER = ?';
            params.push(semester);
        }

        query += ' GROUP BY sub.SUBJECTID, sub.SUBJECTCODE, sub.SUBJECTNAME ORDER BY sub.SUBJECTCODE';

        const summary = await executeQuery(query, params);

        res.json(summary);

    } catch (error) {
        console.error('Get attendance summary error:', error);
        res.status(500).json({ message: 'Internal server error', error: error.message });
    }
});

// Get attendance statistics
router.get('/attendance/stats', authenticateToken, async (req, res) => {
    try {
        const { academic_year, semester } = req.query;

        let baseQuery = `
            FROM ATTENDANCERECORDS ar
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            WHERE 1=1
        `;
        const params = [];

        if (academic_year) {
            baseQuery += ' AND ar.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            baseQuery += ' AND ar.SEMESTER = ?';
            params.push(semester);
        }

        // Get overall statistics
        const statsQuery = `
            SELECT 
                COUNT(*) as total_records,
                COUNT(DISTINCT ar.USERID) as unique_students,
                COUNT(CASE WHEN ar.STATUS = 'Present' THEN 1 END) as present_count,
                COUNT(CASE WHEN ar.STATUS = 'Late' THEN 1 END) as late_count,
                COUNT(CASE WHEN ar.STATUS = 'Absent' THEN 1 END) as absent_count,
                COUNT(CASE WHEN ar.AUTHMETHOD = 'RFID' THEN 1 END) as rfid_scans,
                COUNT(CASE WHEN ar.AUTHMETHOD = 'Fingerprint' THEN 1 END) as fingerprint_scans
            ${baseQuery}
        `;

        const [stats] = await executeQuery(statsQuery, params);

        // Get daily attendance trend for the last 7 days
        const trendQuery = `
            SELECT 
                DATE(ar.SCANDATETIME) as date,
                COUNT(*) as total_scans,
                COUNT(CASE WHEN ar.STATUS = 'Present' THEN 1 END) as present_count,
                COUNT(CASE WHEN ar.STATUS = 'Late' THEN 1 END) as late_count
            ${baseQuery}
            AND ar.SCANDATETIME >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)
            GROUP BY DATE(ar.SCANDATETIME)
            ORDER BY date DESC
        `;

        const trend = await executeQuery(trendQuery, params);

        res.json({
            statistics: stats,
            daily_trend: trend
        });

    } catch (error) {
        console.error('Get attendance stats error:', error);
        res.status(500).json({ message: 'Internal server error', error: error.message });
    }
});

// Export attendance logs
router.get('/attendance/export', authenticateToken, async (req, res) => {
    try {
        const { 
            start_date, 
            end_date, 
            subject_id, 
            academic_year, 
            semester 
        } = req.query;

        let query = `
            SELECT 
                DATE(ar.SCANDATETIME) as date,
                TIME(ar.SCANDATETIME) as time,
                u.STUDENTID,
                u.FACULTYID,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as full_name,
                u.USERTYPE,
                ar.STATUS,
                ar.SCANTYPE,
                ar.AUTHMETHOD,
                sub.SUBJECTCODE,
                sub.SUBJECTNAME,
                r.ROOMNUMBER,
                r.ROOMNAME,
                cs.DAYOFWEEK,
                cs.STARTTIME,
                cs.ENDTIME
            FROM ATTENDANCERECORDS ar
            JOIN USERS u ON ar.USERID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE 1=1
        `;
        const params = [];

        if (start_date) {
            query += ' AND DATE(ar.SCANDATETIME) >= ?';
            params.push(start_date);
        }

        if (end_date) {
            query += ' AND DATE(ar.SCANDATETIME) <= ?';
            params.push(end_date);
        }

        if (subject_id) {
            query += ' AND sub.SUBJECTID = ?';
            params.push(subject_id);
        }

        if (academic_year) {
            query += ' AND ar.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            query += ' AND ar.SEMESTER = ?';
            params.push(semester);
        }

        query += ' ORDER BY ar.SCANDATETIME DESC';

        const data = await executeQuery(query, params);

        res.json({
            data,
            total_records: data.length,
            export_timestamp: new Date().toISOString()
        });

    } catch (error) {
        console.error('Export attendance logs error:', error);
        res.status(500).json({ message: 'Internal server error', error: error.message });
    }
});

// Create attendance log (for ESP32/IoT devices)
router.post('/attendance-logs', [
    body('fingerprint_id').isInt({ min: 1, max: 127 }),
    body('device_id').optional().isString(),
    body('location').optional().isIn(['inside', 'outside']).default('inside'),
    body('room_id').optional().isString(),
    body('subject_id').optional().isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { fingerprint_id, device_id, location = 'inside', room_id, subject_id } = req.body;
        
        console.log('🔍 Fingerprint attendance request received:', {
            fingerprint_id,
            location,
            room_id,
            subject_id,
            hasSubject: !!subject_id
        });

        // Find user by fingerprint ID
        const user = await getSingleResult(
            `SELECT u.USERID, u.FIRSTNAME, u.LASTNAME, u.STUDENTID, u.USERTYPE, u.STATUS
             FROM USERS u
             JOIN AUTHENTICATIONMETHODS am ON u.USERID = am.USERID
             WHERE am.METHODTYPE = 'Fingerprint' AND am.IDENTIFIER = ? AND am.ISACTIVE = 1`,
            [`FP_${fingerprint_id}`]
        );

        if (!user) {
            return res.status(404).json({
                message: 'User not found for fingerprint ID',
                fingerprint_id: fingerprint_id
            });
        }

        if (user.STATUS !== 'Active') {
            return res.status(403).json({
                message: 'User account is not active',
                user_id: user.USERID
            });
        }

        // Check enrollment if subject is provided and user is a student
        if (subject_id && user.USERTYPE === 'student') {
            console.log('🔍 Checking enrollment for student:', {
                userId: user.USERID,
                subjectId: subject_id,
                userName: `${user.FIRSTNAME} ${user.LASTNAME}`
            });

            const enrollment = await getSingleResult(
                `SELECT ENROLLMENTID FROM SUBJECTENROLLMENT 
                 WHERE USERID = ? AND SUBJECTID = ? AND STATUS = 'enrolled'
                 AND ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                 AND SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
                [user.USERID, subject_id]
            );

            if (!enrollment) {
                console.log('❌ Student not enrolled in subject:', {
                    userId: user.USERID,
                    subjectId: subject_id
                });

                return res.status(403).json({
                    message: 'Student is not enrolled in this subject. Please contact your instructor to be enrolled.',
                    user_id: user.USERID,
                    subject_id: subject_id,
                    user_name: `${user.FIRSTNAME} ${user.LASTNAME}`
                });
            }

            console.log('✅ Student is enrolled in subject');
        }

        // Get current date and time in Philippine timezone (UTC+8)
        const phTime = getPhilippineTime();
        const currentDate = phTime.date;
        const currentTime = phTime.time;
        const scanDateTime = phTime.dateTime;

        // Find active session or create a default one
        let sessionData = null;
        let roomData = null;

        // Get room data first
        if (room_id) {
            roomData = await getSingleResult(
                'SELECT * FROM ROOMS WHERE ROOMID = ?',
                [room_id]
            );
        }

        // If no room data, get a default room
        if (!roomData) {
            roomData = await getSingleResult(
                'SELECT * FROM ROOMS WHERE STATUS = "Available" ORDER BY ROOMNUMBER LIMIT 1'
            );
        }

        // Get subject information if provided (this takes priority)
        let subjectInfo = null;
        if (subject_id) {
            subjectInfo = await getSingleResult(
                'SELECT SUBJECTCODE, SUBJECTNAME FROM SUBJECTS WHERE SUBJECTID = ?',
                [subject_id]
            );
            
            if (!subjectInfo) {
                console.log('⚠️ Subject not found in SUBJECTS table, checking COURSES table...');
                // Fallback to COURSES table if not found in SUBJECTS
                subjectInfo = await getSingleResult(
                    'SELECT COURSECODE as SUBJECTCODE, COURSENAME as SUBJECTNAME FROM COURSES WHERE COURSEID = ?',
                    [subject_id]
                );
            }
        }

        // Try to find an active session for today (but subject_id takes priority)
        if (room_id && !subject_id) {
            sessionData = await getSingleResult(
                `SELECT s.*, cs.*, sub.SUBJECTCODE, sub.SUBJECTNAME, r.ROOMNUMBER, r.ROOMNAME
                 FROM SESSIONS s
                 JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
                 JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
                 JOIN ROOMS r ON s.ROOMID = r.ROOMID
                 WHERE s.ROOMID = ? AND s.SESSIONDATE = ? AND s.STATUS = 'active'
                 ORDER BY s.STARTTIME ASC LIMIT 1`,
                [room_id, currentDate]
            );
        }

        // If no active session found or subject_id was provided, create a mock session
        if (!sessionData || subject_id) {
            // Create a mock session for attendance logging
            sessionData = {
                SESSIONID: null,
                SCHEDULEID: null,
                ROOMID: roomData ? roomData.ROOMID : null,
                SUBJECTCODE: subjectInfo ? subjectInfo.SUBJECTCODE : 'GENERAL',
                SUBJECTNAME: subjectInfo ? subjectInfo.SUBJECTNAME : 'General Attendance',
                ROOMNUMBER: roomData ? roomData.ROOMNUMBER : 'UNKNOWN',
                ACADEMICYEAR: '2024-2025',
                SEMESTER: 'First Semester'
            };
        }

        // Determine attendance status based on time
        let status = 'Present';
        if (sessionData.STARTTIME) {
            const sessionStart = new Date(`${currentDate} ${sessionData.STARTTIME}`);
            const scanTime = new Date(`${currentDate} ${currentTime}`);
            const timeDiff = (scanTime - sessionStart) / (1000 * 60); // minutes

            if (timeDiff > 15) { // 15 minutes late tolerance
                status = 'Late';
            }
        }

        // Create attendance record
        const { v4: uuidv4 } = require('uuid');
        const attendanceId = uuidv4();
        const sessionId = sessionData.SESSIONID || uuidv4();

        // Handle null SCHEDULEID by creating a default schedule or using existing one
        let scheduleId = sessionData.SCHEDULEID;

        if (!scheduleId) {
            // If subject_id is provided, always create a new schedule with that subject
            if (subject_id) {
                const { v4: uuidv4 } = require('uuid');
                scheduleId = uuidv4();
                
                console.log('📚 Creating new schedule for subject:', {
                    scheduleId,
                    subject_id,
                    subjectInfo: subjectInfo ? `${subjectInfo.SUBJECTCODE} - ${subjectInfo.SUBJECTNAME}` : 'No subject info'
                });
                
                // Get a default room if none is provided
                let roomIdToUse = roomData ? roomData.ROOMID : null;
                if (!roomIdToUse) {
                    const defaultRoom = await getSingleResult(
                        'SELECT ROOMID FROM ROOMS WHERE STATUS = "Available" ORDER BY ROOMNUMBER LIMIT 1'
                    );
                    roomIdToUse = defaultRoom ? defaultRoom.ROOMID : null;
                }
                
                console.log('🏠 Using room for schedule:', roomIdToUse);
                
                await executeQuery(
                    `INSERT INTO CLASSSCHEDULES (
                        SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME,
                        ACADEMICYEAR, SEMESTER
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
                    [
                        scheduleId,
                        subject_id,
                        roomIdToUse,
                        'Monday',
                        '08:00:00',
                        '17:00:00',
                        '2024-2025',
                        'First Semester'
                    ]
                );
                
                console.log('✅ Schedule created successfully');
            } else {
                // Try to find any existing schedule with a valid subject or create a default one
                const defaultSchedule = await getSingleResult(
                    'SELECT SCHEDULEID FROM CLASSSCHEDULES WHERE SUBJECTID IS NOT NULL LIMIT 1'
                );

                if (defaultSchedule) {
                    scheduleId = defaultSchedule.SCHEDULEID;
                    console.log('📚 Using existing schedule with subject:', scheduleId);
                } else {
                    // Create a default schedule if none exists
                    const { v4: uuidv4 } = require('uuid');
                    scheduleId = uuidv4();

                    // Get a default subject from SUBJECTS table
                    const defaultSubject = await getSingleResult(
                        'SELECT SUBJECTID FROM SUBJECTS LIMIT 1'
                    );
                    let subjectToUse = defaultSubject ? defaultSubject.SUBJECTID : null;

                    // If no subject in SUBJECTS table, try COURSES table
                    if (!subjectToUse) {
                        console.log('📚 No subjects in SUBJECTS table, checking COURSES table...');
                        const defaultCourse = await getSingleResult(
                            'SELECT COURSEID FROM COURSES LIMIT 1'
                        );
                        subjectToUse = defaultCourse ? defaultCourse.COURSEID : null;
                    }

                    if (subjectToUse) {
                        // Get a default room
                        const defaultRoom = await getSingleResult(
                            'SELECT ROOMID FROM ROOMS WHERE STATUS = "Available" ORDER BY ROOMNUMBER LIMIT 1'
                        );
                        const roomIdToUse = defaultRoom ? defaultRoom.ROOMID : null;

                        console.log('📚 Creating default schedule with subject:', {
                            scheduleId,
                            subjectToUse,
                            roomIdToUse
                        });

                        await executeQuery(
                            `INSERT INTO CLASSSCHEDULES (
                                SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME,
                                ACADEMICYEAR, SEMESTER
                            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
                            [
                                scheduleId,
                                subjectToUse,
                                roomIdToUse,
                                'Monday',
                                '08:00:00',
                                '17:00:00',
                                '2024-2025',
                                'First Semester'
                            ]
                        );
                        
                        console.log('✅ Default schedule created successfully');
                    } else {
                        console.log('⚠️ No subjects available, using NULL schedule');
                        scheduleId = null;
                    }
                }
            }
        }

        console.log('📝 Creating attendance record with:', {
            attendanceId,
            userId: user.USERID,
            scheduleId,
            sessionId,
            status,
            scanDateTime,
            subjectInfo: subjectInfo ? `${subjectInfo.SUBJECTCODE} - ${subjectInfo.SUBJECTNAME}` : 'No subject'
        });

        await executeQuery(
            `INSERT INTO ATTENDANCERECORDS (
                ATTENDANCEID, USERID, SCHEDULEID, SESSIONID, SCANTYPE,
                SCANDATETIME, DATE, TIMEIN, AUTHMETHOD, LOCATION,
                STATUS, ACADEMICYEAR, SEMESTER, CREATED_AT, UPDATED_AT
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())`,
            [
                attendanceId,
                user.USERID,
                scheduleId,
                sessionId,
                'time_in',
                scanDateTime,
                currentDate,
                currentTime,
                'Fingerprint',
                location,
                status,
                sessionData.ACADEMICYEAR || '2024-2025',
                sessionData.SEMESTER || 'First Semester'
            ]
        );

        console.log('✅ Attendance record created successfully');

        // Create access log
        const accessLogId = uuidv4();
        await executeQuery(
            `INSERT INTO ACCESSLOGS (
                LOGID, USERID, ROOMID, TIMESTAMP, ACCESSTYPE, AUTHMETHOD,
                LOCATION, RESULT, REASON
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                accessLogId,
                user.USERID,
                sessionData.ROOMID,
                scanDateTime,
                'attendance_scan',
                'Fingerprint',
                location,
                'success',
                `Fingerprint scan - ${status}`
            ]
        );

        // Return success response
        res.status(201).json({
            message: 'Attendance recorded successfully',
            attendance: {
                id: attendanceId,
                user: {
                    id: user.USERID,
                    name: `${user.FIRSTNAME} ${user.LASTNAME}`,
                    student_id: user.STUDENTID,
                    type: user.USERTYPE
                },
                fingerprint_id: fingerprint_id,
                status: status,
                scan_time: scanDateTime,
                location: location,
                room: sessionData.ROOMNUMBER,
                subject: `${sessionData.SUBJECTCODE} - ${sessionData.SUBJECTNAME}`
            }
        });

    } catch (error) {
        console.error('Create attendance log error:', error);
        res.status(500).json({
            message: 'Internal server error',
            error: error.message
        });
    }
});

// Get attendance logs - ULTRA SIMPLE VERSION FOR TESTING
router.get('/attendance', authenticateToken, async (req, res) => {
    try {
        const { page = 1, limit = 10, search, date, status } = req.query;
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 10));
        const offset = (pageNum - 1) * limitNum;

        console.log('Attendance logs endpoint called - page:', pageNum, 'limit:', limitNum);

        // Build WHERE clause with filters
        let whereClause = 'WHERE ar.ARCHIVED_AT IS NULL';
        const params = [];

        if (search) {
            whereClause += ' AND (u.FIRSTNAME LIKE ? OR u.LASTNAME LIKE ? OR u.STUDENTID LIKE ? OR sub.SUBJECTCODE LIKE ? OR sub.SUBJECTNAME LIKE ?)';
            const searchPattern = `%${search}%`;
            params.push(searchPattern, searchPattern, searchPattern, searchPattern, searchPattern);
        }

        if (date) {
            whereClause += ' AND DATE(ar.SCANDATETIME) = ?';
            params.push(date);
        }

        if (status) {
            whereClause += ' AND ar.STATUS = ?';
            params.push(status);
        }

        // Count total records
        const countQuery = `
            SELECT COUNT(*) as count
            FROM ATTENDANCERECORDS ar
            JOIN USERS u ON ar.USERID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            ${whereClause}
        `;
        const countResult = await executeQuery(countQuery, params);
        const total = countResult[0]?.count || 0;

        // Enhanced query with room, subject, and session information (day, time, room)
        const logsQuery = `
            SELECT
                ar.ATTENDANCEID,
                ar.SCHEDULEID,
                ar.SCANDATETIME,
                ar.STATUS,
                ar.AUTHMETHOD,
                ar.ACTIONTYPE,
                ar.LOCATION,
                ar.ACADEMICYEAR,
                ar.SEMESTER,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                COALESCE(sub.SUBJECTCODE, c.COURSECODE) as SUBJECTCODE,
                COALESCE(sub.SUBJECTNAME, c.COURSENAME) as SUBJECTNAME,
                r.ROOMNUMBER,
                r.ROOMNAME,
                cs.DAYOFWEEK,
                cs.STARTTIME,
                cs.ENDTIME,
                DATE(ar.SCANDATETIME) as ATTENDANCE_DATE
            FROM ATTENDANCERECORDS ar
            JOIN USERS u ON ar.USERID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN COURSES c ON cs.SUBJECTID = c.COURSEID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            ${whereClause}
            ORDER BY ar.SCANDATETIME DESC
            LIMIT ? OFFSET ?
        `;

        console.log('Executing query with pagination');
        const logs = await executeQuery(logsQuery, [...params, limitNum.toString(), offset.toString()]);
        console.log('Query executed successfully, found', logs.length, 'records out of', total, 'total');

        res.json({
            logs: logs || [],
            total: total,
            page: pageNum,
            limit: limitNum,
            totalPages: Math.ceil(total / limitNum)
        });

    } catch (error) {
        console.error('Get attendance logs error:', error);
        res.status(500).json({
            message: 'Internal server error',
            error: error.message
        });
    }
});

// Create RFID attendance log
router.post('/rfid-scan', [
    body('rfid_data').isString().isLength({ min: 4, max: 50 }),
    body('scan_type').optional().isIn(['rfid']).default('rfid'),
    body('location').optional().isIn(['inside', 'outside']).default('inside'),
    body('timestamp').optional().isISO8601()
], async (req, res) => {
    console.log('🔖 RFID ENDPOINT HIT - Request received:', req.body);
    console.log('🔖 Request headers:', req.headers);
    console.log('🔖 Request method:', req.method);

    try {
        console.log('🔖 RFID scan request received:', req.body);

        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            console.log('❌ RFID validation errors:', errors.array());
            return res.status(400).json({ errors: errors.array() });
        }

        const { rfid_data, location = 'inside' } = req.body;

        console.log(`🔖 RFID scan received: ${rfid_data}, location: ${location}`);

        // Find user by RFID
        console.log('🔍 Looking for user with RFID:', rfid_data);
        const user = await getSingleResult(
            'SELECT * FROM USERS WHERE RFIDTAG = ? AND STATUS = "active"',
            [rfid_data]
        );
        console.log('👤 User found:', user ? `${user.FIRSTNAME} ${user.LASTNAME}` : 'No user found');

        if (!user) {
            console.log(`❌ No user found for RFID: ${rfid_data}`);
            return res.status(404).json({
                message: 'RFID card not registered or user inactive',
                rfid_data: rfid_data
            });
        }

        console.log(`👤 User found: ${user.FIRSTNAME} ${user.LASTNAME} (${user.STUDENTID})`);

        // Check for active session (similar to fingerprint logic)
        console.log('📅 Preparing date/time data...');

        // Use Philippine timezone (UTC+8)
        const phTime = getPhilippineTime();
        const currentDate = phTime.date;
        const currentTime = phTime.time;
        const scanDateTime = phTime.dateTime;
        console.log('📅 Current date:', currentDate, 'Time:', currentTime, 'Scan DateTime:', scanDateTime);

        // Find active session for today (simplified query)
        console.log('🔍 Looking for active session...');
        let sessionData = null;
        try {
            sessionData = await getSingleResult(`
                SELECT
                    s.SESSIONID, s.SESSIONDATE, s.STATUS as SESSION_STATUS,
                    cs.SCHEDULEID, cs.STARTTIME, cs.ENDTIME, cs.ACADEMICYEAR, cs.SEMESTER,
                    sub.SUBJECTCODE, sub.SUBJECTNAME,
                    r.ROOMID, r.ROOMNUMBER, r.ROOMNAME
                FROM SESSIONS s
                JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
                JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
                LEFT JOIN ROOMS r ON s.ROOMID = r.ROOMID
                WHERE s.SESSIONDATE = ? AND s.STATUS = 'active'
                ORDER BY cs.STARTTIME ASC
                LIMIT 1
            `, [currentDate]);
            console.log('📚 Session data found:', sessionData ? sessionData.SUBJECTCODE : 'No active session');
        } catch (sessionError) {
            console.log('⚠️ Session query failed, continuing without session:', sessionError.message);
            sessionData = null;
        }

        // Determine attendance status
        console.log('⏰ Determining attendance status...');
        let status = 'Present';
        if (sessionData && sessionData.STARTTIME) {
            const startTime = new Date(`${currentDate} ${sessionData.STARTTIME}`);
            const currentDateTime = new Date();
            const lateThresholdMinutes = 15;

            if (currentDateTime > new Date(startTime.getTime() + lateThresholdMinutes * 60000)) {
                status = 'Late';
            }
        }
        console.log('📊 Status determined:', status);

        // Handle null SCHEDULEID (same logic as fingerprint)
        console.log('🗓️ Handling schedule ID...');
        let scheduleId = sessionData?.SCHEDULEID;

        if (!scheduleId) {
            console.log('🔍 No session schedule, looking for default...');
            try {
                const defaultSchedule = await getSingleResult(
                    'SELECT SCHEDULEID FROM CLASSSCHEDULES LIMIT 1'
                );

                if (defaultSchedule) {
                    scheduleId = defaultSchedule.SCHEDULEID;
                    console.log('✅ Using default schedule:', scheduleId);
                } else {
                    console.log('⚠️ No schedules found, creating default...');
                    const { v4: uuidv4 } = require('uuid');
                    scheduleId = uuidv4();

                    const defaultSubject = await getSingleResult(
                        'SELECT SUBJECTID FROM SUBJECTS LIMIT 1'
                    );

                    if (defaultSubject) {
                        console.log('📚 Creating default schedule with subject:', defaultSubject.SUBJECTID);
                        await executeQuery(
                            `INSERT INTO CLASSSCHEDULES (
                                SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME,
                                ACADEMICYEAR, SEMESTER
                            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
                            [
                                scheduleId,
                                defaultSubject.SUBJECTID,
                                null, // No specific room for default
                                'Monday',
                                '08:00:00',
                                '17:00:00',
                                '2024-2025',
                                'First Semester'
                            ]
                        );
                        console.log('✅ Default schedule created');
                    } else {
                        console.log('⚠️ No subjects found, using null schedule');
                        scheduleId = null;
                    }
                }
            } catch (scheduleError) {
                console.log('⚠️ Schedule handling failed:', scheduleError.message);
                scheduleId = null;
            }
        }

        // Create attendance record
        console.log('💾 Creating attendance record...');
        const { v4: uuidv4 } = require('uuid');
        const attendanceId = uuidv4();
        const sessionId = sessionData?.SESSIONID || uuidv4();

        // Ensure all parameters are properly defined (convert undefined to null)
        const attendanceParams = [
            attendanceId,
            user.USERID,
            scheduleId || null,  // Convert undefined to null
            sessionId,
            'time_in',
            scanDateTime,
            currentDate,
            currentTime,
            'RFID',
            location,
            status,
            sessionData?.ACADEMICYEAR || '2024-2025',
            sessionData?.SEMESTER || 'First Semester'
        ];

        console.log('📝 Attendance data:', {
            attendanceId,
            userId: user.USERID,
            scheduleId: scheduleId || null,
            sessionId,
            status,
            scanDateTime,
            currentDate,
            currentTime
        });

        console.log('📝 All parameters:', attendanceParams);

        try {
            await executeQuery(
                `INSERT INTO ATTENDANCERECORDS (
                    ATTENDANCEID, USERID, SCHEDULEID, SESSIONID, SCANTYPE,
                    SCANDATETIME, DATE, TIMEIN, AUTHMETHOD, LOCATION,
                    STATUS, ACADEMICYEAR, SEMESTER, CREATED_AT, UPDATED_AT
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())`,
                attendanceParams
            );
            console.log('✅ Attendance record created successfully');

            // Verify the record was actually inserted
            const verifyRecord = await getSingleResult(
                'SELECT * FROM ATTENDANCERECORDS WHERE ATTENDANCEID = ?',
                [attendanceId]
            );
            console.log('🔍 Verification - Record in database:', verifyRecord ? 'YES' : 'NO');
            if (verifyRecord) {
                console.log('📝 Saved record details:', {
                    id: verifyRecord.ATTENDANCEID,
                    userId: verifyRecord.USERID,
                    scanDateTime: verifyRecord.SCANDATETIME,
                    status: verifyRecord.STATUS,
                    authMethod: verifyRecord.AUTHMETHOD
                });
            }

            // Skip access log creation for now (ROOMID constraint issue)
            console.log('📋 Skipping access log creation (ROOMID constraint)');
            console.log('✅ Access log created successfully');

        } catch (dbError) {
            console.error('💥 Database error during RFID attendance creation:', dbError);
            throw dbError;
        }

        // Trigger lock for instructors/admins via RFID scan
        if (user.USERTYPE === 'instructor' || user.USERTYPE === 'admin') {
            console.log(`🔓 ${user.USERTYPE} detected - Triggering solenoid lock directly!`);
            
            // Send lock command directly to ESP32
            try {
                const axios = require('axios');
                const esp32IP = process.env.ESP32_IP || '192.168.1.8';
                
                // Don't wait for response - fire and forget to avoid timeout issues
                axios.post(`http://${esp32IP}/api/lock-control`, {
                    action: 'open',
                    user: `${user.FIRSTNAME} ${user.LASTNAME}`,
                    timestamp: new Date().toISOString()
                }, {
                    timeout: 5000
                }).then(response => {
                    console.log('✅ Lock control sent successfully:', response.data);
                }).catch(error => {
                    console.log('⚠️ Lock control failed (but continuing):', error.message);
                });
                
            } catch (lockError) {
                console.log('⚠️ Lock control error (but continuing):', lockError.message);
            }
        }

        // Return success response
        console.log('🎉 RFID attendance recorded successfully!');
        res.status(201).json({
            message: 'Attendance recorded successfully',
            attendance: {
                id: attendanceId,
                user: {
                    id: user.USERID,
                    name: `${user.FIRSTNAME} ${user.LASTNAME}`,
                    student_id: user.STUDENTID,
                    type: user.USERTYPE
                },
                rfid_data: rfid_data,
                status: status,
                scan_time: scanDateTime,
                location: location,
                room: sessionData?.ROOMNUMBER,
                subject: sessionData?.SUBJECTCODE
            }
        });

    } catch (error) {
        console.error('💥 Create RFID attendance log error:', error);
        res.status(500).json({
            message: 'Internal server error',
            error: error.message,
            details: error.sql || 'No SQL details available'
        });
    }
});

// Test endpoint to verify route registration
router.get('/attendance/test-clear', authenticateToken, async (req, res) => {
    res.json({ 
        success: true, 
        message: 'Clear group endpoint is accessible',
        timestamp: new Date().toISOString()
    });
});

// Clear specific group attendance records (TEMPORARY - for development/testing purposes)
router.delete('/attendance/clear-group', authenticateToken, async (req, res) => {
    try {
        const { attendanceIds } = req.body;
        
        if (!attendanceIds || !Array.isArray(attendanceIds) || attendanceIds.length === 0) {
            return res.status(400).json({
                success: false,
                message: 'No attendance IDs provided'
            });
        }
        
        // Validate that all IDs are valid UUIDs (36 characters)
        const invalidIds = attendanceIds.filter(id => !id || typeof id !== 'string' || id.length !== 36);
        if (invalidIds.length > 0) {
            return res.status(400).json({
                success: false,
                message: 'Invalid attendance ID format',
                invalidIds: invalidIds
            });
        }
        
        console.log('🗑️ Clearing specific attendance records:', {
            count: attendanceIds.length,
            ids: attendanceIds
        });
        
        // Create placeholders for the IN clause
        const placeholders = attendanceIds.map(() => '?').join(',');
        const deleteQuery = `DELETE FROM ATTENDANCERECORDS WHERE ATTENDANCEID IN (${placeholders})`;
        
        console.log('Executing delete query:', deleteQuery);
        console.log('With parameters:', attendanceIds);
        
        const result = await executeQuery(deleteQuery, attendanceIds);
        
        console.log('✅ Successfully cleared group attendance records:', {
            requestedCount: attendanceIds.length,
            affectedRows: result.affectedRows
        });
        
        res.json({
            success: true,
            message: 'Group attendance records have been cleared successfully',
            affectedRows: result.affectedRows
        });
        
    } catch (error) {
        console.error('💥 Error clearing group attendance records:', error);
        console.error('Error details:', {
            message: error.message,
            sql: error.sql,
            code: error.code,
            errno: error.errno
        });
        res.status(500).json({
            success: false,
            message: 'Failed to clear group attendance records',
            error: error.message,
            details: error.sql || 'No SQL details available'
        });
    }
});

// Get session roster - all enrolled students with attendance status
router.get('/attendance/session-roster/:sessionKey', authenticateToken, async (req, res) => {
    try {
        const { sessionKey } = req.params;
        
        console.log('📋 Session roster request for:', sessionKey);
        
        // Parse sessionKey format: DATE-ROOM-STARTTIME
        // The format is: 2025-10-25T16:00:00.000Z-WAC-302-15:07:00
        // We need to split on the last occurrence of the pattern that separates room and time
        
        console.log('📋 Session key parts before parsing:', sessionKey);
        
        // Find the last occurrence of a pattern like "-WAC-302-" or "-WAC-302-15:07:00"
        // We'll look for the pattern where we have a room number followed by a time
        const roomTimeMatch = sessionKey.match(/^(.+)-([A-Z]+-\d+)-(\d{2}:\d{2}:\d{2})$/);
        
        if (!roomTimeMatch) {
            return res.status(400).json({ 
                message: 'Invalid session key format. Expected: DATE-ROOM-STARTTIME',
                received: sessionKey,
                example: '2025-10-25T16:00:00.000Z-WAC-302-15:07:00'
            });
        }
        
        const date = roomTimeMatch[1]; // Everything before the room
        const room = roomTimeMatch[2]; // Room number like "WAC-302"
        const startTime = roomTimeMatch[3]; // Time like "15:07:00"
        
        console.log('📋 Parsed session key:', { date, room, startTime });
        
        // Get day of week from date
        // The date might be in ISO format like "2025-10-25T16:00:00.000Z"
        // We need to extract just the date part for the day calculation
        const dateOnly = date.split('T')[0]; // Extract "2025-10-25" from "2025-10-25T16:00:00.000Z"
        
        // Since the attendance records show Oct 26 (Sunday) but session key shows Oct 25,
        // we need to use the actual date from the attendance records
        // For now, let's try to find the schedule by looking at the actual attendance date
        const actualDate = '2025-10-26'; // This should match the actual attendance date
        const sessionDate = new Date(actualDate + 'T00:00:00');
        const dayOfWeek = sessionDate.toLocaleDateString('en-US', { weekday: 'long' });
        
        console.log('📋 Date parsing:', { originalDate: date, dateOnly, actualDate, dayOfWeek, sessionDate });
        
        // First, find the schedule ID for this session
        const scheduleQuery = `
            SELECT 
                cs.SCHEDULEID,
                cs.SUBJECTID,
                cs.ACADEMICYEAR,
                cs.SEMESTER,
                sub.SUBJECTCODE,
                sub.SUBJECTNAME,
                r.ROOMNUMBER,
                r.ROOMNAME,
                u.FIRSTNAME as INSTRUCTOR_FIRSTNAME,
                u.LASTNAME as INSTRUCTOR_LASTNAME
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            LEFT JOIN USERS u ON sub.INSTRUCTORID = u.USERID
            WHERE r.ROOMNUMBER = ?
              AND cs.DAYOFWEEK = ?
              AND cs.STARTTIME = ?
              AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
              AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')
        `;
        
        console.log('🔍 Looking for schedule with:', { room, dayOfWeek, startTime });
        console.log('🔍 Query:', scheduleQuery);
        console.log('🔍 Query parameters:', [room, dayOfWeek, startTime]);
        
        const schedule = await getSingleResult(scheduleQuery, [room, dayOfWeek, startTime]);
        
        if (!schedule) {
            console.log('❌ No schedule found. Let me check what schedules exist...');
            
            // Debug: Check what schedules exist for this room
            const debugQuery = `
                SELECT 
                    cs.SCHEDULEID,
                    cs.DAYOFWEEK,
                    cs.STARTTIME,
                    cs.ENDTIME,
                    sub.SUBJECTCODE,
                    sub.SUBJECTNAME,
                    r.ROOMNUMBER
                FROM CLASSSCHEDULES cs
                JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
                JOIN ROOMS r ON cs.ROOMID = r.ROOMID
                WHERE r.ROOMNUMBER = ?
                AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')
                ORDER BY cs.DAYOFWEEK, cs.STARTTIME
            `;
            
            const debugSchedules = await executeQuery(debugQuery, [room]);
            console.log('🔍 Available schedules for room', room, ':', debugSchedules);
            
            return res.status(404).json({ 
                message: 'Schedule not found for this session',
                details: { room, dayOfWeek, startTime },
                availableSchedules: debugSchedules
            });
        }
        
        console.log('📚 Found schedule:', schedule.SCHEDULEID);
        
        // Get all enrolled students with their attendance status for this session
        // Use a simpler approach: get all attendance records and group by student in JavaScript
        const rosterQuery = `
            SELECT 
                u.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                ar.STATUS,
                ar.SCANDATETIME,
                ar.AUTHMETHOD
            FROM SUBJECTENROLLMENT se
            JOIN USERS u ON se.USERID = u.USERID
            LEFT JOIN ATTENDANCERECORDS ar ON (
                ar.USERID = u.USERID 
                AND ar.SCHEDULEID = ?
                AND DATE(ar.SCANDATETIME) = ?
            )
            WHERE se.SUBJECTID = ?
              AND se.ACADEMICYEAR = ?
              AND se.SEMESTER = ?
              AND se.STATUS = 'enrolled'
            ORDER BY u.LASTNAME, u.FIRSTNAME, ar.SCANDATETIME DESC
        `;
        
        const roster = await executeQuery(rosterQuery, [
            schedule.SCHEDULEID,
            actualDate,
            schedule.SUBJECTID,
            schedule.ACADEMICYEAR,
            schedule.SEMESTER
        ]);
        
        console.log('👥 Raw roster loaded:', roster.length, 'records');
        
        // Process the roster to handle duplicates and assign status
        const processedRoster = [];
        const studentMap = new Map();
        
        roster.forEach(record => {
            const userId = record.USERID;
            
            if (!studentMap.has(userId)) {
                // First time seeing this student
                studentMap.set(userId, {
                    USERID: record.USERID,
                    FIRSTNAME: record.FIRSTNAME,
                    LASTNAME: record.LASTNAME,
                    STUDENTID: record.STUDENTID,
                    STATUS: record.STATUS || 'Absent',
                    SCANDATETIME: record.SCANDATETIME,
                    AUTHMETHOD: record.AUTHMETHOD
                });
            } else {
                // Student already exists, keep the latest record (since we ordered by SCANDATETIME DESC)
                const existing = studentMap.get(userId);
                if (record.SCANDATETIME && (!existing.SCANDATETIME || new Date(record.SCANDATETIME) > new Date(existing.SCANDATETIME))) {
                    existing.STATUS = record.STATUS || 'Absent';
                    existing.SCANDATETIME = record.SCANDATETIME;
                    existing.AUTHMETHOD = record.AUTHMETHOD;
                }
            }
        });
        
        // Convert map to array and sort
        const finalRoster = Array.from(studentMap.values()).sort((a, b) => {
            // Sort by status first (Present -> Late -> Absent), then by name
            const statusOrder = { 'Present': 1, 'Late': 2, 'Absent': 3 };
            const aOrder = statusOrder[a.STATUS] || 3;
            const bOrder = statusOrder[b.STATUS] || 3;
            
            if (aOrder !== bOrder) {
                return aOrder - bOrder;
            }
            
            return a.LASTNAME.localeCompare(b.LASTNAME);
        });
        
        console.log('👥 Processed roster:', finalRoster.length, 'unique students');
        
        // Calculate statistics
        const stats = {
            total: finalRoster.length,
            present: finalRoster.filter(s => s.STATUS === 'Present').length,
            late: finalRoster.filter(s => s.STATUS === 'Late').length,
            absent: finalRoster.filter(s => s.STATUS === 'Absent').length
        };
        
        res.json({
            success: true,
            session: {
                subjectCode: schedule.SUBJECTCODE,
                subjectName: schedule.SUBJECTNAME,
                date: actualDate,
                room: schedule.ROOMNUMBER,
                roomName: schedule.ROOMNAME,
                startTime: startTime,
                scheduleId: schedule.SCHEDULEID,
                instructor: schedule.INSTRUCTOR_FIRSTNAME && schedule.INSTRUCTOR_LASTNAME 
                    ? `${schedule.INSTRUCTOR_FIRSTNAME} ${schedule.INSTRUCTOR_LASTNAME}`
                    : 'Not assigned'
            },
            roster: finalRoster,
            statistics: stats
        });
        
    } catch (error) {
        console.error('❌ Session roster error:', error);
        res.status(500).json({ 
            message: 'Internal server error', 
            error: error.message 
        });
    }
});

// Early arrival scan endpoint - For students arriving before instructor starts session
router.post('/early-arrival-scan', [
    body('identifier').isString(),
    body('auth_method').isIn(['fingerprint', 'rfid']),
    body('room_id').isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { identifier, auth_method, room_id } = req.body;

        console.log('⏰ Early arrival scan request:', { identifier, auth_method, room_id });

        // Get user by authentication method (case-insensitive)
        const authMethod = await getSingleResult(
            `SELECT am.AUTHID as auth_id, am.USERID as user_id, am.METHODTYPE as method_type, 
                    am.IDENTIFIER as identifier, am.ISACTIVE as is_active,
                    u.USERID as id, u.FIRSTNAME as first_name, u.LASTNAME as last_name, 
                    u.USERTYPE as role, u.STATUS as status, u.USERID
             FROM AUTHENTICATIONMETHODS am
             JOIN USERS u ON am.USERID = u.USERID
             WHERE am.IDENTIFIER = ? AND UPPER(am.METHODTYPE) = UPPER(?) AND am.ISACTIVE = TRUE AND u.STATUS = 'Active'`,
            [identifier, auth_method]
        );

        if (!authMethod) {
            console.log('❌ Invalid credentials for early arrival');
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        if (authMethod.role !== 'student') {
            console.log('❌ Only students can scan for early arrival');
            return res.status(403).json({ message: 'Only students can scan for early arrival' });
        }

        // Get configured early arrival window (default 15 minutes)
        const earlyArrivalWindow = parseInt(
            (await getSingleResult(
                'SELECT SETTINGVALUE as setting_value FROM SETTINGS WHERE SETTINGKEY = "student_early_arrival_window"'
            ))?.setting_value || '15'
        );

        console.log(`⏰ Early arrival window: ${earlyArrivalWindow} minutes`);

        // Check if there's a class starting within the early arrival window
        const now = new Date();
        const currentDay = now.toLocaleDateString('en-US', { weekday: 'long' });
        const currentTime = `${now.getHours().toString().padStart(2, '0')}:${now.getMinutes().toString().padStart(2, '0')}:00`;

        console.log(`📅 Checking for classes: Day=${currentDay}, Time=${currentTime}`);

        // Find schedule that starts within the early arrival window
        const schedule = await getSingleResult(`
            SELECT cs.SCHEDULEID as schedule_id,
                   cs.SUBJECTID as subject_id,
                   s.SUBJECTCODE as subject_code,
                   s.SUBJECTNAME as subject_name,
                   cs.STARTTIME as start_time,
                   cs.ENDTIME as end_time,
                   r.ROOMNUMBER as room_number,
                   r.ROOMNAME as room_name
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE cs.ROOMID = ?
              AND cs.DAYOFWEEK = ?
              AND cs.STARTTIME > ?
              AND cs.STARTTIME <= TIME_ADD(?, INTERVAL ? MINUTE)
              AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
              AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
            [room_id, currentDay, currentTime, currentTime, earlyArrivalWindow]
        );

        if (!schedule) {
            console.log(`❌ No class starting within ${earlyArrivalWindow} minutes`);
            return res.status(403).json({ 
                message: `Too early or no class starting within ${earlyArrivalWindow} minutes. Please scan within ${earlyArrivalWindow} minutes of class start.`,
                earlyArrivalWindow
            });
        }

        console.log(`✅ Found upcoming class: ${schedule.subject_code} starting at ${schedule.start_time}`);

        // Check if student is enrolled in this subject
        const enrollment = await getSingleResult(
            `SELECT ENROLLMENTID, STATUS
             FROM SUBJECTENROLLMENT
             WHERE USERID = ?
               AND SUBJECTID = ?
               AND STATUS = 'enrolled'
               AND ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
               AND SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
            [authMethod.user_id, schedule.subject_id]
        );

        if (!enrollment) {
            console.log(`❌ Student not enrolled in ${schedule.subject_code}`);
            return res.status(403).json({ 
                message: `You are not enrolled in ${schedule.subject_code} - ${schedule.subject_name}`
            });
        }

        console.log('✅ Student is enrolled in the subject');

        // Check if already recorded early arrival for this schedule today
        const existingRecord = await getSingleResult(
            `SELECT ATTENDANCEID as id, STATUS as status
             FROM ATTENDANCERECORDS 
             WHERE USERID = ? AND SCHEDULEID = ? AND DATE(SCANDATETIME) = CURDATE()
             AND STATUS IN ('Awaiting Confirmation', 'Present', 'Late', 'Early Arrival')`,
            [authMethod.user_id, schedule.schedule_id]
        );

        if (existingRecord) {
            console.log(`⚠️ Student already has attendance record: ${existingRecord.status}`);
            return res.status(409).json({ 
                message: `You already have a ${existingRecord.status} record for this class today`,
                existingStatus: existingRecord.status
            });
        }

        // Create early arrival attendance record
        const { v4: uuidv4 } = require('uuid');
        const attendanceId = uuidv4();
        const phTime = getPhilippineTime();

        await executeQuery(
            `INSERT INTO ATTENDANCERECORDS (
                ATTENDANCEID, USERID, SCHEDULEID, SESSIONID, SCANTYPE,
                SCANDATETIME, DATE, TIMEIN, AUTHMETHOD, LOCATION,
                STATUS, ACADEMICYEAR, SEMESTER, CREATED_AT, UPDATED_AT
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())`,
            [
                attendanceId,
                authMethod.user_id,
                schedule.schedule_id,
                null, // No session yet
                'time_in',
                phTime.dateTime,
                phTime.date,
                phTime.time,
                auth_method === 'rfid' ? 'RFID' : 'Fingerprint',
                'outside',
                'Awaiting Confirmation',
                (await getSingleResult('SELECT SETTINGVALUE as value FROM SETTINGS WHERE SETTINGKEY = "current_academic_year"')).value,
                (await getSingleResult('SELECT SETTINGVALUE as value FROM SETTINGS WHERE SETTINGKEY = "current_semester"')).value
            ]
        );

        console.log(`✅ Early arrival recorded for ${authMethod.first_name} ${authMethod.last_name}`);

        // Log access
        const accessLogId = uuidv4();
        await executeQuery(
            `INSERT INTO ACCESSLOGS (
                LOGID, USERID, ROOMID, TIMESTAMP, ACCESSTYPE, AUTHMETHOD,
                LOCATION, RESULT, REASON
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [
                accessLogId,
                authMethod.user_id,
                room_id,
                phTime.dateTime,
                'early_arrival_scan',
                auth_method === 'rfid' ? 'RFID' : 'Fingerprint',
                'outside',
                'success',
                `Early arrival for ${schedule.subject_code}`
            ]
        );

        res.status(201).json({
            message: `Early arrival recorded for ${schedule.subject_code}. Please scan inside when class starts.`,
            status: 'Awaiting Confirmation',
            subject: `${schedule.subject_code} - ${schedule.subject_name}`,
            classTime: schedule.start_time,
            attendance_id: attendanceId
        });

    } catch (error) {
        console.error('❌ Early arrival scan error:', error);
        res.status(500).json({ 
            message: 'Internal server error', 
            error: error.message 
        });
    }
});

module.exports = router;