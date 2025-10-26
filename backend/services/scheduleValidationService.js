const { executeQuery, getSingleResult } = require('../config/database');
const moment = require('moment');

/**
 * Schedule Validation Service
 * Provides comprehensive schedule validation for attendance systems
 */
class ScheduleValidationService {
    
    /**
     * Validate if a user has a scheduled class at the current time
     * @param {string} userId - User ID
     * @param {string} roomId - Room ID
     * @param {string} userType - User type (student, instructor, admin, custodian, dean)
     * @returns {Object} Validation result with isValid, reason, and schedule details
     */
    static async validateCurrentSchedule(userId, roomId, userType) {
        try {
            const now = moment();
            const currentDay = now.format('dddd');
            const currentTime = now.format('HH:mm:ss');
            
            // Custodian: Always allow access, no schedule validation needed
            if (userType === 'custodian') {
                return {
                    isValid: true,
                    reason: 'Custodian access - no schedule required',
                    schedule: null
                };
            }
            
            // Dean: Check if they have scheduled classes, but allow access regardless
            if (userType === 'dean') {
                // First check if they have a scheduled class
                const deanSchedule = await this.getDeanSchedule(userId, roomId);
                if (deanSchedule) {
                    return {
                        isValid: true,
                        reason: 'Dean has scheduled class',
                        schedule: deanSchedule
                    };
                } else {
                    // Dean still gets access even without schedule
                    return {
                        isValid: true,
                        reason: 'Dean access - no schedule required',
                        schedule: null
                    };
                }
            }
            
            let query, params;
            
            if (userType === 'instructor') {
                // For instructors: Check if they have a scheduled class at this time
                query = `
                    SELECT cs.SCHEDULEID as schedule_id, 
                           s.SUBJECTID as subject_id,
                           s.SUBJECTCODE as subject_code,
                           s.SUBJECTNAME as subject_name,
                           cs.STARTTIME as start_time,
                           cs.ENDTIME as end_time,
                           r.ROOMNUMBER as room_number,
                           r.ROOMNAME as room_name
                    FROM CLASSSCHEDULES cs
                    JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                    JOIN ROOMS r ON cs.ROOMID = r.ROOMID
                    WHERE s.INSTRUCTORID = ?
                      AND cs.ROOMID = ?
                      AND cs.DAYOFWEEK = ?
                      AND cs.STARTTIME <= ?
                      AND cs.ENDTIME >= ?
                      AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                      AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`;
                params = [userId, roomId, currentDay, currentTime, currentTime];
            } else if (userType === 'student') {
                // For students: Check if they are enrolled in a subject that has a scheduled class at this time
                query = `
                    SELECT cs.SCHEDULEID as schedule_id,
                           s.SUBJECTID as subject_id,
                           s.SUBJECTCODE as subject_code,
                           s.SUBJECTNAME as subject_name,
                           cs.STARTTIME as start_time,
                           cs.ENDTIME as end_time,
                           r.ROOMNUMBER as room_number,
                           r.ROOMNAME as room_name
                    FROM CLASSSCHEDULES cs
                    JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                    JOIN SUBJECTENROLLMENT se ON s.SUBJECTID = se.SUBJECTID
                    JOIN ROOMS r ON cs.ROOMID = r.ROOMID
                    WHERE se.USERID = ?
                      AND se.STATUS = 'enrolled'
                      AND cs.ROOMID = ?
                      AND cs.DAYOFWEEK = ?
                      AND cs.STARTTIME <= ?
                      AND cs.ENDTIME >= ?
                      AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                      AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`;
                params = [userId, roomId, currentDay, currentTime, currentTime];
            } else {
                return {
                    isValid: false,
                    reason: 'Invalid user type for schedule validation',
                    schedule: null
                };
            }
            
            const schedule = await getSingleResult(query, params);
            
            if (schedule) {
                return {
                    isValid: true,
                    reason: `${userType} has scheduled class`,
                    schedule: schedule
                };
            } else {
                return {
                    isValid: false,
                    reason: `No scheduled class for ${userType} at this time`,
                    schedule: null
                };
            }
            
        } catch (error) {
            console.error('Schedule validation error:', error);
            return {
                isValid: false,
                reason: `Validation error: ${error.message}`,
                schedule: null
            };
        }
    }
    
