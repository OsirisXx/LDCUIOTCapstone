const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin, requireInstructor } = require('../middleware/auth');

const router = express.Router();

// Get all enrollments
router.get('/', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { USERID, SUBJECTID, STATUS, page = 1, limit = 50 } = req.query;

        let query = `
            SELECT 
                ce.ENROLLMENTID,
                ce.ENROLLMENTDATE,
                ce.STATUS,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as student_name,
                u.EMAIL,
                u.YEARLEVEL,
                s.SUBJECTNAME,
                s.SEMESTER,
                s.YEAR
            FROM SUBJECTENROLLMENT ce
            JOIN USERS u ON ce.USERID = u.USERID
            JOIN SUBJECTS s ON ce.SUBJECTID = s.SUBJECTID
            WHERE 1=1
        `;
        const params = [];

        if (USERID) {
            query += ' AND ce.USERID = ?';
            params.push(USERID);
        }

        if (SUBJECTID) {
            query += ' AND ce.SUBJECTID = ?';
            params.push(SUBJECTID);
        }

        if (STATUS) {
            query += ' AND ce.STATUS = ?';
            params.push(STATUS);
        }

        query += ' ORDER BY ce.ENROLLMENTDATE DESC';

        const offset = (page - 1) * limit;
        query += ' LIMIT ? OFFSET ?';
        params.push(parseInt(limit), offset);

        const enrollments = await executeQuery(query, params);

        // Get total count
        let countQuery = 'SELECT COUNT(*) as total FROM SUBJECTENROLLMENT ce WHERE 1=1';
        const countParams = params.slice(0, -2);

        if (USERID) countQuery += ' AND ce.USERID = ?';
        if (SUBJECTID) countQuery += ' AND ce.SUBJECTID = ?';
        if (STATUS) countQuery += ' AND ce.STATUS = ?';

        const [{ total }] = await executeQuery(countQuery, countParams);

        res.json({
            enrollments,
            pagination: {
                page: parseInt(page),
                limit: parseInt(limit),
                total,
                pages: Math.ceil(total / limit)
            }
        });

    } catch (error) {
        console.error('Get enrollments error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get enrollments by schedule ID (for manage enrollment functionality)
router.get('/schedule/:scheduleId', authenticateToken, async (req, res) => {
    try {
        const { scheduleId } = req.params;

        // Get schedule details first
        const scheduleQuery = `
            SELECT 
                cs.SCHEDULEID,
                cs.SUBJECTID,
                cs.ACADEMICYEAR,
                cs.SEMESTER,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                r.ROOMNUMBER,
                r.ROOMNAME
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            JOIN USERS u ON s.INSTRUCTORID = u.USERID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE cs.SCHEDULEID = ?
        `;
        
        const [scheduleInfo] = await executeQuery(scheduleQuery, [scheduleId]);
        if (!scheduleInfo) {
            return res.status(404).json({ message: 'Schedule not found' });
        }

        // Get enrolled students for this subject in the same academic year and semester
        const enrolledQuery = `
            SELECT 
                ce.ENROLLMENTID,
                ce.USERID,
                ce.ENROLLMENTDATE,
                ce.STATUS,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as student_name,
                u.EMAIL,
                u.STUDENTID,
                u.YEARLEVEL,
                u.DEPARTMENT
            FROM SUBJECTENROLLMENT ce
            JOIN USERS u ON ce.USERID = u.USERID
            WHERE ce.SUBJECTID = ? 
            AND ce.ACADEMICYEAR = ? 
            AND ce.SEMESTER = ?
            AND ce.STATUS = 'enrolled'
            ORDER BY u.LASTNAME, u.FIRSTNAME
        `;

        const enrolledStudents = await executeQuery(enrolledQuery, [
            scheduleInfo.SUBJECTID,
            scheduleInfo.ACADEMICYEAR,
            scheduleInfo.SEMESTER
        ]);

        // Get available students (not enrolled in this subject)
        const availableQuery = `
            SELECT 
                u.USERID,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as student_name,
                u.EMAIL,
                u.STUDENTID,
                u.YEARLEVEL,
                u.DEPARTMENT
            FROM USERS u
            WHERE u.USERTYPE = 'student'
            AND u.STATUS = 'Active'
            AND u.USERID NOT IN (
                SELECT se.USERID 
                FROM SUBJECTENROLLMENT se 
                WHERE se.SUBJECTID = ? 
                AND se.ACADEMICYEAR = ? 
                AND se.SEMESTER = ?
                AND se.STATUS = 'enrolled'
            )
            ORDER BY u.LASTNAME, u.FIRSTNAME
        `;

        const availableStudents = await executeQuery(availableQuery, [
            scheduleInfo.SUBJECTID,
            scheduleInfo.ACADEMICYEAR,
            scheduleInfo.SEMESTER
        ]);

        res.json({
            schedule: scheduleInfo,
            enrolled: enrolledStudents,
            available: availableStudents,
            statistics: {
                totalEnrolled: enrolledStudents.length,
                totalAvailable: availableStudents.length
            }
        });

    } catch (error) {
        console.error('Get schedule enrollments error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Enroll multiple students in a schedule
router.post('/schedule/:scheduleId/enroll', [
    authenticateToken,
    requireInstructor,
    body('studentIds').isArray().withMessage('Student IDs must be an array'),
    body('studentIds.*').isUUID().withMessage('Each student ID must be a valid UUID')
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { scheduleId } = req.params;
        const { studentIds } = req.body;

        // Get schedule info
        const scheduleQuery = `
            SELECT cs.SUBJECTID, cs.ACADEMICYEAR, cs.SEMESTER
            FROM CLASSSCHEDULES cs
            WHERE cs.SCHEDULEID = ?
        `;
        
        const [scheduleInfo] = await executeQuery(scheduleQuery, [scheduleId]);
        if (!scheduleInfo) {
            return res.status(404).json({ message: 'Schedule not found' });
        }

        const { v4: uuidv4 } = require('uuid');
        const enrollments = [];
        const enrollmentErrors = [];

        for (const studentId of studentIds) {
            try {
                // Check if student exists and is active
                const student = await getSingleResult(
                    'SELECT USERID, USERTYPE, STATUS FROM USERS WHERE USERID = ?',
                    [studentId]
                );

                if (!student) {
                    enrollmentErrors.push(`Student with ID ${studentId} not found`);
                    continue;
                }

                if (student.USERTYPE !== 'student') {
                    enrollmentErrors.push(`User ${studentId} is not a student`);
                    continue;
                }

                if (student.STATUS !== 'Active') {
                    enrollmentErrors.push(`Student ${studentId} is not active`);
                    continue;
                }

                // Check if already enrolled
                const existing = await getSingleResult(
                    `SELECT ENROLLMENTID FROM SUBJECTENROLLMENT 
                     WHERE USERID = ? AND SUBJECTID = ? AND ACADEMICYEAR = ? AND SEMESTER = ?`,
                    [studentId, scheduleInfo.SUBJECTID, scheduleInfo.ACADEMICYEAR, scheduleInfo.SEMESTER]
                );

                if (existing) {
                    enrollmentErrors.push(`Student ${studentId} is already enrolled`);
                    continue;
                }

                // Enroll the student
                const enrollmentId = uuidv4();
                await executeQuery(
                    `INSERT INTO SUBJECTENROLLMENT (ENROLLMENTID, USERID, SUBJECTID, ACADEMICYEAR, SEMESTER, STATUS)
                     VALUES (?, ?, ?, ?, ?, 'enrolled')`,
                    [enrollmentId, studentId, scheduleInfo.SUBJECTID, scheduleInfo.ACADEMICYEAR, scheduleInfo.SEMESTER]
                );

                enrollments.push({ enrollmentId, studentId });

            } catch (error) {
                console.error(`Error enrolling student ${studentId}:`, error);
                enrollmentErrors.push(`Failed to enroll student ${studentId}: ${error.message}`);
            }
        }

        res.json({
            message: `Successfully enrolled ${enrollments.length} student(s)`,
            enrollments,
            errors: enrollmentErrors.length > 0 ? enrollmentErrors : undefined
        });

    } catch (error) {
        console.error('Bulk enrollment error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Remove enrollment
router.delete('/schedule/:scheduleId/student/:studentId', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { scheduleId, studentId } = req.params;

        // Get schedule info
        const scheduleQuery = `
            SELECT cs.SUBJECTID, cs.ACADEMICYEAR, cs.SEMESTER
            FROM CLASSSCHEDULES cs
            WHERE cs.SCHEDULEID = ?
        `;
        
        const [scheduleInfo] = await executeQuery(scheduleQuery, [scheduleId]);
        if (!scheduleInfo) {
            return res.status(404).json({ message: 'Schedule not found' });
        }

        // Check if enrollment exists
        const enrollment = await getSingleResult(
            `SELECT ENROLLMENTID FROM SUBJECTENROLLMENT 
             WHERE USERID = ? AND SUBJECTID = ? AND ACADEMICYEAR = ? AND SEMESTER = ?`,
            [studentId, scheduleInfo.SUBJECTID, scheduleInfo.ACADEMICYEAR, scheduleInfo.SEMESTER]
        );

        if (!enrollment) {
            return res.status(404).json({ message: 'Enrollment not found' });
        }

        // Remove enrollment (or mark as dropped)
        await executeQuery(
            `UPDATE SUBJECTENROLLMENT 
             SET STATUS = 'dropped'
             WHERE USERID = ? AND SUBJECTID = ? AND ACADEMICYEAR = ? AND SEMESTER = ?`,
            [studentId, scheduleInfo.SUBJECTID, scheduleInfo.ACADEMICYEAR, scheduleInfo.SEMESTER]
        );

        res.json({ message: 'Student removed from course successfully' });

    } catch (error) {
        console.error('Remove enrollment error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Create enrollment
router.post('/', [
    authenticateToken,
    requireAdmin,
    body('USERID').isUUID(),
    body('SUBJECTID').isUUID()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { USERID, SUBJECTID } = req.body;

        // Get subject details to get ACADEMICYEAR and SEMESTER
        const subject = await getSingleResult('SELECT ACADEMICYEAR, SEMESTER FROM SUBJECTS WHERE SUBJECTID = ?', [SUBJECTID]);
        if (!subject) {
            return res.status(404).json({ message: 'Subject not found' });
        }

        // Check if enrollment already exists
        const existing = await getSingleResult(
            'SELECT ENROLLMENTID FROM SUBJECTENROLLMENT WHERE USERID = ? AND SUBJECTID = ? AND ACADEMICYEAR = ? AND SEMESTER = ?',
            [USERID, SUBJECTID, subject.ACADEMICYEAR, subject.SEMESTER]
        );

        if (existing) {
            return res.status(409).json({ message: 'Student already enrolled in this subject' });
        }

        // Check if user is a student
        const user = await getSingleResult('SELECT USERTYPE FROM USERS WHERE USERID = ?', [USERID]);
        if (!user || user.USERTYPE !== 'student') {
            return res.status(400).json({ message: 'Only students can be enrolled in subjects' });
        }

        const { v4: uuidv4 } = require('uuid');
        const enrollmentId = uuidv4();
        
        const result = await executeQuery(
            'INSERT INTO SUBJECTENROLLMENT (ENROLLMENTID, USERID, SUBJECTID, ACADEMICYEAR, SEMESTER, STATUS) VALUES (?, ?, ?, ?, ?, ?)',
            [enrollmentId, USERID, SUBJECTID, subject.ACADEMICYEAR, subject.SEMESTER, 'enrolled']
        );

        res.status(201).json({
            message: 'Student enrolled successfully',
            enrollmentId: enrollmentId
        });

    } catch (error) {
        console.error('Create enrollment error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Check if a specific student is enrolled in a subject
router.get('/schedule/:subjectId/check/:studentId', authenticateToken, async (req, res) => {
    try {
        const { subjectId, studentId } = req.params;

        // Check if student is enrolled in the subject
        const enrollment = await getSingleResult(
            `SELECT ENROLLMENTID FROM SUBJECTENROLLMENT 
             WHERE USERID = ? AND SUBJECTID = ? AND STATUS = 'enrolled'
             AND ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
             AND SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
            [studentId, subjectId]
        );

        res.json({
            enrolled: !!enrollment,
            message: enrollment ? 'Student is enrolled' : 'Student is not enrolled'
        });

    } catch (error) {
        console.error('Check enrollment error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router; 
