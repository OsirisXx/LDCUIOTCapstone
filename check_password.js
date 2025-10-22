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

async function checkPassword() {
    let connection;
    try {
        connection = await mysql.createConnection(dbConfig);
        console.log('Connected to database');
        
        const [users] = await connection.execute(
            'SELECT EMAIL, PASSWORD_HASH FROM USERS WHERE EMAIL = ?',
            ['admin@liceo.edu.ph']
        );
        
        if (users.length > 0) {
            const user = users[0];
            console.log('User found:', user.EMAIL);
            console.log('Password hash exists:', !!user.PASSWORD_HASH);
            
            // Test password verification
            const isValid = await bcrypt.compare('password123', user.PASSWORD_HASH);
            console.log('Password verification result:', isValid);
        } else {
            console.log('User not found');
        }
        
    } catch (error) {
        console.error('Error:', error);
    } finally {
        if (connection) {
            await connection.end();
        }
    }
}

checkPassword();