    /**
     * Check if there's an active session for a room
     * @param {string} roomId - Room ID
     * @returns {Object} Session information or null
     */
    static async getActiveSession(roomId) {
        try {
            const session = await getSingleResult(`
                SELECT s.SESSIONID as session_id,
                       s.SCHEDULEID as schedule_id,
                       s.INSTRUCTORID as instructor_id,
                       s.STATUS as status,
                       s.STARTTIME as start_time,
                       s.ENDTIME as end_time,
                       cs.SUBJECTID as subject_id,
                       sub.SUBJECTCODE as subject_code,
                       sub.SUBJECTNAME as subject_name,
                       u.FIRSTNAME as instructor_first_name,
                       u.LASTNAME as instructor_last_name
                FROM SESSIONS s
                JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
                JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
                JOIN USERS u ON s.INSTRUCTORID = u.USERID
                WHERE s.ROOMID = ? 
                  AND s.SESSIONDATE = CURDATE() 
                  AND s.STATUS = 'active'`,
                [roomId]
            );
            
            return session;
        } catch (error) {
            console.error('Error getting active session:', error);
            return null;
        }
    }
    
    /**
     * Get dean's scheduled class (if any) - deans can be instructors of classes
     * @param {string} deanId - Dean ID
     * @param {string} roomId - Room ID
     * @returns {Object} Schedule information or null
     */
    static async getDeanSchedule(deanId, roomId) {
        try {
            const now = moment();
            const currentDay = now.format('dddd');
            const currentTime = now.format('HH:mm:ss');
            
            const schedule = await getSingleResult(`
                SELECT cs.SCHEDULEID as schedule_id, 
                       s.SUBJECTID as subject_id,
                       s.SUBJECTCODE as subject_code,
                       s.SUBJECTNAME as subject_name,
                       cs.STARTTIME as start_time,
                       cs.ENDTIME as end_time,
                       r.ROOMNUMBER as room_number,
                       r.ROOMNAME as room_name
                FROM CLASSSCHEDULES cs
                JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                JOIN ROOMS r ON cs.ROOMID = r.ROOMID
                WHERE s.INSTRUCTORID = ?
                  AND cs.ROOMID = ?
                  AND cs.DAYOFWEEK = ?
                  AND cs.STARTTIME <= ?
                  AND cs.ENDTIME >= ?
                  AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                  AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
                [deanId, roomId, currentDay, currentTime, currentTime]
            );
            
            return schedule;
        } catch (error) {
            console.error('Error getting dean schedule:', error);
            return null;
        }
    }
    
    /**
     * Check if a student is enrolled in a subject
     * @param {string} studentId - Student ID
     * @param {string} subjectId - Subject ID
     * @returns {Object} Enrollment information or null
     */
    static async checkStudentEnrollment(studentId, subjectId) {
        try {
            const enrollment = await getSingleResult(`
                SELECT se.ENROLLMENTID as enrollment_id,
                       se.USERID as student_id,
                       se.SUBJECTID as subject_id,
                       se.STATUS as status,
                       se.ACADEMICYEAR as academic_year,
                       se.SEMESTER as semester
                FROM SUBJECTENROLLMENT se
                WHERE se.USERID = ?
                  AND se.SUBJECTID = ?
                  AND se.STATUS = 'enrolled'
                  AND se.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                  AND se.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
                [studentId, subjectId]
            );
            
            return enrollment;
        } catch (error) {
            console.error('Error checking student enrollment:', error);
            return null;
        }
    }
    
