# IoT Attendance System - Liceo de Cagayan University

A full-stack IoT-enabled classroom attendance and access control system built with React.js, Node.js, Express, and MySQL.

## 🏗️ System Architecture

- **Frontend**: React.js with TailwindCSS
- **Backend**: Node.js with Express.js
- **Database**: MySQL with UUID primary keys
- **Authentication**: JWT-based authentication
- **IoT Simulation**: Test buttons for RFID/Fingerprint scanning

## 📋 Features

### Core Functionality
- **Instructor Access Control**: RFID scan outside room to start/end sessions and control door locks
- **Student Attendance**: Fingerprint scan inside room to record attendance during active sessions
- **Real-time Session Management**: Track active class sessions with automatic late detection
- **Comprehensive Logging**: All access attempts and attendance records are logged

### User Management
- Role-based access control (Admin, Instructor, Student)
- User promotion system (Student → Instructor → Admin)
- Authentication method management (RFID tags, fingerprint templates)

### Academic Features
- Course and enrollment management
- Class schedule management with conflict detection
- Academic year and semester scoping
- Attendance analytics and reporting

### System Administration
- Room and device management
- System health monitoring
- Manual door control override
- Comprehensive audit trails

## 🗄️ Database Structure

The system uses 9 main tables following your exact specifications:

1. **USERS** (11 attributes) - User information and roles
2. **AUTHENTICATIONMETHODS** (7 attributes) - RFID tags and fingerprint data
3. **ROOMS** (8 attributes) - Classroom and laboratory information
4. **COURSES** (8 attributes) - Course information with instructor assignment
5. **CLASSSCHEDULES** (8 attributes) - Class timetables
6. **COURSEENROLLMENT** (6 attributes) - Student course enrollments
7. **ATTENDANCERECORDS** (10 attributes) - Student attendance logs
8. **ACCESSLOGS** (8 attributes) - All access attempt logs
9. **DEVICES** (9 attributes) - IoT device management

## 🚀 Getting Started

### Prerequisites
- Node.js (v16 or higher)
- MySQL (v8.0 or higher)
- npm or yarn

### Installation

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd attendanceiot/IOTCapstone
   ```

2. **Install dependencies**
   ```bash
   # Install root dependencies
   npm install
   
   # Install backend dependencies
   cd backend
   npm install
   
   # Install frontend dependencies
   cd ../frontend
   npm install
   ```

3. **Database Setup**
   ```bash
   # Create .env file in backend directory
   cp backend/env.example backend/.env
   
   # Edit .env with your database credentials
   # Then run database setup
   cd backend
   node scripts/setupDatabase.js
   ```

4. **Start the application**
   ```bash
   # From root directory, start both frontend and backend
   npm run dev
   ```

   Or run separately:
   ```bash
   # Backend (Port 5000)
   cd backend
   npm run dev
   
   # Frontend (Port 3000)
   cd frontend
   npm start
   ```

## 🔧 Configuration

### Environment Variables

Create a `.env` file in the backend directory:

```env
# Database Configuration
DB_HOST=localhost
DB_PORT=3306
DB_USER=root
DB_PASSWORD=your_password
DB_NAME=iot_attendance

# JWT Configuration
JWT_SECRET=your_jwt_secret_key
JWT_EXPIRE=7d

# Server Configuration
PORT=5000
NODE_ENV=development
FRONTEND_URL=http://localhost:3000
```

## 🧪 Testing the System

1. **Access the Test Page**: Navigate to `/test` in the application
2. **Simulate IoT Scans**: 
   - Select a room and user
   - Choose authentication method (RFID/Fingerprint)
   - Select scan location (inside/outside)
   - Click "Simulate Scan"

### Test Scenarios

**Instructor Workflow:**
- Outside scan: Start/end sessions, control door locks
- Inside scan: End session only

**Student Workflow:**
- Inside scan: Record attendance during active sessions
- Late detection based on class start time

## 📱 User Interface

The system includes 9 main views:

1. **Dashboard** - System overview and statistics
2. **Attendance Logs** - Student attendance records
3. **Access Logs** - All system access attempts
4. **Session Management** - Active class sessions
5. **User Management** - Students, instructors, admins
6. **Room Management** - Classrooms and devices
7. **Course Management** - Academic courses
8. **Schedule Management** - Class timetables
9. **Test Simulation** - IoT device testing

## 🔐 Security Features

- JWT-based authentication
- Role-based access control
- Rate limiting on API endpoints
- Input validation and sanitization
- SQL injection prevention
- Comprehensive audit logging

## 📊 System Monitoring

- Real-time system health indicators
- Database connection monitoring
- Device status tracking
- Session analytics
- Attendance reporting

## 🛠️ Development

### Project Structure
```
IOTCapstone/
├── backend/
│   ├── config/         # Database configuration
│   ├── middleware/     # Authentication middleware
│   ├── routes/         # API routes
│   ├── scripts/        # Database setup scripts
│   └── server.js       # Main server file
├── frontend/
│   ├── public/         # Static assets
│   ├── src/
│   │   ├── components/ # React components
│   │   ├── contexts/   # React contexts
│   │   ├── pages/      # Page components
│   │   └── App.js      # Main app component
│   └── package.json
├── database/
│   └── schema.sql      # Database schema
└── package.json        # Root package file
```

### API Endpoints

- `POST /api/auth/login` - User authentication
- `POST /api/scan/simulate` - Simulate IoT device scan
- `GET /api/dashboard/stats` - System statistics
- `GET /api/logs/attendance` - Attendance records
- `GET /api/logs/access` - Access logs
- `GET /api/sessions` - Active sessions
- `GET /api/users` - User management
- `GET /api/rooms` - Room management

## 📄 License

This project is developed for Liceo de Cagayan University as part of an IoT Capstone project.

## 🤝 Contributing

This is an academic project. For questions or suggestions, please contact the development team.

## 📞 Support

For technical support or questions about the system, please refer to the project documentation or contact the system administrators. 