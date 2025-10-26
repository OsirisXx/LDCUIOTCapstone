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

async function resetAdminPassword() {
    let connection;
    try {
        connection = await mysql.createConnection(dbConfig);
        console.log('Connected to database');
        
        // Hash the password
        const password = 'password123';
        const saltRounds = 10;
        const passwordHash = await bcrypt.hash(password, saltRounds);
        
        // Update admin user password
        await connection.execute(
            'UPDATE USERS SET PASSWORD_HASH = ? WHERE EMAIL = ?',
            [passwordHash, 'admin@liceo.edu.ph']
        );
        
        console.log('Admin password updated successfully');
        
        // Verify the password
        const [users] = await connection.execute(
            'SELECT PASSWORD_HASH FROM USERS WHERE EMAIL = ?',
            ['admin@liceo.edu.ph']
        );
        
        if (users.length > 0) {
            const isValid = await bcrypt.compare(password, users[0].PASSWORD_HASH);
            console.log('Password verification result:', isValid);
        }
        
    } catch (error) {
        console.error('Error:', error);
    } finally {
        if (connection) {
            await connection.end();
        }
    }
}

resetAdminPassword();

