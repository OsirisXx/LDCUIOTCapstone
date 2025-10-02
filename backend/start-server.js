// Set environment variables
process.env.JWT_SECRET = 'iot_attendance_super_secret_jwt_key_2024_liceo_de_cagayan_university';
process.env.DB_NAME = 'iot_attendance';
process.env.DB_HOST = 'localhost';
process.env.DB_PORT = '3306';
process.env.DB_USER = 'root';
process.env.DB_PASSWORD = '';
process.env.FRONTEND_URL = 'http://localhost:3000';
process.env.PORT = '5000';
process.env.NODE_ENV = 'development';

// Start the server
require('./server.js'); 