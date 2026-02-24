const express = require('express');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin } = require('../middleware/auth');
const backupService = require('../services/backupService');

const router = express.Router();

// Get archive dashboard statistics
router.get('/dashboard', authenticateToken, requireAdmin, async (req, res) => {
    try {
        // Check if ARCHIVED_AT column exists in ACCESSLOGS
        let accessLogsHasArchivedColumn = false;
        try {
            const columnCheck = await getSingleResult(`
                SELECT COUNT(*) as count 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = DATABASE() 
                AND TABLE_NAME = 'ACCESSLOGS' 
                AND COLUMN_NAME = 'ARCHIVED_AT'
            `);
            accessLogsHasArchivedColumn = columnCheck && columnCheck.count > 0;
        } catch (error) {
            // Column doesn't exist, ignore
        }

        const [subjects, rooms, schedules, users, attendance] = await Promise.all([
            executeQuery(`SELECT COUNT(*) as count FROM SUBJECTS WHERE ARCHIVED_AT IS NOT NULL`),
            executeQuery(`SELECT COUNT(*) as count FROM ROOMS WHERE ARCHIVED_AT IS NOT NULL`),
            executeQuery(`SELECT COUNT(*) as count FROM CLASSSCHEDULES WHERE ARCHIVED_AT IS NOT NULL`),
            executeQuery(`SELECT COUNT(*) as count FROM USERS WHERE ARCHIVED_AT IS NOT NULL AND USERTYPE = 'student'`),
            executeQuery(`SELECT COUNT(*) as count FROM ATTENDANCERECORDS WHERE ARCHIVED_AT IS NOT NULL`)
        ]);

        // Get archived access logs count if column exists
        let accessLogsCount = 0;
        if (accessLogsHasArchivedColumn) {
            try {
                const accessLogsResult = await getSingleResult(`
                    SELECT COUNT(*) as count 
                    FROM ACCESSLOGS 
                    WHERE ARCHIVED_AT IS NOT NULL 
                    AND ACCESSTYPE = 'attendance_scan'
                `);
                accessLogsCount = accessLogsResult?.count || 0;
            } catch (error) {
                console.warn('Could not get archived access logs count:', error.message);
            }
        }

        // Total attendance count includes both attendance records and access logs
        const totalAttendanceCount = attendance[0].count + accessLogsCount;

        const stats = [
            { category: 'subjects', count: subjects[0].count },
            { category: 'rooms', count: rooms[0].count },
            { category: 'schedules', count: schedules[0].count },
            { category: 'users', count: users[0].count },
            { category: 'attendance', count: totalAttendanceCount },
            { category: 'backups', count: (await backupService.listArchivedBackups()).length }
        ];

        res.json({ stats });
    } catch (error) {
        console.error('Get archive dashboard error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive subjects by academic year/semester
router.post('/subjects', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, reason } = req.body;
        const adminId = req.user.id;

        if (!academic_year || !semester) {
            return res.status(400).json({ message: 'Academic year and semester are required' });
        }

        const result = await executeQuery(`
            UPDATE SUBJECTS 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE ACADEMICYEAR = ? AND SEMESTER = ? AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, academic_year, semester]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} subjects`
        });
    } catch (error) {
        console.error('Archive subjects error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive rooms
router.post('/rooms', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { room_ids, reason } = req.body;
        const adminId = req.user.id;

        if (!room_ids || !Array.isArray(room_ids) || room_ids.length === 0) {
            return res.status(400).json({ message: 'Room IDs are required' });
        }

        const result = await executeQuery(`
            UPDATE ROOMS 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE ROOMID IN (${room_ids.map(() => '?').join(',')}) AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, ...room_ids]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} rooms`
        });
    } catch (error) {
        console.error('Archive rooms error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive schedules by academic year/semester
router.post('/schedules', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, reason } = req.body;
        const adminId = req.user.id;

        if (!academic_year || !semester) {
            return res.status(400).json({ message: 'Academic year and semester are required' });
        }

        const result = await executeQuery(`
            UPDATE CLASSSCHEDULES 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE ACADEMICYEAR = ? AND SEMESTER = ? AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, academic_year, semester]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} schedules`
        });
    } catch (error) {
        console.error('Archive schedules error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive users (all user types)
router.post('/users', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { user_ids, reason } = req.body;
        const adminId = req.user.id;

        if (!user_ids || !Array.isArray(user_ids) || user_ids.length === 0) {
            return res.status(400).json({ message: 'User IDs are required' });
        }

        const result = await executeQuery(`
            UPDATE USERS 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE USERID IN (${user_ids.map(() => '?').join(',')}) 
            AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, ...user_ids]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} user${result.affectedRows !== 1 ? 's' : ''}`
        });
    } catch (error) {
        console.error('Archive users error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive attendance records by academic year/semester
router.post('/attendance', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, reason } = req.body;
        const adminId = req.user.id;

        if (!academic_year || !semester) {
            return res.status(400).json({ message: 'Academic year and semester are required' });
        }

        // Get the date range from multiple sources to ensure we capture all relevant dates
        // 1. From attendance records
        const attendanceDateRange = await getSingleResult(`
            SELECT 
                MIN(DATE(SCANDATETIME)) as min_date,
                MAX(DATE(SCANDATETIME)) as max_date
            FROM ATTENDANCERECORDS 
            WHERE ACADEMICYEAR = ? AND SEMESTER = ? AND ARCHIVED_AT IS NULL
        `, [academic_year, semester]);

        // 2. From sessions (which might have dates beyond attendance records)
        const sessionDateRange = await getSingleResult(`
            SELECT 
                MIN(SESSIONDATE) as min_date,
                MAX(SESSIONDATE) as max_date
            FROM SESSIONS 
            WHERE ACADEMICYEAR = ? AND SEMESTER = ? AND ARCHIVED_AT IS NULL
        `, [academic_year, semester]);

        // Combine date ranges to get the broadest possible range
        let minDate = null;
        let maxDate = null;
        
        if (attendanceDateRange && attendanceDateRange.min_date) {
            minDate = attendanceDateRange.min_date;
            maxDate = attendanceDateRange.max_date;
        }
        
        if (sessionDateRange && sessionDateRange.min_date) {
            if (!minDate || sessionDateRange.min_date < minDate) {
                minDate = sessionDateRange.min_date;
            }
            if (!maxDate || sessionDateRange.max_date > maxDate) {
                maxDate = sessionDateRange.max_date;
            }
        }

        // Archive attendance records
        const result = await executeQuery(`
            UPDATE ATTENDANCERECORDS 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE ACADEMICYEAR = ? AND SEMESTER = ? AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, academic_year, semester]);

        // Archive related ACCESSLOGS (denied access logs and unknown scans) within the date range
        // Use the broader date range that includes both attendance records and sessions
        // Only archive attendance_scan type logs that are shown in the attendance logs page
        let accessLogsArchived = 0;
        if (minDate && maxDate) {
            try {
                // Expand the date range slightly (7 days before and after) to catch edge cases
                // This ensures ACCESSLOGS from dates just outside the range are also archived
                const expandedMinDate = new Date(minDate);
                expandedMinDate.setDate(expandedMinDate.getDate() - 7);
                const expandedMaxDate = new Date(maxDate);
                expandedMaxDate.setDate(expandedMaxDate.getDate() + 7);
                
                const accessLogsResult = await executeQuery(`
                    UPDATE ACCESSLOGS 
                    SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
                    WHERE ACCESSTYPE = 'attendance_scan' 
                    AND ARCHIVED_AT IS NULL
                    AND DATE(TIMESTAMP) >= ? 
                    AND DATE(TIMESTAMP) <= ?
                `, [adminId, reason || null, expandedMinDate.toISOString().split('T')[0], expandedMaxDate.toISOString().split('T')[0]]);
                accessLogsArchived = accessLogsResult.affectedRows || 0;
                console.log(`Archived ${accessLogsArchived} ACCESSLOGS from ${expandedMinDate.toISOString().split('T')[0]} to ${expandedMaxDate.toISOString().split('T')[0]}`);
            } catch (error) {
                // If ARCHIVED_AT column doesn't exist in ACCESSLOGS, log warning but continue
                console.warn('Could not archive access logs (column may not exist, run migration):', error.message);
            }
        } else {
            // If no date range found, try to estimate based on academic year/semester
            try {
                const yearMatch = academic_year.match(/(\d{4})-(\d{4})/);
                if (yearMatch) {
                    const startYear = parseInt(yearMatch[1]);
                    const endYear = parseInt(yearMatch[2]);
                    let semesterStart, semesterEnd;
                    
                    if (semester === 'First Semester' || semester === '1st Semester') {
                        semesterStart = `${startYear}-08-01`; // Typical first semester start
                        semesterEnd = `${startYear}-12-31`;   // Typical first semester end
                    } else if (semester === 'Second Semester' || semester === '2nd Semester') {
                        semesterStart = `${endYear}-01-01`;   // Typical second semester start
                        semesterEnd = `${endYear}-05-31`;     // Typical second semester end
                    } else if (semester === 'Summer') {
                        semesterStart = `${endYear}-06-01`;
                        semesterEnd = `${endYear}-07-31`;
                    }
                    
                    if (semesterStart && semesterEnd) {
                        const accessLogsResult = await executeQuery(`
                            UPDATE ACCESSLOGS 
                            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
                            WHERE ACCESSTYPE = 'attendance_scan' 
                            AND ARCHIVED_AT IS NULL
                            AND DATE(TIMESTAMP) >= ? 
                            AND DATE(TIMESTAMP) <= ?
                        `, [adminId, reason || null, semesterStart, semesterEnd]);
                        accessLogsArchived = accessLogsResult.affectedRows || 0;
                        console.log(`Archived ${accessLogsArchived} ACCESSLOGS using estimated semester range: ${semesterStart} to ${semesterEnd}`);
                    }
                }
            } catch (error) {
                console.warn('Could not archive access logs by semester estimate:', error.message);
            }
        }

        res.json({
            success: true,
            archived: result.affectedRows,
            accessLogsArchived: accessLogsArchived,
            message: `Successfully archived ${result.affectedRows} attendance records${accessLogsArchived > 0 ? ` and ${accessLogsArchived} access logs` : ''}`
        });
    } catch (error) {
        console.error('Archive attendance error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive ACCESSLOGS by date range (for manually archiving specific date ranges)
router.post('/access-logs', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { start_date, end_date, reason } = req.body;
        const adminId = req.user.id;

        if (!start_date || !end_date) {
            return res.status(400).json({ message: 'Start date and end date are required' });
        }

        const result = await executeQuery(`
            UPDATE ACCESSLOGS 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE ACCESSTYPE = 'attendance_scan' 
            AND ARCHIVED_AT IS NULL
            AND DATE(TIMESTAMP) >= ? 
            AND DATE(TIMESTAMP) <= ?
        `, [adminId, reason || null, start_date, end_date]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} access logs from ${start_date} to ${end_date}`
        });
    } catch (error) {
        console.error('Archive access logs error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive backup copies (move files from backups to archived_backups)
router.post('/backups', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { filenames } = req.body;
        if (!Array.isArray(filenames) || filenames.length === 0) {
            return res.status(400).json({ message: 'filenames array is required' });
        }
        const result = await backupService.archiveBackups(filenames);
        res.json({ success: true, ...result });
    } catch (error) {
        console.error('Archive backups error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get archived subjects
router.get('/subjects', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, page = 1, limit = 20 } = req.query;
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 20));
        const offset = (pageNum - 1) * limitNum;

        let whereClause = 'WHERE s.ARCHIVED_AT IS NOT NULL';
        const params = [];

        if (academic_year) {
            whereClause += ' AND s.ACADEMICYEAR = ?';
            params.push(academic_year);
        }
        if (semester) {
            whereClause += ' AND s.SEMESTER = ?';
            params.push(semester);
        }

        const subjects = await executeQuery(`
            SELECT s.*, 
                   CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name
            FROM SUBJECTS s
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            ${whereClause}
            ORDER BY s.ARCHIVED_AT DESC
            LIMIT ? OFFSET ?
        `, [...params, limitNum.toString(), offset.toString()]);

        const total = await getSingleResult(`
            SELECT COUNT(*) as count FROM SUBJECTS s ${whereClause}
        `, params);

        res.json({
            subjects,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total: total.count,
                pages: Math.ceil(total.count / limitNum)
            }
        });
    } catch (error) {
        console.error('Get archived subjects error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get archived rooms
router.get('/rooms', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { page = 1, limit = 20 } = req.query;
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 20));
        const offset = (pageNum - 1) * limitNum;

        const rooms = await executeQuery(`
            SELECT * FROM ROOMS
            WHERE ARCHIVED_AT IS NOT NULL
            ORDER BY ARCHIVED_AT DESC
            LIMIT ? OFFSET ?
        `, [limitNum.toString(), offset.toString()]);

        const total = await getSingleResult(`
            SELECT COUNT(*) as count FROM ROOMS WHERE ARCHIVED_AT IS NOT NULL
        `);

        res.json({
            rooms,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total: total.count,
                pages: Math.ceil(total.count / limitNum)
            }
        });
    } catch (error) {
        console.error('Get archived rooms error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get archived schedules
router.get('/schedules', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, page = 1, limit = 20 } = req.query;
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 20));
        const offset = (pageNum - 1) * limitNum;

        let whereClause = 'WHERE cs.ARCHIVED_AT IS NOT NULL';
        const params = [];

        if (academic_year) {
            whereClause += ' AND cs.ACADEMICYEAR = ?';
            params.push(academic_year);
        }
        if (semester) {
            whereClause += ' AND cs.SEMESTER = ?';
            params.push(semester);
        }

        const schedules = await executeQuery(`
            SELECT cs.*,
                   sub.SUBJECTCODE,
                   sub.SUBJECTNAME,
                   r.ROOMNUMBER,
                   r.ROOMNAME
            FROM CLASSSCHEDULES cs
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            ${whereClause}
            ORDER BY cs.ARCHIVED_AT DESC
            LIMIT ? OFFSET ?
        `, [...params, limitNum.toString(), offset.toString()]);

        const total = await getSingleResult(`
            SELECT COUNT(*) as count FROM CLASSSCHEDULES cs ${whereClause}
        `, params);

        res.json({
            schedules,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total: total.count,
                pages: Math.ceil(total.count / limitNum)
            }
        });
    } catch (error) {
        console.error('Get archived schedules error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get archived users (students)
router.get('/users', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { page = 1, limit = 20 } = req.query;
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 20));
        const offset = (pageNum - 1) * limitNum;

        const users = await executeQuery(`
            SELECT * FROM USERS
            WHERE ARCHIVED_AT IS NOT NULL AND USERTYPE = 'student'
            ORDER BY ARCHIVED_AT DESC
            LIMIT ? OFFSET ?
        `, [limitNum.toString(), offset.toString()]);

        const total = await getSingleResult(`
            SELECT COUNT(*) as count FROM USERS WHERE ARCHIVED_AT IS NOT NULL AND USERTYPE = 'student'
        `);

        res.json({
            users,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total: total.count,
                pages: Math.ceil(total.count / limitNum)
            }
        });
    } catch (error) {
        console.error('Get archived users error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get archived attendance records (includes both ATTENDANCERECORDS and ACCESSLOGS)
router.get('/attendance', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, page = 1, limit = 20 } = req.query;
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 20));
        const offset = (pageNum - 1) * limitNum;

        // Check if ARCHIVED_AT column exists in ACCESSLOGS
        let accessLogsHasArchivedColumn = false;
        try {
            const columnCheck = await getSingleResult(`
                SELECT COUNT(*) as count 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = DATABASE() 
                AND TABLE_NAME = 'ACCESSLOGS' 
                AND COLUMN_NAME = 'ARCHIVED_AT'
            `);
            accessLogsHasArchivedColumn = columnCheck && columnCheck.count > 0;
        } catch (error) {
            console.warn('Could not check for ARCHIVED_AT column in ACCESSLOGS:', error.message);
        }

        // Build WHERE clause for attendance records
        let attendanceWhereClause = 'WHERE ar.ARCHIVED_AT IS NOT NULL';
        const attendanceParams = [];

        if (academic_year) {
            attendanceWhereClause += ' AND ar.ACADEMICYEAR = ?';
            attendanceParams.push(academic_year);
        }
        if (semester) {
            attendanceWhereClause += ' AND ar.SEMESTER = ?';
            attendanceParams.push(semester);
        }

        // Get archived attendance records
        const attendanceRecords = await executeQuery(`
            SELECT 
                ar.ATTENDANCEID,
                ar.USERID,
                ar.SCHEDULEID,
                ar.SCANDATETIME as TIMESTAMP,
                ar.SCANDATETIME,
                ar.STATUS,
                ar.AUTHMETHOD,
                ar.ACTIONTYPE,
                ar.LOCATION,
                ar.ACADEMICYEAR,
                ar.SEMESTER,
                ar.ARCHIVED_AT,
                ar.ARCHIVED_BY,
                ar.ARCHIVE_REASON,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                u.USERTYPE,
                sub.SUBJECTCODE,
                sub.SUBJECTNAME,
                r.ROOMNUMBER,
                r.ROOMNAME,
                DATE(ar.SCANDATETIME) as DATE,
                NULL as REASON,
                'attendance_record' as RECORD_TYPE
            FROM ATTENDANCERECORDS ar
            LEFT JOIN USERS u ON ar.USERID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            ${attendanceWhereClause}
        `, attendanceParams);

        // Get archived access logs (if column exists)
        let accessLogs = [];
        if (accessLogsHasArchivedColumn) {
            try {
                accessLogs = await executeQuery(`
                    SELECT 
                        al.LOGID as ATTENDANCEID,
                        al.USERID,
                        NULL as SCHEDULEID,
                        al.TIMESTAMP,
                        al.TIMESTAMP as SCANDATETIME,
                        CASE 
                            WHEN al.USERID IS NULL THEN 'Unknown'
                            ELSE 'Denied'
                        END as STATUS,
                        al.AUTHMETHOD,
                        CASE 
                            WHEN al.USERID IS NULL THEN 'Unknown Scan'
                            ELSE 'Access Denied'
                        END as ACTIONTYPE,
                        al.LOCATION,
                        NULL as ACADEMICYEAR,
                        NULL as SEMESTER,
                        al.ARCHIVED_AT,
                        al.ARCHIVED_BY,
                        al.ARCHIVE_REASON,
                        COALESCE(u.FIRSTNAME, 'Unknown') as FIRSTNAME,
                        COALESCE(u.LASTNAME, 'User') as LASTNAME,
                        u.STUDENTID,
                        u.USERTYPE,
                        NULL as SUBJECTCODE,
                        NULL as SUBJECTNAME,
                        r.ROOMNUMBER,
                        r.ROOMNAME,
                        DATE(al.TIMESTAMP) as DATE,
                        al.REASON,
                        CASE 
                            WHEN al.USERID IS NULL THEN 'unknown_scan'
                            ELSE 'denied_access'
                        END as RECORD_TYPE
                    FROM ACCESSLOGS al
                    LEFT JOIN USERS u ON al.USERID = u.USERID
                    LEFT JOIN ROOMS r ON al.ROOMID = r.ROOMID
                    WHERE al.ACCESSTYPE = 'attendance_scan' 
                    AND al.ARCHIVED_AT IS NOT NULL
                    ORDER BY al.ARCHIVED_AT DESC
                `);
            } catch (error) {
                console.warn('Could not fetch archived access logs:', error.message);
            }
        }

        // Combine both types of records and sort by archived date
        const allRecords = [...attendanceRecords, ...accessLogs].sort((a, b) => {
            const dateA = new Date(a.ARCHIVED_AT);
            const dateB = new Date(b.ARCHIVED_AT);
            return dateB - dateA; // Most recent first
        });

        // Get total count
        const attendanceCount = attendanceRecords.length;
        const accessLogsCount = accessLogs.length;
        const total = attendanceCount + accessLogsCount;

        // Apply pagination
        const paginatedRecords = allRecords.slice(offset, offset + limitNum);

        res.json({
            records: paginatedRecords,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total: total,
                pages: Math.ceil(total / limitNum)
            }
        });
    } catch (error) {
        console.error('Get archived attendance error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get archived backup copies
router.get('/backups', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const backups = await backupService.listArchivedBackups();
        res.json({ backups });
    } catch (error) {
        console.error('Get archived backups error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Unarchive attendance records by archived date
router.put('/attendance/unarchive', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { archived_at } = req.body;
        
        if (!archived_at) {
            return res.status(400).json({ message: 'Archive date is required' });
        }

        // Unarchive records that were archived at the specified timestamp
        // Use a wider range to capture all records archived within the same operation
        const targetTimestamp = parseInt(archived_at);
        // Use a very wide buffer to ensure we catch all records in the same archive batch
        const startOfSecond = targetTimestamp - 3600; // 1 hour buffer before
        const endOfSecond = targetTimestamp + 3600;   // 1 hour buffer after

        // Use UNIX_TIMESTAMP for comparison to avoid timezone issues
        const result = await executeQuery(`
            UPDATE ATTENDANCERECORDS 
            SET ARCHIVED_AT = NULL
            WHERE UNIX_TIMESTAMP(ARCHIVED_AT) BETWEEN ? AND ?
        `, [startOfSecond.toString(), endOfSecond.toString()]);

        // Also unarchive related ACCESSLOGS that were archived at the same time
        let accessLogsUnarchived = 0;
        try {
            const accessLogsResult = await executeQuery(`
                UPDATE ACCESSLOGS 
                SET ARCHIVED_AT = NULL
                WHERE ACCESSTYPE = 'attendance_scan'
                AND UNIX_TIMESTAMP(ARCHIVED_AT) BETWEEN ? AND ?
            `, [startOfSecond.toString(), endOfSecond.toString()]);
            accessLogsUnarchived = accessLogsResult.affectedRows || 0;
        } catch (error) {
            // If ARCHIVED_AT column doesn't exist in ACCESSLOGS, ignore the error
            // This allows the unarchive to work even if migration hasn't been run yet
            console.warn('Could not unarchive access logs (column may not exist):', error.message);
        }

        console.log(`Unarchive query: timestamp ${archived_at} (range ${startOfSecond}-${endOfSecond}) affected ${result.affectedRows} attendance records and ${accessLogsUnarchived} access logs`);

        res.json({
            success: true,
            unarchived: result.affectedRows,
            accessLogsUnarchived: accessLogsUnarchived,
            message: `Successfully unarchived ${result.affectedRows} attendance records${accessLogsUnarchived > 0 ? ` and ${accessLogsUnarchived} access logs` : ''}`
        });
    } catch (error) {
        console.error('Unarchive attendance error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router;