    /**
     * Get schedule for early arrival (students can scan 15 minutes before scheduled start)
     * @param {string} roomId - Room ID
     * @returns {Object} Schedule information or null
     */
    static async getScheduleForEarlyArrival(roomId) {
        try {
            const now = moment();
            const currentDay = now.format('dddd');
            const currentTime = now.format('HH:mm:ss');
            
            const schedule = await getSingleResult(`
                SELECT cs.SCHEDULEID as schedule_id,
                       cs.SUBJECTID as subject_id,
                       s.SUBJECTCODE as subject_code,
                       s.SUBJECTNAME as subject_name,
                       cs.STARTTIME as start_time,
                       cs.ENDTIME as end_time,
                       s.INSTRUCTORID as instructor_id,
                       r.ROOMNUMBER as room_number,
                       r.ROOMNAME as room_name
                FROM CLASSSCHEDULES cs
                JOIN SUBJECTS s ON cs.SUBJECTID = s.SUBJECTID
                JOIN ROOMS r ON cs.ROOMID = r.ROOMID
                WHERE cs.ROOMID = ?
                  AND cs.DAYOFWEEK = ?
                  AND cs.STARTTIME > ?
                  AND cs.STARTTIME <= TIME_ADD(?, INTERVAL 15 MINUTE)
                  AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                  AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')`,
                [roomId, currentDay, currentTime, currentTime]
            );
            
            return schedule;
        } catch (error) {
            console.error('Error getting schedule for early arrival:', error);
            return null;
        }
    }
    
    /**
     * Validate attendance recording with comprehensive checks
     * @param {string} userId - User ID
     * @param {string} roomId - Room ID
     * @param {string} userType - User type
     * @param {string} location - Location (inside/outside)
     * @returns {Object} Comprehensive validation result
     */
    static async validateAttendanceRecording(userId, roomId, userType, location = 'inside') {
        try {
            const result = {
                isValid: false,
                reason: '',
                schedule: null,
                session: null,
                enrollment: null,
                canRecord: false
            };
            
            // Custodian: Always allow access, no schedule validation needed
            if (userType === 'custodian') {
                result.isValid = true;
                result.reason = 'Custodian access - no schedule required';
                result.canRecord = false; // Custodians don't record attendance
                return result;
            }
            
            // Dean: Check if they have scheduled classes, but allow access regardless
            if (userType === 'dean') {
                const scheduleValidation = await this.validateCurrentSchedule(userId, roomId, userType);
                result.isValid = true;
                result.reason = scheduleValidation.reason;
                result.schedule = scheduleValidation.schedule;
                result.canRecord = scheduleValidation.schedule ? true : false; // Only record if they have a schedule
                return result;
            }
            
            // Step 1: Check if user has a scheduled class
            const scheduleValidation = await this.validateCurrentSchedule(userId, roomId, userType);
            if (!scheduleValidation.isValid) {
                result.reason = scheduleValidation.reason;
                return result;
            }
            
            result.schedule = scheduleValidation.schedule;
            
            // Step 2: For students, check enrollment
            if (userType === 'student' && scheduleValidation.schedule) {
                const enrollment = await this.checkStudentEnrollment(userId, scheduleValidation.schedule.subject_id);
                if (!enrollment) {
                    result.reason = `Student is not enrolled in ${scheduleValidation.schedule.subject_code} - ${scheduleValidation.schedule.subject_name}`;
                    return result;
                }
                result.enrollment = enrollment;
            }
            
            // Step 3: Check for active session (for inside scans)
            if (location === 'inside') {
                const session = await this.getActiveSession(roomId);
                if (!session) {
                    result.reason = 'No active class session in this room. Instructor must start the session first.';
                    return result;
                }
                result.session = session;
            }
            
            // Step 4: For early arrival (outside scans for students)
            if (location === 'outside' && userType === 'student') {
                const earlyArrivalSchedule = await this.getScheduleForEarlyArrival(roomId);
                if (!earlyArrivalSchedule) {
                    result.reason = 'No class starting within the next 15 minutes';
                    return result;
                }
                
                // Check if student is enrolled in the early arrival subject
                const enrollment = await this.checkStudentEnrollment(userId, earlyArrivalSchedule.subject_id);
                if (!enrollment) {
                    result.reason = `Student is not enrolled in ${earlyArrivalSchedule.subject_code} - ${earlyArrivalSchedule.subject_name}`;
                    return result;
                }
                
                result.schedule = earlyArrivalSchedule;
                result.enrollment = enrollment;
            }
            
            result.isValid = true;
            result.canRecord = true;
            result.reason = 'All validations passed';
            
            return result;
            
        } catch (error) {
            console.error('Comprehensive attendance validation error:', error);
            return {
                isValid: false,
                reason: `Validation error: ${error.message}`,
                schedule: null,
                session: null,
                enrollment: null,
                canRecord: false
            };
        }
    }
}

module.exports = ScheduleValidationService;

