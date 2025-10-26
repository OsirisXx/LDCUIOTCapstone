const express = require('express');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin, requireInstructor } = require('../middleware/auth');

const router = express.Router();

// Get all schedules
router.get('/', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { instructor_id, room_id, subject_id, day_of_week, academic_year, semester, page = 1, limit = 50 } = req.query;

        let query = `
            SELECT 
                cs.SCHEDULEID,
                cs.SUBJECTID,
                cs.ROOMID,
                cs.DAYOFWEEK,
                cs.STARTTIME,
                cs.ENDTIME,
                cs.ACADEMICYEAR,
                cs.SEMESTER,
                CONCAT(u.FIRSTNAME, ' ', u.LASTNAME) as instructor_name,
                s.SUBJECTCODE as subject_code,
                s.SUBJECTNAME as subject_name,
                r.ROOMNUMBER as room_number,
                r.ROOMNAME as room_name
            FROM CLASSSCHEDULES cs
            JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            JOIN USERS u ON s.INSTRUCTORID = u.USERID
            JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE 1=1 AND cs.ARCHIVED_AT IS NULL
        `;
        const params = [];

        if (instructor_id) {
            query += ' AND s.INSTRUCTORID = ?';
            params.push(instructor_id);
        }

        if (room_id) {
            query += ' AND cs.ROOMID = ?';
            params.push(room_id);
        }

        if (subject_id) {
            query += ' AND cs.SUBJECTID = ?';
            params.push(subject_id);
        }

        if (day_of_week) {
            query += ' AND cs.DAYOFWEEK = ?';
            params.push(day_of_week);
        }

        if (academic_year) {
            query += ' AND cs.ACADEMICYEAR = ?';
            params.push(academic_year);
        }

        if (semester) {
            query += ' AND cs.SEMESTER = ?';
            params.push(semester);
        }

        query += ' ORDER BY cs.DAYOFWEEK, cs.STARTTIME';

        const offset = (page - 1) * limit;
        query += ` LIMIT ${parseInt(limit)} OFFSET ${offset}`;

        const schedules = await executeQuery(query, params);

        res.json({ schedules });

    } catch (error) {
        console.error('Get schedules error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Create schedule
router.post('/', [
    authenticateToken,
    requireAdmin,
    body('subject_id').isUUID(),
    body('room_id').isUUID(),
    body('day_of_week').isIn(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']),
    body('start_time').matches(/^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$/),
    body('end_time').matches(/^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$/),
    body('academic_year').notEmpty(),
    body('semester').notEmpty()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { subject_id, room_id, day_of_week, start_time, end_time, academic_year, semester } = req.body;
        
        console.log(`Creating schedule: ${day_of_week} ${start_time}-${end_time} for subject ${subject_id} in room ${room_id}`);
        console.log('Request body:', req.body);

        // Check for conflicts - only check for the same day, room, and overlapping times
        const conflicts = await executeQuery(
            `SELECT COUNT(*) as count FROM CLASSSCHEDULES 
             WHERE ROOMID = ? 
             AND DAYOFWEEK = ? 
             AND ACADEMICYEAR = ? 
             AND SEMESTER = ?
             AND (
                 (STARTTIME < ? AND ENDTIME > ?) OR
                 (STARTTIME < ? AND ENDTIME > ?) OR
                 (STARTTIME >= ? AND ENDTIME <= ?)
             )`,
            [room_id, day_of_week, academic_year, semester, 
             end_time, start_time, start_time, end_time, start_time, end_time]
        );

        if (conflicts[0].count > 0) {
            console.log(`Schedule conflict detected for ${day_of_week} ${start_time}-${end_time} in room ${room_id}`);
            return res.status(409).json({ message: 'Schedule conflict detected' });
        }

        const { v4: uuidv4 } = require('uuid');
        const scheduleId = uuidv4();

        const result = await executeQuery(
            `INSERT INTO CLASSSCHEDULES (SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME, ACADEMICYEAR, SEMESTER)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
            [scheduleId, subject_id, room_id, day_of_week, start_time, end_time, academic_year, semester]
        );

        console.log(`Schedule created successfully: ${scheduleId} for ${day_of_week} ${start_time}-${end_time}`);
        res.status(201).json({ message: 'Schedule created successfully', id: scheduleId });

    } catch (error) {
        console.error('Create schedule error:', error);
        console.error('Error details:', {
            message: error.message,
            code: error.code,
            errno: error.errno,
            sqlState: error.sqlState,
            sqlMessage: error.sqlMessage
        });
        res.status(500).json({ message: 'Internal server error', details: error.message });
    }
});

// Update schedule
router.put('/:id', [
    authenticateToken,
    requireAdmin,
    body('subject_id').optional().isUUID(),
    body('room_id').optional().isUUID(),
    body('day_of_week').optional().isIn(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']),
    body('start_time').optional().matches(/^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$/),
    body('end_time').optional().matches(/^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$/),
    body('academic_year').optional().notEmpty(),
    body('semester').optional().notEmpty()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { id } = req.params;
        const updateFields = req.body;
        
        console.log(`Updating schedule ${id} with fields:`, updateFields);

        // Remove undefined fields
        Object.keys(updateFields).forEach(key => {
            if (updateFields[key] === undefined) {
                delete updateFields[key];
            }
        });

        if (Object.keys(updateFields).length === 0) {
            return res.status(400).json({ message: 'No fields to update' });
        }

        // Check if schedule exists
        const existingSchedule = await getSingleResult('SELECT SCHEDULEID FROM CLASSSCHEDULES WHERE SCHEDULEID = ?', [id]);
        if (!existingSchedule) {
            return res.status(404).json({ message: 'Schedule not found' });
        }

        // Build update query - map frontend field names to database column names
        const fieldMap = {
            subject_id: 'SUBJECTID',
            room_id: 'ROOMID',
            day_of_week: 'DAYOFWEEK',
            start_time: 'STARTTIME',
            end_time: 'ENDTIME',
            academic_year: 'ACADEMICYEAR',
            semester: 'SEMESTER'
        };

        const setClause = Object.keys(updateFields).map(key => `${fieldMap[key]} = ?`).join(', ');
        const values = Object.values(updateFields);
        values.push(id);

        await executeQuery(
            `UPDATE CLASSSCHEDULES SET ${setClause}, UPDATED_AT = CURRENT_TIMESTAMP WHERE SCHEDULEID = ?`,
            values
        );

        console.log(`Schedule ${id} updated successfully with fields: ${setClause}`);
        res.json({ message: 'Schedule updated successfully' });

    } catch (error) {
        console.error('Update schedule error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Bulk delete schedules
router.delete('/bulk-delete', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { ids } = req.body;
        
        if (!ids || !Array.isArray(ids) || ids.length === 0) {
            return res.status(400).json({ message: 'Schedule IDs are required' });
        }

        // Validate that all IDs are valid UUIDs
        const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
        const invalidIds = ids.filter(id => !uuidRegex.test(id));
        if (invalidIds.length > 0) {
            return res.status(400).json({ message: 'Invalid schedule ID format' });
        }

        let deletedCount = 0;
        const errors = [];

        for (const scheduleId of ids) {
            try {
                // Check if schedule exists
                const schedule = await getSingleResult(
                    'SELECT SCHEDULEID, DAYOFWEEK, STARTTIME, ENDTIME FROM CLASSSCHEDULES WHERE SCHEDULEID = ?',
                    [scheduleId]
                );

                if (!schedule) {
                    errors.push(`Schedule with ID ${scheduleId} not found`);
                    continue;
                }

                // Delete the schedule (schedules don't have related data that needs cascade deletion)
                await executeQuery('DELETE FROM CLASSSCHEDULES WHERE SCHEDULEID = ?', [scheduleId]);
                
                deletedCount++;
                console.log(`✅ Schedule (${schedule.DAYOFWEEK} ${schedule.STARTTIME}-${schedule.ENDTIME}) deleted successfully`);
                
            } catch (error) {
                console.error(`❌ Error deleting schedule ${scheduleId}:`, error);
                errors.push(`Failed to delete schedule ${scheduleId}: ${error.message}`);
            }
        }

        if (deletedCount === 0) {
            return res.status(400).json({ 
                message: 'No schedules were deleted', 
                details: errors.join('; ') 
            });
        }

        const message = `${deletedCount} schedule${deletedCount > 1 ? 's' : ''} deleted successfully`;
        
        res.json({ 
            message,
            deletedCount,
            errors: errors.length > 0 ? errors : undefined
        });

    } catch (error) {
        console.error('Bulk delete schedules error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Delete schedule
router.delete('/:id', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { id } = req.params;

        // Check if schedule exists
        const existingSchedule = await getSingleResult('SELECT SCHEDULEID FROM CLASSSCHEDULES WHERE SCHEDULEID = ?', [id]);
        if (!existingSchedule) {
            return res.status(404).json({ message: 'Schedule not found' });
        }

        await executeQuery('DELETE FROM CLASSSCHEDULES WHERE SCHEDULEID = ?', [id]);

        res.json({ message: 'Schedule deleted successfully' });

    } catch (error) {
        console.error('Delete schedule error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});


module.exports = router; 
