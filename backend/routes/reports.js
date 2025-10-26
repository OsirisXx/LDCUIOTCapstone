const express = require('express');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken } = require('../middleware/auth');

const router = express.Router();

// Utility function to parse time strings
function parseTime(timeStr) {
    const [hours, minutes, seconds] = timeStr.split(':').map(Number);
    const date = new Date();
    date.setHours(hours, minutes, seconds || 0, 0);
    return date.getTime();
}

// Get attendance reports with early arrival support
router.get('/attendance', authenticateToken, async (req, res) => {
    try {
        console.log('📊 Reports endpoint called with query:', req.query);
        const { startDate, endDate, scheduleId, subjectId } = req.query;
        
        const query = `
            SELECT 
                ar.ATTENDANCEID,
                ar.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID,
                ar.SCHEDULEID,
                ar.SCANDATETIME,
                ar.TIMEIN as TIME_SCANNED,
                ar.STATUS,
                ar.SCANTYPE,
                ar.AUTHMETHOD,
                ar.LOCATION,
                s.SUBJECTNAME,
                s.SUBJECTCODE,
                r.ROOMNUMBER,
                cs.DAYOFWEEK,
                cs.STARTTIME,
                cs.ENDTIME,
                cs.ACADEMICYEAR,
                cs.SEMESTER,
                ar.DATE as ATTENDANCE_DATE
            FROM ATTENDANCERECORDS ar
            JOIN USERS u ON ar.USERID = u.USERID
            LEFT JOIN CLASSSCHEDULES cs ON ar.SCHEDULEID = cs.SCHEDULEID
            LEFT JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
            LEFT JOIN ROOMS r ON cs.ROOMID = r.ROOMID
            WHERE ar.ARCHIVED_AT IS NULL
              ${startDate ? 'AND ar.DATE >= ?' : ''}
              ${endDate ? 'AND ar.DATE <= ?' : ''}
              ${scheduleId ? 'AND ar.SCHEDULEID = ?' : ''}
              ${subjectId ? 'AND cs.SUBJECTID = ?' : ''}
            ORDER BY ar.DATE DESC, ar.TIMEIN ASC
        `;
        
        const params = [];
        if (startDate) params.push(startDate);
        if (endDate) params.push(endDate);
        if (scheduleId) params.push(scheduleId);
        if (subjectId) params.push(subjectId);
        
        const attendance = await executeQuery(query, params);
        console.log('📊 Found', attendance.length, 'attendance records');
        console.log('📊 Sample record:', attendance[0]);
        
        // Enrich with "Early" status display
        const enriched = attendance.map(record => {
            // Calculate if student was early
            if (record.STARTTIME && record.TIME_SCANNED) {
                const startTime = parseTime(record.STARTTIME);
                const scanTime = parseTime(record.TIME_SCANNED);
                const minutesDiff = (scanTime - startTime) / (1000 * 60);
                
                if (minutesDiff < 0 && record.SCANTYPE === 'time_in') {
                    record.DISPLAY_STATUS = 'Early';
                } else {
                    record.DISPLAY_STATUS = record.STATUS;
                }
            } else {
                record.DISPLAY_STATUS = record.STATUS;
            }
            
            return record;
        });
        
        res.json({ success: true, data: enriched });
    } catch (error) {
        console.error('Reports error:', error);
        res.status(500).json({ success: false, error: error.message });
    }
});

// Get attendance reports with absent detection
router.get('/attendance-with-absents', authenticateToken, async (req, res) => {
    try {
        const { date, scheduleId } = req.query;
        
        // Get all enrolled students for the schedule
        const enrolledQuery = `
            SELECT DISTINCT
                u.USERID,
                u.FIRSTNAME,
                u.LASTNAME,
                u.STUDENTID
            FROM SUBJECTENROLLMENT se
            JOIN USERS u ON se.USERID = u.USERID
            JOIN CLASSSCHEDULES cs ON se.SUBJECTID = cs.SUBJECTID
            WHERE cs.SCHEDULEID = ?
              AND se.STATUS = 'enrolled'
              AND u.STATUS = 'Active'
        `;
        
        const enrolled = await executeQuery(enrolledQuery, [scheduleId]);
        
        // Get actual attendance records
        const attendanceQuery = `
            SELECT 
                USERID,
                TIMEIN as TIME_SCANNED,
                STATUS,
                SCANTYPE
            FROM ATTENDANCERECORDS
            WHERE SCHEDULEID = ?
              AND DATE = ?
              AND ARCHIVED_AT IS NULL
        `;
        
        const attendance = await executeQuery(attendanceQuery, [scheduleId, date]);
        
        // Build attendance map
        const attendanceMap = {};
        attendance.forEach(record => {
            attendanceMap[record.USERID] = record;
        });
        
        // Merge enrolled with attendance
        const report = enrolled.map(student => {
            const record = attendanceMap[student.USERID];
            
            if (record) {
                return {
                    ...student,
                    TIME_SCANNED: record.TIME_SCANNED,
                    DISPLAY_STATUS: record.SCANTYPE === 'time_in' ? 'Early' : record.STATUS,
                    SCANTYPE: record.SCANTYPE
                };
            } else {
                return {
                    ...student,
                    TIME_SCANNED: null,
                    DISPLAY_STATUS: 'Absent',
                    SCANTYPE: null
                };
            }
        });
        
        res.json({ success: true, data: report });
    } catch (error) {
        console.error('Reports error:', error);
        res.status(500).json({ success: false, error: error.message });
    }
});

module.exports = router;
