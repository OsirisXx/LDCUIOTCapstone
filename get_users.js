const mysql = require('mysql2/promise');

const dbConfig = {
    host: 'localhost',
    port: 3306,
    user: 'root',
    password: '',
    database: 'iot_attendance',
    charset: 'utf8mb4'
};

async function getUsers() {
    let connection;
    try {
        connection = await mysql.createConnection(dbConfig);
        console.log('Connected to database');
        
        const [users] = await connection.execute(
            'SELECT EMAIL, USERTYPE, FIRSTNAME, LASTNAME, PASSWORD_HASH FROM USERS WHERE EMAIL IS NOT NULL AND EMAIL != "" LIMIT 5'
        );
        
        console.log('Sample users:');
        users.forEach(u => {
            console.log(`Email: ${u.EMAIL}, Type: ${u.USERTYPE}, Name: ${u.FIRSTNAME} ${u.LASTNAME}, Has Password: ${u.PASSWORD_HASH ? 'Yes' : 'No'}`);
        });
        
    } catch (error) {
        console.error('Error:', error);
    } finally {
        if (connection) {
            await connection.end();
        }
    }
}

getUsers();
