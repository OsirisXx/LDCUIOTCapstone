const express = require('express');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin } = require('../middleware/auth');

const router = express.Router();

// Get archive dashboard statistics
router.get('/dashboard', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const [subjects, rooms, schedules, users, attendance, sessions] = await Promise.all([
            executeQuery(`SELECT COUNT(*) as count FROM SUBJECTS WHERE ARCHIVED_AT IS NOT NULL`),
            executeQuery(`SELECT COUNT(*) as count FROM ROOMS WHERE ARCHIVED_AT IS NOT NULL`),
            executeQuery(`SELECT COUNT(*) as count FROM CLASSSCHEDULES WHERE ARCHIVED_AT IS NOT NULL`),
            executeQuery(`SELECT COUNT(*) as count FROM USERS WHERE ARCHIVED_AT IS NOT NULL AND USERTYPE = 'student'`),
            executeQuery(`SELECT COUNT(*) as count FROM ATTENDANCERECORDS WHERE ARCHIVED_AT IS NOT NULL`),
            executeQuery(`SELECT COUNT(*) as count FROM SESSIONS WHERE ARCHIVED_AT IS NOT NULL`)
        ]);

        const stats = [
            { category: 'subjects', count: subjects[0].count },
            { category: 'rooms', count: rooms[0].count },
            { category: 'schedules', count: schedules[0].count },
            { category: 'users', count: users[0].count },
            { category: 'attendance', count: attendance[0].count },
            { category: 'sessions', count: sessions[0].count }
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

// Archive students (users with USERTYPE = 'student')
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
            AND USERTYPE = 'student' 
            AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, ...user_ids]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} students`
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

        const result = await executeQuery(`
            UPDATE ATTENDANCERECORDS 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE ACADEMICYEAR = ? AND SEMESTER = ? AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, academic_year, semester]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} attendance records`
        });
    } catch (error) {
        console.error('Archive attendance error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Archive sessions by academic year/semester
router.post('/sessions', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, reason } = req.body;
        const adminId = req.user.id;

        if (!academic_year || !semester) {
            return res.status(400).json({ message: 'Academic year and semester are required' });
        }

        const result = await executeQuery(`
            UPDATE SESSIONS 
            SET ARCHIVED_AT = NOW(), ARCHIVED_BY = ?, ARCHIVE_REASON = ?
            WHERE ACADEMICYEAR = ? AND SEMESTER = ? AND ARCHIVED_AT IS NULL
        `, [adminId, reason || null, academic_year, semester]);

        res.json({
            success: true,
            archived: result.affectedRows,
            message: `Successfully archived ${result.affectedRows} sessions`
        });
    } catch (error) {
        console.error('Archive sessions error:', error);
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

// Get archived attendance records
router.get('/attendance', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { academic_year, semester, page = 1, limit = 20 } = req.query;
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 20));
        const offset = (pageNum - 1) * limitNum;

        let whereClause = 'WHERE ar.ARCHIVED_AT IS NOT NULL';
        const params = [];

        if (academic_year) {
            whereClause += ' AND ar.ACADEMICYEAR = ?';
            params.push(academic_year);
        }
        if (semester) {
            whereClause += ' AND ar.SEMESTER = ?';
            params.push(semester);
        }

        const records = await executeQuery(`
            SELECT ar.*,
                   u.FIRSTNAME,
                   u.LASTNAME,
                   u.STUDENTID,
                   u.USERTYPE,
                   sub.SUBJECTCODE,
                   sub.SUBJECTNAME,
                   r.ROOMNUMBER,
                   r.ROOMNAME
            FROM ATTENDANCERECORDS ar
            LEFT JOIN USERS u ON ar.USERID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            ${whereClause}
            ORDER BY ar.ARCHIVED_AT DESC
            LIMIT ? OFFSET ?
        `, [...params, limitNum.toString(), offset.toString()]);

        const total = await getSingleResult(`
            SELECT COUNT(*) as count FROM ATTENDANCERECORDS ar ${whereClause}
        `, params);

        res.json({
            records,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total: total.count,
                pages: Math.ceil(total.count / limitNum)
            }
        });
    } catch (error) {
        console.error('Get archived attendance error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get archived sessions
router.get('/sessions', authenticateToken, requireAdmin, async (req, res) => {
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

        const sessions = await executeQuery(`
            SELECT s.*,
                   sub.SUBJECTCODE,
                   sub.SUBJECTNAME,
                   r.ROOMNUMBER,
                   r.ROOMNAME
            FROM SESSIONS s
            LEFT JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
            LEFT JOIN ROOMS r ON s.ROOMID = r.ROOMID
            ${whereClause}
            ORDER BY s.ARCHIVED_AT DESC
            LIMIT ? OFFSET ?
        `, [...params, limitNum.toString(), offset.toString()]);

        const total = await getSingleResult(`
            SELECT COUNT(*) as count FROM SESSIONS s ${whereClause}
        `, params);

        res.json({
            sessions,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total: total.count,
                pages: Math.ceil(total.count / limitNum)
            }
        });
    } catch (error) {
        console.error('Get archived sessions error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router;
