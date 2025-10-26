const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin, requireInstructor } = require('../middleware/auth');
const { v4: uuidv4 } = require('uuid');

const router = express.Router();

// Get list of instructors for dropdown
router.get('/instructors/list', async (req, res) => {
    try {
        const instructors = await executeQuery(
            `SELECT USERID, FIRSTNAME, LASTNAME, FACULTYID, EMAIL 
             FROM USERS 
             WHERE USERTYPE = 'instructor' AND STATUS = 'Active' 
             ORDER BY LASTNAME, FIRSTNAME`,
            []
        );

        // Map FACULTYID to EMPLOYEEID for frontend compatibility
        const mappedInstructors = instructors.map(instructor => ({
            ...instructor,
            EMPLOYEEID: instructor.FACULTYID
        }));

        res.json(mappedInstructors);
    } catch (error) {
        console.error('Get instructors error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get all courses (subjects) with pagination and filters
router.get('/', async (req, res) => {
    try {
        const { page = 1, limit = 50, ACADEMICYEAR, SEMESTER } = req.query;

        // Ensure page and limit are valid integers
        const pageNum = Math.max(1, parseInt(page) || 1);
        const limitNum = Math.max(1, Math.min(100, parseInt(limit) || 50));

        // Build main query using SUBJECTS table
        let query = `
            SELECT 
                s.SUBJECTID,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                s.INSTRUCTORID,
                s.SEMESTER,
                s.YEAR,
                s.ACADEMICYEAR,
                s.DESCRIPTION,
                s.CREATED_AT,
                s.UPDATED_AT,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                u.FACULTYID
            FROM SUBJECTS s
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            WHERE 1=1 AND s.ARCHIVED_AT IS NULL`;

        // Add filtering conditions
        if (ACADEMICYEAR) {
            query += ` AND s.ACADEMICYEAR = '${ACADEMICYEAR}'`;
        }

        if (SEMESTER) {
            query += ` AND s.SEMESTER = '${SEMESTER}'`;
        }

        query += ' ORDER BY s.SUBJECTCODE';

        // Add pagination using template literals
        const offset = (pageNum - 1) * limitNum;
        query += ` LIMIT ${limitNum} OFFSET ${offset}`;

        const subjects = await executeQuery(query, []);

        // Get enrollment counts separately for each subject and map FACULTYID to EMPLOYEEID
        for (let subject of subjects) {
            const enrollmentCount = await executeQuery(
                `SELECT COUNT(*) as count FROM SUBJECTENROLLMENT WHERE SUBJECTID = ? AND STATUS = 'enrolled'`,
                [subject.SUBJECTID]
            );
            subject.enrolled_students = enrollmentCount[0]?.count || 0;
            
            // Map FACULTYID to EMPLOYEEID for frontend compatibility
            subject.EMPLOYEEID = subject.FACULTYID;
        }

        // Get total count
        let countQuery = 'SELECT COUNT(*) as total FROM SUBJECTS s WHERE 1=1 AND s.ARCHIVED_AT IS NULL';

        if (ACADEMICYEAR) {
            countQuery += ` AND s.ACADEMICYEAR = '${ACADEMICYEAR}'`;
        }

        if (SEMESTER) {
            countQuery += ` AND s.SEMESTER = '${SEMESTER}'`;
        }

        const [{ total }] = await executeQuery(countQuery, []);

        res.json({
            subjects,
            pagination: {
                page: pageNum,
                limit: limitNum,
                total,
                pages: Math.ceil(total / limitNum)
            }
        });

    } catch (error) {
        console.error('Get courses error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get single subject by ID
router.get('/:id', authenticateToken, async (req, res) => {
    try {
        const { id } = req.params;

        const subject = await getSingleResult(
            `SELECT 
                s.SUBJECTID,
                s.SUBJECTCODE,
                s.SUBJECTNAME,
                s.INSTRUCTORID,
                s.SEMESTER,
                s.YEAR,
                s.ACADEMICYEAR,
                s.DESCRIPTION,
                s.CREATED_AT,
                s.UPDATED_AT,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                u.FACULTYID
            FROM SUBJECTS s
            LEFT JOIN USERS u ON s.INSTRUCTORID = u.USERID
            WHERE s.SUBJECTID = ?`,
            [id]
        );

        if (!subject) {
            return res.status(404).json({ message: 'Subject not found' });
        }

        // Map FACULTYID to EMPLOYEEID for frontend compatibility
        subject.EMPLOYEEID = subject.FACULTYID;

        // Get enrollment count
        const enrollmentCount = await executeQuery(
            `SELECT COUNT(*) as count FROM SUBJECTENROLLMENT WHERE SUBJECTID = ? AND STATUS = 'enrolled'`,
            [subject.SUBJECTID]
        );
        subject.enrolled_students = enrollmentCount[0]?.count || 0;

        res.json(subject);

    } catch (error) {
        console.error('Get subject error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Create new subject
router.post('/', [
    authenticateToken,
    requireAdmin,
    body('SUBJECTCODE').trim().notEmpty().withMessage('Subject code is required'),
    body('SUBJECTNAME').trim().notEmpty().withMessage('Subject name is required'),
    body('INSTRUCTORID').notEmpty().withMessage('Instructor is required'),
    body('SEMESTER').isIn(['First Semester', 'Second Semester', 'Summer']).withMessage('Invalid semester'),
    body('YEAR').isInt({ min: 2020, max: 2030 }).withMessage('Invalid year'),
    body('ACADEMICYEAR').matches(/^\d{4}-\d{4}$/).withMessage('Academic year must be in format YYYY-YYYY'),
    body('DESCRIPTION').optional().isLength({ max: 1000 }).withMessage('Description too long')
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { SUBJECTCODE, SUBJECTNAME, INSTRUCTORID, SEMESTER, YEAR, ACADEMICYEAR, DESCRIPTION } = req.body;

        // Verify instructor exists
        const instructor = await getSingleResult(
            'SELECT USERID FROM USERS WHERE USERID = ? AND USERTYPE = "instructor" AND STATUS = "Active"',
            [INSTRUCTORID]
        );

        if (!instructor) {
            return res.status(400).json({ message: 'Invalid instructor selected' });
        }

        // Check if subject already exists for this term (unique constraint)
        const existing = await getSingleResult(
            'SELECT SUBJECTID FROM SUBJECTS WHERE SUBJECTCODE = ? AND SEMESTER = ? AND ACADEMICYEAR = ?',
            [SUBJECTCODE, SEMESTER, ACADEMICYEAR]
        );

        if (existing) {
            return res.status(409).json({ message: 'Subject already exists for this semester and academic year' });
        }

        const subjectId = uuidv4();

        await executeQuery(
            `INSERT INTO SUBJECTS (SUBJECTID, SUBJECTCODE, SUBJECTNAME, INSTRUCTORID, SEMESTER, YEAR, ACADEMICYEAR, DESCRIPTION)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            [subjectId, SUBJECTCODE, SUBJECTNAME, INSTRUCTORID, SEMESTER, YEAR, ACADEMICYEAR, DESCRIPTION]
        );

        res.status(201).json({
            message: 'Subject created successfully',
            subjectId: subjectId
        });

    } catch (error) {
        console.error('Create subject error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Update subject
router.put('/:id', [
    authenticateToken,
    requireAdmin,
    body('SUBJECTCODE').trim().notEmpty().withMessage('Subject code is required'),
    body('SUBJECTNAME').trim().notEmpty().withMessage('Subject name is required'),
    body('INSTRUCTORID').notEmpty().withMessage('Instructor is required'),
    body('SEMESTER').isIn(['First Semester', 'Second Semester', 'Summer']).withMessage('Invalid semester'),
    body('YEAR').isInt({ min: 2020, max: 2030 }).withMessage('Invalid year'),
    body('ACADEMICYEAR').matches(/^\d{4}-\d{4}$/).withMessage('Academic year must be in format YYYY-YYYY'),
    body('DESCRIPTION').optional().isLength({ max: 1000 }).withMessage('Description too long')
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { SUBJECTCODE, SUBJECTNAME, INSTRUCTORID, SEMESTER, YEAR, ACADEMICYEAR, DESCRIPTION } = req.body;
        const subjectId = req.params.id;

        // Check if subject exists
        const existingSubject = await getSingleResult('SELECT SUBJECTID FROM SUBJECTS WHERE SUBJECTID = ?', [subjectId]);
        if (!existingSubject) {
            return res.status(404).json({ message: 'Subject not found' });
        }

        // Verify instructor exists
        const instructor = await getSingleResult(
            'SELECT USERID FROM USERS WHERE USERID = ? AND USERTYPE = "instructor" AND STATUS = "Active"',
            [INSTRUCTORID]
        );

        if (!instructor) {
            return res.status(400).json({ message: 'Invalid instructor selected' });
        }

        // Check for duplicate subject code in the same term (excluding current subject)
        const duplicate = await getSingleResult(
            'SELECT SUBJECTID FROM SUBJECTS WHERE SUBJECTCODE = ? AND SEMESTER = ? AND ACADEMICYEAR = ? AND SUBJECTID != ?',
            [SUBJECTCODE, SEMESTER, ACADEMICYEAR, subjectId]
        );

        if (duplicate) {
            return res.status(409).json({ message: 'Another subject with this code already exists for this semester and academic year' });
        }

        await executeQuery(
            `UPDATE SUBJECTS 
             SET SUBJECTCODE = ?, SUBJECTNAME = ?, INSTRUCTORID = ?, SEMESTER = ?, YEAR = ?, ACADEMICYEAR = ?, DESCRIPTION = ?, UPDATED_AT = CURRENT_TIMESTAMP
             WHERE SUBJECTID = ?`,
            [SUBJECTCODE, SUBJECTNAME, INSTRUCTORID, SEMESTER, YEAR, ACADEMICYEAR, DESCRIPTION, subjectId]
        );

        res.json({ message: 'Subject updated successfully' });

    } catch (error) {
        console.error('Update subject error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Bulk delete subjects
router.delete('/bulk-delete', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { ids } = req.body;
        
        if (!ids || !Array.isArray(ids) || ids.length === 0) {
            return res.status(400).json({ message: 'Subject IDs are required' });
        }

        // Validate that all IDs are valid UUIDs
        const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
        const invalidIds = ids.filter(id => !uuidRegex.test(id));
        if (invalidIds.length > 0) {
            return res.status(400).json({ message: 'Invalid subject ID format' });
        }

        let deletedCount = 0;
        const errors = [];

        for (const subjectId of ids) {
            try {
                // Check if subject exists
                const subject = await getSingleResult(
                    'SELECT SUBJECTID, SUBJECTCODE, SUBJECTNAME FROM SUBJECTS WHERE SUBJECTID = ?',
                    [subjectId]
                );

                if (!subject) {
                    errors.push(`Subject with ID ${subjectId} not found`);
                    continue;
                }

                // Delete related data first (cascade delete)
                // Delete subject enrollments
                await executeQuery('DELETE FROM SUBJECTENROLLMENT WHERE SUBJECTID = ?', [subjectId]);
                
                // Delete class schedules
                await executeQuery('DELETE FROM CLASSSCHEDULES WHERE SUBJECTID = ?', [subjectId]);
                
                // Delete the subject
                await executeQuery('DELETE FROM SUBJECTS WHERE SUBJECTID = ?', [subjectId]);
                
                deletedCount++;
                console.log(`✅ Subject ${subject.SUBJECTCODE} - ${subject.SUBJECTNAME} deleted successfully`);
                
            } catch (error) {
                console.error(`❌ Error deleting subject ${subjectId}:`, error);
                errors.push(`Failed to delete subject ${subjectId}: ${error.message}`);
            }
        }

        if (deletedCount === 0) {
            return res.status(400).json({ 
                message: 'No subjects were deleted', 
                details: errors.join('; ') 
            });
        }

        const message = `${deletedCount} subject${deletedCount > 1 ? 's' : ''} deleted successfully`;
        
        res.json({ 
            message,
            deletedCount,
            errors: errors.length > 0 ? errors : undefined
        });

    } catch (error) {
        console.error('Bulk delete subjects error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Delete subject with cascade deletion
router.delete('/:id', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const subjectId = req.params.id;
        console.log(`🚀 DELETE /api/subjects/${subjectId} - NEW CASCADE DELETION PROCESS STARTING`);

        // Check if subject exists
        const subject = await getSingleResult('SELECT SUBJECTID, SUBJECTCODE, SUBJECTNAME FROM SUBJECTS WHERE SUBJECTID = ?', [subjectId]);
        if (!subject) {
            return res.status(404).json({ message: 'Subject not found' });
        }

        // Start transaction-like operations
        let deletedEnrollments = 0;
        let deletedSchedules = 0;

        // Get counts before deletion for response message
        const enrollmentCount = await executeQuery(
            'SELECT COUNT(*) as count FROM SUBJECTENROLLMENT WHERE SUBJECTID = ?',
            [subjectId]
        );
        deletedEnrollments = enrollmentCount[0].count;

        const scheduleCount = await executeQuery(
            'SELECT COUNT(*) as count FROM CLASSSCHEDULES WHERE SUBJECTID = ?',
            [subjectId]
        );
        deletedSchedules = scheduleCount[0].count;

        console.log(`Deleting subject ${subjectId}: ${deletedEnrollments} enrollments, ${deletedSchedules} schedules`);

        // Cascade delete: First delete enrollments
        if (deletedEnrollments > 0) {
            console.log('Deleting enrollments...');
            try {
                await executeQuery('DELETE FROM SUBJECTENROLLMENT WHERE SUBJECTID = ?', [subjectId]);
                console.log('Enrollments deleted successfully');
            } catch (enrollmentError) {
                console.error('Error deleting enrollments:', enrollmentError);
                // Mark enrollments as dropped instead of deleting
                await executeQuery('UPDATE SUBJECTENROLLMENT SET STATUS = "dropped" WHERE SUBJECTID = ?', [subjectId]);
                console.log('Enrollments marked as dropped');
            }
        }

        // Cascade delete: Then delete schedules
        if (deletedSchedules > 0) {
            console.log('Deleting schedules...');
            try {
                await executeQuery('DELETE FROM CLASSSCHEDULES WHERE SUBJECTID = ?', [subjectId]);
                console.log('Schedules deleted successfully');
            } catch (scheduleError) {
                console.error('Error deleting schedules:', scheduleError);
                throw scheduleError; // Re-throw if we can't delete schedules
            }
        }

        // Finally delete the subject itself
        console.log('Deleting subject...');
        try {
            await executeQuery('DELETE FROM SUBJECTS WHERE SUBJECTID = ?', [subjectId]);
            console.log('Subject deleted successfully');
        } catch (subjectError) {
            console.error('Error deleting subject:', subjectError);
            throw subjectError;
        }

        // Prepare response message
        let message = `Subject "${subject.SUBJECTNAME}" deleted successfully`;
        const deletedItems = [];
        
        if (deletedEnrollments > 0) {
            deletedItems.push(`${deletedEnrollments} enrollment${deletedEnrollments !== 1 ? 's' : ''}`);
        }
        if (deletedSchedules > 0) {
            deletedItems.push(`${deletedSchedules} schedule${deletedSchedules !== 1 ? 's' : ''}`);
        }
        
        if (deletedItems.length > 0) {
            message += ` (also removed ${deletedItems.join(' and ')})`;
        }

        res.json({ 
            message,
            deletedEnrollments,
            deletedSchedules
        });

    } catch (error) {
        console.error('Delete subject error:', error);
        console.error('Error details:', error.message);
        console.error('SQL State:', error.sqlState);
        console.error('Error Code:', error.code);
        res.status(500).json({ 
            message: 'Internal server error',
            details: error.message 
        });
    }
});

// Get students for a specific subject
router.get('/:id/students', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;

        // Check if subject exists
        const subject = await getSingleResult('SELECT SUBJECTID, SUBJECTCODE, SUBJECTNAME, ACADEMICYEAR, SEMESTER FROM SUBJECTS WHERE SUBJECTID = ?', [id]);
        if (!subject) {
            return res.status(404).json({ message: 'Subject not found' });
        }

        // Get enrolled students
        const enrolledStudents = await executeQuery(
            `SELECT 
                u.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.EMAIL,
                u.STUDENTID,
                u.YEARLEVEL,
                u.DEPARTMENT,
                se.ENROLLMENTDATE,
                se.STATUS
            FROM SUBJECTENROLLMENT se
            JOIN USERS u ON se.USERID = u.USERID
            WHERE se.SUBJECTID = ? AND se.ACADEMICYEAR = ? AND se.SEMESTER = ? AND se.STATUS = 'enrolled'
            ORDER BY u.LASTNAME, u.FIRSTNAME`,
            [id, subject.ACADEMICYEAR, subject.SEMESTER]
        );

        // Get available students (not enrolled in this subject)
        const availableStudents = await executeQuery(
            `SELECT 
                u.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
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
                WHERE se.SUBJECTID = ? AND se.ACADEMICYEAR = ? AND se.SEMESTER = ? AND se.STATUS = 'enrolled'
            )
            ORDER BY u.LASTNAME, u.FIRSTNAME`,
            [id, subject.ACADEMICYEAR, subject.SEMESTER]
        );

        res.json({
            subject,
            enrolled: enrolledStudents,
            available: availableStudents,
            statistics: {
                totalEnrolled: enrolledStudents.length,
                totalAvailable: availableStudents.length
            }
        });

    } catch (error) {
        console.error('Get subject students error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Enroll students in a subject
router.post('/:id/enroll', [
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

        const { id } = req.params;
        const { studentIds } = req.body;

        // Check if subject exists
        const subject = await getSingleResult('SELECT SUBJECTID, SUBJECTCODE, SUBJECTNAME, ACADEMICYEAR, SEMESTER FROM SUBJECTS WHERE SUBJECTID = ?', [id]);
        if (!subject) {
            return res.status(404).json({ message: 'Subject not found' });
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
                    [studentId, id, subject.ACADEMICYEAR, subject.SEMESTER]
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
                    [enrollmentId, studentId, id, subject.ACADEMICYEAR, subject.SEMESTER]
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

// Remove student from subject
router.delete('/:id/students/:studentId', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id, studentId } = req.params;

        // Check if subject exists
        const subject = await getSingleResult('SELECT SUBJECTID, ACADEMICYEAR, SEMESTER FROM SUBJECTS WHERE SUBJECTID = ?', [id]);
        if (!subject) {
            return res.status(404).json({ message: 'Subject not found' });
        }

        // Check if enrollment exists
        const enrollment = await getSingleResult(
            `SELECT ENROLLMENTID FROM SUBJECTENROLLMENT 
             WHERE USERID = ? AND SUBJECTID = ? AND ACADEMICYEAR = ? AND SEMESTER = ?`,
            [studentId, id, subject.ACADEMICYEAR, subject.SEMESTER]
        );

        if (!enrollment) {
            return res.status(404).json({ message: 'Enrollment not found' });
        }

        // Remove enrollment (mark as dropped)
        await executeQuery(
            `UPDATE SUBJECTENROLLMENT 
             SET STATUS = 'dropped', UPDATED_AT = CURRENT_TIMESTAMP
             WHERE USERID = ? AND SUBJECTID = ? AND ACADEMICYEAR = ? AND SEMESTER = ?`,
            [studentId, id, subject.ACADEMICYEAR, subject.SEMESTER]
        );

        res.json({ message: 'Student removed from subject successfully' });

    } catch (error) {
        console.error('Remove enrollment error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});


module.exports = router;
