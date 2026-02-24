const { executeQuery, getSingleResult } = require('../config/database');

/**
 * Early Arrival Service
 * Handles auto-confirmation of early arrival scans when instructors don't show up
 */
class EarlyArrivalService {
    
    /**
     * Auto-confirm "Awaiting Confirmation" attendance records when
     * scheduled class ends and instructor never started the session
     */
    static async autoConfirmNoShowInstructor() {
        try {
            console.log('🔍 Checking for auto-confirmation of early arrivals...');
            
            // Get the configured early arrival window (default 15 minutes)
            const earlyArrivalWindow = parseInt(
                (await getSingleResult(
                    'SELECT SETTINGVALUE as setting_value FROM SETTINGS WHERE SETTINGKEY = "student_early_arrival_window"'
                ))?.setting_value || '15'
            );
            
            // Find schedules that ended without an active session
            const schedulesWithoutSession = await executeQuery(`
                SELECT cs.SCHEDULEID, cs.STARTTIME, cs.ENDTIME, sub.SUBJECTCODE, sub.SUBJECTNAME
                FROM CLASSSCHEDULES cs
                JOIN SUBJECTS sub ON cs.SUBJECTID = sub.SUBJECTID
                LEFT JOIN SESSIONS s ON cs.SCHEDULEID = s.SCHEDULEID AND s.SESSIONDATE = CURDATE()
                WHERE cs.ENDTIME < TIME(NOW())
                  AND s.SESSIONID IS NULL
                  AND cs.ACADEMICYEAR = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_academic_year')
                  AND cs.SEMESTER = (SELECT SETTINGVALUE FROM SETTINGS WHERE SETTINGKEY = 'current_semester')
            `);
            
            if (!schedulesWithoutSession || schedulesWithoutSession.length === 0) {
                console.log('✅ No schedules without sessions found');
                return { updated: 0 };
            }
            
            console.log(`📋 Found ${schedulesWithoutSession.length} schedules without sessions`);
            
            // Extract schedule IDs
            const scheduleIds = schedulesWithoutSession.map(s => s.SCHEDULEID);
            
            // Update all "Awaiting Confirmation" records for these schedules
            const result = await executeQuery(`
                UPDATE ATTENDANCERECORDS 
                SET STATUS = 'Present',
                    UPDATED_AT = NOW()
                WHERE SCHEDULEID IN (${scheduleIds.map(() => '?').join(',')})
                  AND STATUS = 'Awaiting Confirmation'
                  AND DATE(SCANDATETIME) = CURDATE()
            `, scheduleIds);
            
            console.log(`✅ Auto-confirmed ${result.affectedRows || 0} early arrival records`);
            
            // Log the auto-confirmations
            if (result.affectedRows > 0) {
                console.log('📊 Auto-confirmed schedules:');
                schedulesWithoutSession.forEach(schedule => {
                    console.log(`  - ${schedule.SUBJECTCODE}: ${schedule.ENDTIME}`);
                });
            }
            
            return {
                updated: result.affectedRows || 0,
                schedules: schedulesWithoutSession.length
            };
            
        } catch (error) {
            console.error('❌ Error in auto-confirmation service:', error);
            return {
                updated: 0,
                error: error.message
            };
        }
    }
    
    /**
     * Get statistics about awaiting confirmation records
     */
    static async getAwaitingConfirmationStats() {
        try {
            const stats = await getSingleResult(`
                SELECT 
                    COUNT(*) as total_awaiting,
                    COUNT(DISTINCT SCHEDULEID) as unique_schedules,
                    COUNT(DISTINCT USERID) as unique_students
                FROM ATTENDANCERECORDS
                WHERE STATUS = 'Awaiting Confirmation'
                  AND DATE(SCANDATETIME) = CURDATE()
            `);
            
            return stats || { total_awaiting: 0, unique_schedules: 0, unique_students: 0 };
        } catch (error) {
            console.error('Error getting awaiting confirmation stats:', error);
            return { total_awaiting: 0, unique_schedules: 0, unique_students: 0 };
        }
    }
}

module.exports = EarlyArrivalService;

