const express = require('express');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireInstructor } = require('../middleware/auth');

const router = express.Router();

// Get unified data for rooms, subjects, and schedules with academic year filtering
router.get('/data', authenticateToken, requireInstructor, async (req, res) => {
    try {
        // Check if optional column exists to avoid selecting non-existent fields
        const isLabCol = await getSingleResult(
            `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CLASSSCHEDULES' AND COLUMN_NAME = 'ISLAB'`
        );
        const selectIsLab = (isLabCol && isLabCol.cnt > 0) ? 'COALESCE(cs.ISLAB,0) as ISLAB' : '0 as ISLAB';
        const { academic_year, semester } = req.query;

        // Get all academic years for filter dropdown
        const academicYears = await executeQuery(`
            SELECT DISTINCT ACADEMICYEAR 
            FROM SUBJECTS 
            WHERE ACADEMICYEAR IS NOT NULL 
            ORDER BY ACADEMICYEAR DESC
        `);

        // Get all semesters for filter dropdown
        const semesters = await executeQuery(`
            SELECT DISTINCT SEMESTER 
            FROM SUBJECTS 
            WHERE SEMESTER IS NOT NULL 
            ORDER BY 
                CASE 
                    WHEN SEMESTER = 'First Semester' THEN 1
                    WHEN SEMESTER = 'Second Semester' THEN 2
                    WHEN SEMESTER = 'Summer' THEN 3
                    ELSE 4
                END
        `);

        // Build base query with filters
        let whereClause = 'WHERE 1=1';
        const params = [];

        if (academic_year) {
            whereClause += ' AND s.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            whereClause += ' AND s.SEMESTER = ?';
            params.push(semester);
        }

        // Get rooms with their schedules and subjects
        const roomsData = await executeQuery(`
            SELECT DISTINCT
                r.ROOMID,
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.BUILDING,
                r.CAPACITY,
                r.STATUS,
                COUNT(DISTINCT cs.SCHEDULEID) as schedule_count,
                COUNT(DISTINCT s.SUBJECTID) as subject_count
            FROM ROOMS r
            LEFT JOIN CLASSSCHEDULES cs ON r.ROOMID = cs.ROOMID
            LEFT JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID ${whereClause.replace('WHERE 1=1', 'AND 1=1')}
            GROUP BY r.ROOMID, r.ROOMNUMBER, r.ROOMNAME, r.BUILDING, r.CAPACITY, r.STATUS
            ORDER BY r.BUILDING, r.ROOMNUMBER
        `, params);

        // Get subjects with their schedules and rooms
        const subjectsData = await executeQuery(`
            SELECT 
                s.SUBJECTID,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                s.INSTRUCTORID,
                s.SEMESTER,
                s.YEAR,
                s.ACADEMICYEAR,
                s.DESCRIPTION,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                COUNT(DISTINCT cs.SCHEDULEID) as schedule_count,
                COUNT(DISTINCT cs.ROOMID) as room_count,
                COUNT(DISTINCT CASE WHEN se.ACADEMICYEAR = s.ACADEMICYEAR AND se.SEMESTER = s.SEMESTER THEN se.USERID END) as enrolled_students
            FROM SUBJECTS s
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON s.SUBJECTID = cs.SUBJECTID
            LEFT JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID AND se.STATUS = 'enrolled'
            ${whereClause}
            GROUP BY s.SUBJECTID, s.SUBJECTCODE, s.SUBJECTNAME, s.INSTRUCTORID, s.SEMESTER, s.YEAR, s.ACADEMICYEAR, s.DESCRIPTION, instructor_name
            ORDER BY s.SUBJECTCODE
        `, params);

        // Get schedules with all related information
        const schedulesData = await executeQuery(`
            SELECT 
                cs.SCHEDULEID,
                cs.SUBJECTID,
                cs.ROOMID,
                cs.DAYOFWEEK,
                cs.STARTTIME,
                cs.ENDTIME,
                cs.ACADEMICYEAR,
                cs.SEMESTER,
                ${selectIsLab} ,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.BUILDING,
                COUNT(DISTINCT se.USERID) as enrolled_students
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            LEFT JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID AND se.STATUS = 'enrolled'
            ${whereClause.replace('WHERE 1=1', 'WHERE 1=1 AND s.SUBJECTID IS NOT NULL')}
            GROUP BY cs.SCHEDULEID, cs.SUBJECTID, cs.ROOMID, cs.DAYOFWEEK, cs.STARTTIME, cs.ENDTIME, cs.ACADEMICYEAR, cs.SEMESTER, s.SUBJECTCODE, s.SUBJECTNAME, instructor_name, r.ROOMNUMBER, r.ROOMNAME, r.BUILDING
            ORDER BY cs.DAYOFWEEK, cs.STARTTIME
        `, params);

        // Get detailed room schedules for room view
        const roomSchedules = {};
        for (const room of roomsData) {
            const schedules = await executeQuery(`
                SELECT 
                    cs.SCHEDULEID,
                    cs.DAYOFWEEK,
                    cs.STARTTIME,
                    cs.ENDTIME,
                    ${selectIsLab} ,
                    s.SUBJECTCODE,
                    s.SUBJECTNAME,
                    CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                    COUNT(DISTINCT se.USERID) as enrolled_students
                FROM CLASSSCHEDULES cs
                JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
                LEFT JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID AND se.STATUS = 'enrolled'
                WHERE cs.ROOMID = ? ${academic_year ? 'AND s.ACADEMICYEAR = ?' : ''} ${semester ? 'AND s.SEMESTER = ?' : ''}
                GROUP BY cs.SCHEDULEID, cs.DAYOFWEEK, cs.STARTTIME, cs.ENDTIME, s.SUBJECTCODE, s.SUBJECTNAME, instructor_name
                ORDER BY cs.DAYOFWEEK, cs.STARTTIME
            `, [room.ROOMID, ...(academic_year ? [academic_year] : []), ...(semester ? [semester] : [])]);
            
            roomSchedules[room.ROOMID] = schedules;
        }

        // Get detailed subject schedules for subject view
        const subjectSchedules = {};
        for (const subject of subjectsData) {
            const schedules = await executeQuery(`
                SELECT 
                    cs.SCHEDULEID,
                    cs.DAYOFWEEK,
                    cs.STARTTIME,
                    cs.ENDTIME,
                    r.ROOMNUMBER,
                    r.ROOMNAME,
                    r.BUILDING
                FROM CLASSSCHEDULES cs
                JOIN ROOMS r ON cs.ROOMID = r.ROOMID
                WHERE cs.SUBJECTID = ?
                ORDER BY cs.DAYOFWEEK, cs.STARTTIME
            `, [subject.SUBJECTID]);
            
            subjectSchedules[subject.SUBJECTID] = schedules;
        }

        res.json({
            filters: {
                academic_years: academicYears.map(ay => ay.ACADEMICYEAR),
                semesters: semesters.map(s => s.SEMESTER),
                current_academic_year: academic_year,
                current_semester: semester
            },
            rooms: {
                data: roomsData,
                schedules: roomSchedules
            },
            subjects: {
                data: subjectsData,
                schedules: subjectSchedules
            },
            schedules: {
                data: schedulesData
            },
            statistics: {
                total_rooms: roomsData.length,
                total_subjects: subjectsData.length,
                total_schedules: schedulesData.length,
                total_enrolled_students: subjectsData.reduce((sum, subject) => sum + (subject.enrolled_students || 0), 0)
            }
        });

    } catch (error) {
        console.error('Get unified data error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get detailed data for a specific room
router.get('/room/:id', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;
        const { academic_year, semester } = req.query;

        // Get room details
        const room = await getSingleResult(`
            SELECT r.*
            FROM ROOMS r
            WHERE r.ROOMID = ?
        `, [id]);

        if (!room) {
            return res.status(404).json({ message: 'Room not found' });
        }

        // Get subjects and schedules in this room
        let whereClause = 'WHERE cs.ROOMID = ?';
        const params = [id];

        if (academic_year) {
            whereClause += ' AND s.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            whereClause += ' AND s.SEMESTER = ?';
            params.push(semester);
        }

        const subjects = await executeQuery(`
            SELECT DISTINCT
                s.SUBJECTID,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                s.ACADEMICYEAR,
                s.SEMESTER,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                COUNT(DISTINCT cs.SCHEDULEID) as schedule_count,
                COUNT(DISTINCT se.USERID) as enrolled_students
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            LEFT JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID AND se.STATUS = 'enrolled'
            ${whereClause}
            GROUP BY s.SUBJECTID, s.SUBJECTCODE, s.SUBJECTNAME, s.ACADEMICYEAR, s.SEMESTER, instructor_name
            ORDER BY s.SUBJECTCODE
        `, params);

        const schedules = await executeQuery(`
            SELECT 
                cs.SCHEDULEID,
                cs.DAYOFWEEK,
                cs.STARTTIME,
                cs.ENDTIME,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            ${whereClause}
            ORDER BY cs.DAYOFWEEK, cs.STARTTIME
        `, params);

        res.json({
            room,
            subjects,
            schedules
        });

    } catch (error) {
        console.error('Get room details error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get detailed data for a specific subject
router.get('/subject/:id', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;

        // Check if optional column exists to avoid selecting non-existent fields
        const isLabCol = await getSingleResult(
            `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CLASSSCHEDULES' AND COLUMN_NAME = 'ISLAB'`
        );
        const selectIsLab = (isLabCol && isLabCol.cnt > 0) ? 'COALESCE(cs.ISLAB,0) as ISLAB' : '0 as ISLAB';

        // Get subject details
        const subject = await getSingleResult(`
            SELECT 
                s.*,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                COUNT(DISTINCT se.USERID) as enrolled_students
            FROM SUBJECTS s
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            LEFT JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID AND se.STATUS = 'enrolled'
            WHERE s.SUBJECTID = ?
            GROUP BY s.SUBJECTID
        `, [id]);

        if (!subject) {
            return res.status(404).json({ message: 'Subject not found' });
        }

        // Get rooms and schedules for this subject
        const rooms = await executeQuery(`
            SELECT DISTINCT
                r.ROOMID,
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.BUILDING,
                COUNT(DISTINCT cs.SCHEDULEID) as schedule_count
            FROM CLASSSCHEDULES cs
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE cs.SUBJECTID = ?
            GROUP BY r.ROOMID, r.ROOMNUMBER, r.ROOMNAME, r.BUILDING
            ORDER BY r.BUILDING, r.ROOMNUMBER
        `, [id]);

        const schedules = await executeQuery(`
            SELECT 
                cs.SCHEDULEID,
                cs.DAYOFWEEK,
                cs.STARTTIME,
                cs.ENDTIME,
                ${selectIsLab},
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.BUILDING
            FROM CLASSSCHEDULES cs
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE cs.SUBJECTID = ?
            ORDER BY cs.DAYOFWEEK, cs.STARTTIME
        `, [id]);

        // Get enrolled students
        const students = await executeQuery(`
            SELECT 
                u.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                u.EMAIL,
                u.YEARLEVEL,
                u.DEPARTMENT,
                se.ENROLLMENTDATE
            FROM SUBJECTENROLLMENT se
            JOIN USERS u ON se.USERID = u.USERID
            WHERE se.SUBJECTID = ? AND se.STATUS = 'enrolled'
            ORDER BY u.LASTNAME, u.FIRSTNAME
        `, [id]);

        res.json({
            subject,
            rooms,
            schedules,
            students
        });

    } catch (error) {
        console.error('Get subject details error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get detailed data for a specific schedule
router.get('/schedule/:id', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;

        // Get schedule details with all related information
        const schedule = await getSingleResult(`
            SELECT 
                cs.*,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                s.DESCRIPTION as subject_description,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                r.ROOMNUMBER,
                r.ROOMNAME,
                r.BUILDING,
                r.CAPACITY
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE cs.SCHEDULEID = ?
        `, [id]);

        if (!schedule) {
            return res.status(404).json({ message: 'Schedule not found' });
        }

        // Get enrolled students for this schedule
        const students = await executeQuery(`
            SELECT 
                u.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                u.EMAIL,
                u.YEARLEVEL,
                u.DEPARTMENT
            FROM SUBJECTENROLLMENT se
            JOIN USERS u ON se.USERID = u.USERID
            WHERE se.SUBJECTID = ? AND se.STATUS = 'enrolled'
            ORDER BY u.LASTNAME, u.FIRSTNAME
        `, [schedule.SUBJECTID]);

        res.json({
            schedule,
            students
        });

    } catch (error) {
        console.error('Get schedule details error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router;

// Utilities
async function ensureColumnExists(table, column, definition) {
    try {
        const exists = await getSingleResult(
            `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ? AND COLUMN_NAME = ?`,
            [table, column]
        );
        if (!exists || exists.cnt === 0) {
            await executeQuery(`ALTER TABLE ${table} ADD COLUMN IF NOT EXISTS ${column} ${definition}`);
        }
    } catch (e) {
        // Non-fatal; endpoint will still work if column already exists
        if (!/Duplicate column/i.test(e.message)) {
            console.warn(`ensureColumnExists warning for ${table}.${column}:`, e.message);
        }
    }
}




