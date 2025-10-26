const express = require('express');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireInstructor } = require('../middleware/auth');

const router = express.Router();

// Get dashboard overview
router.get('/overview', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { academic_year, semester } = req.query;

        let whereClause = '';
        const params = [];

        if (academic_year) {
            whereClause += ' AND academic_year = ?';
            params.push(academic_year);
        }

        if (semester) {
            whereClause += ' AND semester = ?';
            params.push(semester);
        }

        // Active sessions today
        const activeSessions = await executeQuery(`
            SELECT COUNT(*) as count
            FROM SESSIONS
            WHERE status = 'active' AND session_date = CURDATE() AND ARCHIVED_AT IS NULL
        `);

        // Total attendance today
        const todayAttendance = await executeQuery(`
            SELECT 
                COUNT(*) as total_scans,
                COUNT(CASE WHEN status = 'present' THEN 1 END) as present_count,
                COUNT(CASE WHEN status = 'late' THEN 1 END) as late_count,
                COUNT(DISTINCT user_id) as unique_students
            FROM ATTENDANCERECORDS
            WHERE DATE(scan_datetime) = CURDATE()
        `);

        // Room status
        const roomStatus = await executeQuery(`
            SELECT 
                door_status,
                COUNT(*) as count
            FROM ROOMS
            WHERE is_active = TRUE AND ARCHIVED_AT IS NULL
            GROUP BY door_status
        `);

        // Device status
        const deviceStatus = await executeQuery(`
            SELECT 
                status,
                COUNT(*) as count
            FROM DEVICES
            WHERE is_active = TRUE
            GROUP BY status
        `);

        // Recent access logs (last 10)
        const recentLogs = await executeQuery(`
            SELECT 
                al.scan_datetime,
                al.action,
                al.result,
                CONCAT(u.first_name, ' ', u.last_name) as user_name,
                u.role,
                r.room_number,
                r.room_name
            FROM ACCESSLOGS al
            LEFT JOIN USERS u ON al.user_id = u.id
            JOIN ROOMS r ON al.room_id = r.id
            ORDER BY al.scan_datetime DESC
            LIMIT 10
        `);

        // User statistics
        const userStats = await executeQuery(`
            SELECT 
                role,
                status,
                COUNT(*) as count
            FROM USERS
            WHERE 1=1 AND ARCHIVED_AT IS NULL ${whereClause}
            GROUP BY role, status
        `, params);

        // Weekly attendance trend
        const weeklyTrend = await executeQuery(`
            SELECT 
                DATE(scan_datetime) as date,
                COUNT(*) as attendance_count,
                COUNT(DISTINCT user_id) as unique_students
            FROM ATTENDANCERECORDS
            WHERE scan_datetime >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)
            GROUP BY DATE(scan_datetime)
            ORDER BY date
        `);

        res.json({
            active_sessions: activeSessions[0].count,
            today_attendance: todayAttendance[0],
            room_status: roomStatus,
            device_status: deviceStatus,
            recent_logs: recentLogs,
            user_stats: userStats,
            weekly_trend: weeklyTrend
        });

    } catch (error) {
        console.error('Get dashboard overview error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get real-time status
router.get('/realtime', authenticateToken, requireInstructor, async (req, res) => {
    try {
        // Active sessions with current attendance
        const activeSessions = await executeQuery(`
            SELECT 
                s.id,
                s.start_time,
                CONCAT(u.first_name, ' ', u.last_name) as instructor_name,
                c.course_code,
                c.course_name,
                r.room_number,
                r.room_name,
                r.door_status,
                COUNT(ar.id) as current_attendance
            FROM SESSIONS s
            JOIN USERS u ON s.instructor_id = u.id
            JOIN CLASSSCHEDULES cs ON s.schedule_id = cs.id
            JOIN COURSES c ON cs.course_id = c.id
            JOIN ROOMS r ON s.room_id = r.id
            LEFT JOIN ATTENDANCERECORDS ar ON s.schedule_id = ar.schedule_id 
                AND DATE(ar.scan_datetime) = s.session_date
                AND ar.scan_type = 'time_in'
            WHERE s.status = 'active'
            GROUP BY s.id
            ORDER BY s.start_time DESC
        `);

        // Latest scan activities (last 5 minutes)
        const recentScans = await executeQuery(`
            SELECT 
                al.scan_datetime,
                al.action,
                al.result,
                al.auth_method,
                al.location,
                CONCAT(u.first_name, ' ', u.last_name) as user_name,
                u.role,
                r.room_number
            FROM ACCESSLOGS al
            LEFT JOIN USERS u ON al.user_id = u.id
            JOIN ROOMS r ON al.room_id = r.id
            WHERE al.scan_datetime >= DATE_SUB(NOW(), INTERVAL 5 MINUTE)
            ORDER BY al.scan_datetime DESC
            LIMIT 20
        `);

        // System alerts (offline devices, failed scans, etc.)
        const alerts = await executeQuery(`
            SELECT 
                'device_offline' as alert_type,
                CONCAT('Device ', d.device_name, ' in ', r.room_number, ' is offline') as message,
                d.last_heartbeat as timestamp,
                'warning' as severity
            FROM DEVICES d
            JOIN ROOMS r ON d.room_id = r.id
            WHERE d.status = 'offline' 
            AND d.is_active = TRUE
            AND d.last_heartbeat < DATE_SUB(NOW(), INTERVAL 10 MINUTE)
            
            UNION ALL
            
            SELECT 
                'failed_scans' as alert_type,
                CONCAT('Multiple failed scan attempts in ', r.room_number) as message,
                MAX(al.scan_datetime) as timestamp,
                'error' as severity
            FROM ACCESSLOGS al
            JOIN ROOMS r ON al.room_id = r.id
            WHERE al.result = 'denied'
            AND al.scan_datetime >= DATE_SUB(NOW(), INTERVAL 15 MINUTE)
            GROUP BY al.room_id
            HAVING COUNT(*) >= 3
            
            ORDER BY timestamp DESC
            LIMIT 10
        `);

        res.json({
            active_sessions: activeSessions,
            recent_scans: recentScans,
            alerts: alerts,
            timestamp: new Date().toISOString()
        });

    } catch (error) {
        console.error('Get realtime status error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get attendance analytics
router.get('/analytics/attendance', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { period = 'week', academic_year, semester } = req.query;

        let dateClause = '';
        let groupBy = '';
        
        switch (period) {
            case 'day':
                dateClause = 'WHERE scan_datetime >= DATE_SUB(CURDATE(), INTERVAL 1 DAY)';
                groupBy = 'HOUR(scan_datetime)';
                break;
            case 'week':
                dateClause = 'WHERE scan_datetime >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)';
                groupBy = 'DATE(scan_datetime)';
                break;
            case 'month':
                dateClause = 'WHERE scan_datetime >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)';
                groupBy = 'DATE(scan_datetime)';
                break;
            default:
                dateClause = 'WHERE scan_datetime >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)';
                groupBy = 'DATE(scan_datetime)';
        }

        const params = [];
        if (academic_year) {
            dateClause += ' AND academic_year = ?';
            params.push(academic_year);
        }
        if (semester) {
            dateClause += ' AND semester = ?';
            params.push(semester);
        }

        // Attendance trend
        const attendanceTrend = await executeQuery(`
            SELECT 
                ${groupBy} as period,
                COUNT(*) as total_scans,
                COUNT(CASE WHEN status = 'present' THEN 1 END) as present_count,
                COUNT(CASE WHEN status = 'late' THEN 1 END) as late_count,
                COUNT(DISTINCT user_id) as unique_students
            FROM ATTENDANCERECORDS
            ${dateClause}
            GROUP BY ${groupBy}
            ORDER BY period
        `, params);

        // Top courses by attendance
        const topCourses = await executeQuery(`
            SELECT 
                c.course_code,
                c.course_name,
                COUNT(*) as attendance_count,
                COUNT(DISTINCT ar.user_id) as unique_students
            FROM ATTENDANCERECORDS ar
            JOIN CLASSSCHEDULES cs ON ar.schedule_id = cs.id
            JOIN COURSES c ON cs.course_id = c.id
            ${dateClause}
            GROUP BY c.id, c.course_code, c.course_name
            ORDER BY attendance_count DESC
            LIMIT 10
        `, params);

        // Attendance by status
        const statusBreakdown = await executeQuery(`
            SELECT 
                status,
                COUNT(*) as count,
                ROUND(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM ATTENDANCERECORDS ${dateClause}), 2) as percentage
            FROM ATTENDANCERECORDS
            ${dateClause}
            GROUP BY status
            ORDER BY count DESC
        `, params);

        res.json({
            period,
            attendance_trend: attendanceTrend,
            top_courses: topCourses,
            status_breakdown: statusBreakdown
        });

    } catch (error) {
        console.error('Get attendance analytics error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get system health metrics
router.get('/system/health', authenticateToken, requireInstructor, async (req, res) => {
    try {
        // Database metrics
        const dbStats = await executeQuery(`
            SELECT 
                'users' as table_name, COUNT(*) as record_count FROM USERS WHERE status = 'active'
            UNION ALL
            SELECT 'rooms', COUNT(*) FROM ROOMS WHERE is_active = TRUE
            UNION ALL
            SELECT 'devices', COUNT(*) FROM DEVICES WHERE is_active = TRUE
            UNION ALL
            SELECT 'active_sessions', COUNT(*) FROM SESSIONS WHERE status = 'active'
            UNION ALL
            SELECT 'today_attendance', COUNT(*) FROM ATTENDANCERECORDS WHERE DATE(scan_datetime) = CURDATE()
        `);

        // Device health
        const deviceHealth = await executeQuery(`
            SELECT 
                device_type,
                COUNT(*) as total_devices,
                COUNT(CASE WHEN status = 'online' THEN 1 END) as online_devices,
                COUNT(CASE WHEN status = 'offline' THEN 1 END) as offline_devices,
                COUNT(CASE WHEN last_heartbeat < DATE_SUB(NOW(), INTERVAL 5 MINUTE) THEN 1 END) as stale_devices
            FROM DEVICES
            WHERE is_active = TRUE
            GROUP BY device_type
        `);

        // Recent errors
        const recentErrors = await executeQuery(`
            SELECT 
                COUNT(*) as error_count,
                reason,
                MAX(scan_datetime) as last_occurrence
            FROM ACCESSLOGS
            WHERE result = 'error' 
            AND scan_datetime >= DATE_SUB(NOW(), INTERVAL 1 HOUR)
            GROUP BY reason
            ORDER BY error_count DESC
            LIMIT 5
        `);

        // System uptime metrics
        const uptimeMetrics = await executeQuery(`
            SELECT 
                COUNT(CASE WHEN result = 'success' THEN 1 END) as successful_operations,
                COUNT(CASE WHEN result = 'denied' THEN 1 END) as denied_operations,
                COUNT(CASE WHEN result = 'error' THEN 1 END) as error_operations,
                COUNT(*) as total_operations
            FROM ACCESSLOGS
            WHERE scan_datetime >= DATE_SUB(NOW(), INTERVAL 24 HOUR)
        `);

        const uptime = uptimeMetrics[0];
        const successRate = uptime.total_operations > 0 ? 
            ((uptime.successful_operations / uptime.total_operations) * 100).toFixed(2) : 100;

        res.json({
            database_stats: dbStats.reduce((acc, stat) => {
                acc[stat.table_name] = stat.record_count;
                return acc;
            }, {}),
            device_health: deviceHealth,
            recent_errors: recentErrors,
            uptime_metrics: {
                ...uptime,
                success_rate: parseFloat(successRate)
            },
            last_updated: new Date().toISOString()
        });

    } catch (error) {
        console.error('Get system health error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Simple stats endpoint for frontend compatibility
router.get('/stats', authenticateToken, async (req, res) => {
    try {
        // Get total users
        const totalUsersResult = await getSingleResult(
            'SELECT COUNT(*) as count FROM USERS WHERE STATUS = "Active" AND ARCHIVED_AT IS NULL'
        );
        const totalUsers = totalUsersResult?.count || 0;

        // Get total attendance records today
        const today = new Date().toISOString().split('T')[0];
        const todayAttendanceResult = await getSingleResult(
            'SELECT COUNT(*) as count FROM ATTENDANCERECORDS WHERE DATE(SCANDATETIME) = ?',
            [today]
        );
        const todayAttendance = todayAttendanceResult?.count || 0;

        // Get total rooms
        const totalRoomsResult = await getSingleResult(
            'SELECT COUNT(*) as count FROM ROOMS WHERE STATUS = "Available" AND ARCHIVED_AT IS NULL'
        );
        const totalRooms = totalRoomsResult?.count || 0;

        // Get current day and time for active sessions calculation
        const currentDay = new Date().toLocaleDateString('en-US', { weekday: 'long' });
        const currentTime = new Date().toTimeString().slice(0, 8);
        
        // Count active sessions based on current time and schedules
        const activeSessionsResult = await getSingleResult(`
            SELECT COUNT(*) as count 
            FROM CLASSSCHEDULES cs
            WHERE cs.DAYOFWEEK = ?
            AND cs.ACADEMICYEAR = '2025-2026'
            AND cs.SEMESTER = 'First Semester'
            AND TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME
            AND cs.ARCHIVED_AT IS NULL
        `, [currentDay]);
        const activeSessions = activeSessionsResult?.count || 0;

        // Get today's sessions from schedules (both active sessions from SESSIONS table and scheduled classes)
        const todaySessions = await executeQuery(`
            SELECT 
                cs.SCHEDULEID,
                sub.SUBJECTCODE,
                sub.SUBJECTNAME,
                r.ROOMNUMBER,
                r.ROOMNAME,
                cs.STARTTIME,
                cs.ENDTIME,
                CASE 
                    WHEN TIME(NOW()) BETWEEN cs.STARTTIME AND cs.ENDTIME THEN 'active'
                    WHEN TIME(NOW()) < cs.STARTTIME THEN 'scheduled'
                    ELSE 'ended'
                END as STATUS,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            JOIN USERS u ON sub.INSTRUCTORID = u.USERID
            WHERE cs.DAYOFWEEK = ?
            AND cs.ACADEMICYEAR = '2025-2026'
            AND cs.SEMESTER = 'First Semester'
            AND cs.ARCHIVED_AT IS NULL
            AND sub.ARCHIVED_AT IS NULL
            AND r.ARCHIVED_AT IS NULL
            ORDER BY cs.STARTTIME
        `, [currentDay]);

        res.json({
            totalUsers,
            todayAttendance,
            totalRooms,
            activeSessions,
            todaySessions: todaySessions.map(session => ({
                subject_name: session.SUBJECTNAME,
                room_number: session.ROOMNUMBER,
                start_time: session.STARTTIME,
                end_time: session.ENDTIME,
                status: session.STATUS,
                instructor_name: session.instructor_name
            }))
        });

    } catch (error) {
        console.error('Dashboard stats error:', error);
        res.status(500).json({
            message: 'Failed to fetch dashboard statistics',
            error: error.message
        });
    }
});

// Simple recent activity endpoint for frontend compatibility
router.get('/recent-activity', authenticateToken, async (req, res) => {
    try {
        const limit = parseInt(req.query.limit) || 10;

        // Get recent attendance records with subject information
        const recentAttendance = await executeQuery(`
            SELECT
                ar.ATTENDANCEID,
                ar.SCANDATETIME,
                ar.SCANTYPE,
                ar.STATUS,
                ar.AUTHMETHOD,
                ar.LOCATION,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                u.USERTYPE,
                sub.SUBJECTNAME,
                sub.SUBJECTCODE,
                r.ROOMNUMBER
            FROM ATTENDANCERECORDS ar
            JOIN USERS u ON ar.USERID = u.USERID
            JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            ORDER BY ar.SCANDATETIME DESC
            LIMIT ?
        `, [limit.toString()]);

        // Format the data for frontend consumption
        const formattedActivity = recentAttendance.map(record => {
            const scanTime = new Date(record.SCANDATETIME);
            const timeStr = scanTime.toLocaleTimeString('en-US', { 
                hour: '2-digit', 
                minute: '2-digit',
                hour12: true 
            });
            
            const action = record.SCANTYPE === 'time_in' ? 'checked in' : 'checked out';
            const statusText = record.STATUS === 'Present' ? 'on time' : 
                             record.STATUS === 'Late' ? 'late' : 
                             record.STATUS === 'Absent' ? 'absent' : record.STATUS;
            
            return {
                description: `${record.FIRSTNAME} ${record.LASTNAME} ${action} for ${record.SUBJECTCODE} (${statusText})`,
                time: timeStr
            };
        });

        res.json(formattedActivity);

    } catch (error) {
        console.error('Recent activity error:', error);
        res.status(500).json({
            message: 'Failed to fetch recent activity',
            error: error.message
        });
    }
});

module.exports = router;
