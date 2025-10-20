const mysql = require('mysql2/promise');
const bcrypt = require('bcryptjs');
require('dotenv').config();

const dbConfig = {
    host: process.env.DB_HOST || 'localhost',
    port: process.env.DB_PORT || 3306,
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD || '',
    database: process.env.DB_NAME || 'iot_attendance',
    charset: 'utf8mb4'
};

async function fixUserPasswords() {
    let connection;
    
    try {
        connection = await mysql.createConnection(dbConfig);
        console.log('Connected to database');
        
        // Get users without passwords
        const [users] = await connection.execute(
            'SELECT USERID, FIRSTNAME, LASTNAME, USERTYPE, STUDENTID, FACULTYID FROM USERS WHERE PASSWORD_HASH IS NULL OR PASSWORD_HASH = ""'
        );
        
        console.log(`Found ${users.length} users without passwords`);
        
        // Hash the default password
        const defaultPassword = 'password123'; // Default password for all users
        const saltRounds = 10;
        const passwordHash = await bcrypt.hash(defaultPassword, saltRounds);
        
        // Update all users with the default password
        await connection.execute(
            'UPDATE USERS SET PASSWORD_HASH = ? WHERE PASSWORD_HASH IS NULL OR PASSWORD_HASH = ""',
            [passwordHash]
        );
        
        console.log(`Updated ${users.length} users with default password: ${defaultPassword}`);
        console.log('All users can now log in with their email and password: password123');
        
    } catch (error) {
        console.error('Error fixing user passwords:', error);
    } finally {
        if (connection) {
            await connection.end();
        }
    }
}

// Run the script
fixUserPasswords();
