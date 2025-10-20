const mysql = require('mysql2/promise');
require('dotenv').config();

const dbConfig = {
    host: process.env.DB_HOST || 'localhost',
    port: process.env.DB_PORT || 3306,
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD || '',
    database: process.env.DB_NAME || 'iot_attendance',
    charset: 'utf8mb4'
};

async function fixUserEmails() {
    let connection;
    
    try {
        connection = await mysql.createConnection(dbConfig);
        console.log('Connected to database');
        
        // Get users without emails
        const [users] = await connection.execute(
            'SELECT USERID, FIRSTNAME, LASTNAME, STUDENTID, FACULTYID, USERTYPE FROM USERS WHERE EMAIL IS NULL OR EMAIL = ""'
        );
        
        console.log(`Found ${users.length} users without emails`);
        
        for (const user of users) {
            let email;
            
            if (user.USERTYPE === 'student' && user.STUDENTID) {
                // Generate email for students: student_id@liceo.edu.ph
                email = `${user.STUDENTID}@liceo.edu.ph`;
            } else if (user.USERTYPE === 'instructor' && user.FACULTYID) {
                // Generate email for instructors: faculty_id@liceo.edu.ph
                email = `${user.FACULTYID}@liceo.edu.ph`;
            } else {
                // Generate email based on name for others
                const firstName = user.FIRSTNAME.toLowerCase().replace(/[^a-z]/g, '');
                const lastName = user.LASTNAME.toLowerCase().replace(/[^a-z]/g, '');
                email = `${firstName}.${lastName}@liceo.edu.ph`;
            }
            
            // Update the user with the generated email
            await connection.execute(
                'UPDATE USERS SET EMAIL = ? WHERE USERID = ?',
                [email, user.USERID]
            );
            
            console.log(`Updated ${user.FIRSTNAME} ${user.LASTNAME} with email: ${email}`);
        }
        
        console.log('All users have been updated with emails');
        
    } catch (error) {
        console.error('Error fixing user emails:', error);
    } finally {
        if (connection) {
            await connection.end();
        }
    }
}

// Run the script
fixUserEmails();
