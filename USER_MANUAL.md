IoT Attendance System - User Manual
Liceo de Cagayan University

Table of Contents
1. Prerequisites
2. Installing Dependencies
3. Setting Up the Database (MySQL)
4. Running the Full-Stack Application
5. Additional Configuration
6. Troubleshooting

PREREQUISITES

📚 *Technical Knowledge Requirements*
- *Basic knowledge of JavaScript, React.js, Node.js, Express.js, and MySQL*
- *Understanding of REST APIs and JSON data format*
- *Basic command line/terminal operations*
- *Fundamental knowledge of Arduino IDE and C++ programming*
- *Basic understanding of electronics and circuit wiring*
- *Network configuration knowledge (WiFi, IP addresses)*

💻 *Software Requirements*

*Development Environment*
- *Node.js* (v16 or higher)
- *npm* or *yarn* package manager
- *MySQL Server* (v8.0 or higher)
- *Arduino IDE* (latest version)
- *Git* (for version control)
- *A code editor* (Visual Studio Code recommended)

*Browser Requirements*
- *Modern web browser* (Chrome, Firefox, Safari, Edge)
- *JavaScript enabled*
- *Local storage support*

*Arduino Libraries (Auto-installed via Arduino IDE)*
- *ESP32 Board Package*
- *Adafruit Fingerprint Sensor Library*
- *ArduinoJson Library*
- *WiFi Library* (built-in)
- *HTTPClient Library* (built-in)
- *WebServer Library* (built-in)

🔧 *Hardware Requirements*

*Computer System*
- *Windows 10/11, macOS, or Linux*
- *Minimum 4GB RAM* (8GB recommended)
- *At least 2GB free disk space*
- *USB ports* for hardware connections
- *WiFi capability* or Ethernet connection

*IoT Hardware Components*
- *ESP32 Development Board* (main microcontroller)
- *R307 Fingerprint Scanner* (biometric authentication)
- *USB RFID Scanner* (card-based authentication)
- *5V Relay Module* (for solenoid lock control)
- *12V Solenoid Lock* (door access control)
- *12V Power Supply* (for solenoid operation)
- *Breadboard and jumper wires*
- *LEDs* (status indicators - optional)
- *Buzzer* (audio feedback - optional)
- *Resistors and diodes* (circuit protection)

🌐 *Network Requirements*
- *2.4GHz WiFi Network* (ESP32 compatible)
- *Local Area Network* (all devices on same network)
- *Internet connection* (for initial setup and updates)
- *Router with DHCP enabled*
- *Firewall configuration* (if applicable)

🗄️ *Database Requirements*
- *MySQL Server* installed and running
- *Database administrator privileges*
- *Minimum 100MB database storage*
- *UTF-8 character set support*

🔐 *Access Requirements*
- *Administrator privileges* on development computer
- *Permission to install software*
- *Permission to configure firewall/network settings*
- *Access to MySQL root account* or database creation privileges

📱 *Optional Requirements*
- *Mobile device* (for testing responsive design)
- *Multiple RFID cards* (for user testing)
- *Multimeter* (for hardware troubleshooting)
- *Soldering equipment* (for permanent installations)

🎓 *Institutional Requirements*
- *Academic schedule data* (courses, students, schedules)
- *User management policies* (roles and permissions)
- *Room/facility information*
- *Network security compliance*

📦 INSTALLING DEPENDENCIES

📁 *Project Structure*
```
IOTCapstone/
├── frontend/          # React.js application
├── backend/           # Express.js server  
├── database/          # Database schema files
├── package.json       # Root package file
└── .env.example       # Environment template
```

🚀 *Quick Installation (Recommended)*

*Option 1: Install All Dependencies at Once*
1. Open your terminal
2. Navigate to the project root directory:
   ```
   cd path/to/your/project/IOTCapstone
   ```
3. Install all dependencies automatically:
   ```
   npm run install-all
   ```
   *This command installs dependencies for root, backend, and frontend simultaneously*

📦 *Manual Installation (Alternative)*

*Frontend (React.js)*
1. Open your terminal
2. Navigate to the frontend directory:
   ```
   cd path/to/your/project/IOTCapstone/frontend
   ```
3. Install the required dependencies:
   ```
   npm install
   ```

*Backend (Express.js)*
1. Open a new terminal or use the existing one
2. Navigate to the backend directory:
   ```
   cd path/to/your/project/IOTCapstone/backend
   ```
3. Install the required dependencies:
   ```
   npm install
   ```

*Root Dependencies*
1. Navigate to the project root:
   ```
   cd path/to/your/project/IOTCapstone
   ```
2. Install root dependencies:
   ```
   npm install
   ```

🗄️ SETTING UP THE DATABASE (MYSQL)

🗄️ *Installing MySQL*
1. *Download and install MySQL* from the official website (https://dev.mysql.com/downloads/)
2. *Follow the installation instructions* specific to your operating system
3. *Remember your root password* - you'll need it for configuration
4. *Start the MySQL service* after installation

⚙️ *Environment Configuration*
1. *Navigate to the backend directory:*
   ```
   cd path/to/your/project/IOTCapstone/backend
   ```

2. *Create environment file:*
   ```
   # Copy the example file
   cp env.example .env
   ```

    3. *Edit the .env file* with your database credentials:
    ```env
    # Database Configuration
    DB_HOST=localhost
    DB_PORT=3306
    DB_USER=root
    DB_PASSWORD=
    DB_NAME=iot_attendance

    # Server Configuration
    PORT=5000
    NODE_ENV=development

    # JWT Configuration
    JWT_SECRET=iot_attendance_super_secret_jwt_key_2024
    JWT_EXPIRES_IN=7d

    # CORS Configuration
    CORS_ORIGIN=http://localhost:3000

    # Rate Limiting
    RATE_LIMIT_WINDOW_MS=900000
    RATE_LIMIT_MAX_REQUESTS=1000

    # Academic Year Settings
    DEFAULT_ACADEMIC_YEAR=2024-2025
    DEFAULT_SEMESTER=First Semester

🏗️ *Database Setup and Initialization*

*Method 1: Automated Setup (Recommended)*
1. *Ensure you are in the backend directory:*
   ```
   cd path/to/your/project/IOTCapstone/backend
   ```

2. *Run the database setup script:*
   ```
   npm run setup-db
   ```
   *This will automatically create the database, tables, and sample data*

3. *Set the admin password:*
   ```
   npm run set-admin-password
   ```
   *This creates the admin user with default credentials*

*Method 2: Manual Setup (Alternative)*
1. *Open the MySQL command line client* or MySQL Workbench
2. *Log in to your MySQL server*
3. *Create the database:*
   ```sql
   CREATE DATABASE iot_attendance;
   USE iot_attendance;
   ```

4. *Import the schema file:*
   ```
   # From the project root directory
   mysql -u root -p iot_attendance < database/schema.sql
   ```

5. *Run the setup script manually:*
   ```
   cd backend
   node scripts/setupDatabase.js
   node scripts/setAdminPassword.js
   ```

🔑 *Default Login Credentials*
After database setup, use these credentials to access the system:
- *Email:* `admin@liceo.edu.ph`
- *Password:* `admin123`

🚀 RUNNING THE FULL-STACK APPLICATION

🚀 *Quick Start (Recommended)*

*Option 1: Start Both Servers Simultaneously*
1. *Navigate to the frontend directory:*
   ```
   cd path/to/your/project/IOTCapstone/frontend
   ```

2. *Start the complete system:*
   ```
   npm run dev
   ```
   *This starts both frontend (React) and backend (Express) servers with colored logs*

*Option 2: Start from Root Directory*
1. *Navigate to the project root:*
   ```
   cd path/to/your/project/IOTCapstone
   ```

2. *Start the complete system:*
   ```
   npm run dev
   ```

🔧 *Manual Startup (Alternative)*

*Running the Backend (Express.js)*
1. *Open a terminal and navigate to the backend directory:*
   ```
   cd path/to/your/project/IOTCapstone/backend
   ```

2. *Start the Express server:*
   ```
   npm run dev
   ```
   *For production use: `npm start`*

*Running the Frontend (React.js)*
1. *Open a new terminal and navigate to the frontend directory:*
   ```
   cd path/to/your/project/IOTCapstone/frontend
   ```

2. *Start the React application:*
   ```
   npm start
   ```
   *For development with hot reload*

🌐 *Server Information*
After successful startup, your applications will be available at:
- *Frontend (React):* http://localhost:3000
- *Backend (Express API):* http://localhost:5000
- *API Health Check:* http://localhost:5000/api/health

🖥️ *Windows Quick Start*
For Windows users, you can use the provided batch file:
```
# Double-click or run from command prompt
start_iot_system.bat
```

⚙️ ADDITIONAL CONFIGURATION

📱 *Hardware Setup (Optional)*
If you're setting up the IoT hardware components:
1. *Arduino IDE Setup:* Install ESP32 board package and required libraries
2. *Upload Arduino sketches* from the `sketch_feb18a_copy_20250803144501/` directory
3. *Configure WiFi credentials* in the Arduino code
4. *Connect hardware components* according to the wiring diagrams

🧪 *Testing the Installation*
1. *Access the application:* Navigate to http://localhost:3000
2. *Login with admin credentials:* admin@liceo.edu.ph / admin123
3. *Check the test page:* Navigate to http://localhost:3000/test
4. *Verify API connectivity:* Visit http://localhost:5000/api/health

📊 *Available Scripts Reference*

*Root Directory Scripts*
```
npm run dev          # Start both frontend and backend
npm run server       # Start backend only
npm run client       # Start frontend only
npm run build        # Build frontend for production
npm run setup-db     # Setup database
npm run install-all  # Install all dependencies
```

*Frontend Scripts (from /frontend directory)*
```
npm run dev          # Start both frontend and backend
npm run frontend     # Start frontend only
npm run backend      # Start backend only
npm start            # Start frontend only
npm run build        # Build for production
```

*Backend Scripts (from /backend directory)*
```
npm start            # Start Express server
npm run dev          # Start with nodemon (auto-restart)
npm run setup-db     # Create database and tables
npm run set-admin-password  # Set admin password
```

🐛 TROUBLESHOOTING

🐛 *Common Issues and Solutions*

*Port Already in Use*
```
# Windows
taskkill /F /IM node.exe

# macOS/Linux  
killall node
```

*Database Connection Issues*
1. *Verify MySQL is running:*
   ```
   # Windows
   net start mysql
   
   # macOS
   sudo /usr/local/mysql/support-files/mysql.server start
   
   # Linux
   sudo systemctl start mysql
   ```

2. *Check .env file credentials* in `backend/.env`
3. *Test database connection manually:*
   ```
   mysql -u root -p
   ```

*Module Not Found Errors*
```
# Reinstall all dependencies
npm run install-all

# Or manually clear and reinstall
rm -rf node_modules package-lock.json
npm install
```

*Permission Denied Errors*
```
# Fix npm permissions (macOS/Linux)
sudo chown -R $(whoami) ~/.npm
sudo chown -R $(whoami) /usr/local/lib/node_modules
```

*Frontend Won't Start*
1. *Clear React cache:*
   ```
   cd frontend
   rm -rf node_modules/.cache
   npm start
   ```

2. *Check port 3000 availability:*
   ```
   # Windows
   netstat -ano | findstr :3000
   
   # macOS/Linux
   lsof -ti:3000
   ```

*Backend API Errors*
1. *Check backend logs* for detailed error messages
2. *Verify database connection* in backend/.env
3. *Ensure all required environment variables* are set
4. *Test API endpoints* using curl or Postman

*Hardware Connection Issues*
1. *Check Arduino IDE serial monitor* for ESP32 status
2. *Verify WiFi credentials* in Arduino code
3. *Ensure ESP32 and computer are on same network*
4. *Check firewall settings* for ports 5000 and 8888

📞 *Getting Help*
If you encounter issues not covered in this troubleshooting section:
1. *Check the console/terminal* for detailed error messages
2. *Review the system logs* in both frontend and backend
3. *Verify all prerequisites* are properly installed
4. *Test individual components* separately
5. *Consult the existing documentation* files in the project

📄 *Additional Resources*

📚 *Documentation Files*
- `README.md` - Project overview and quick start
- `DEVELOPMENT.md` - Development environment setup
- `FINGERPRINT_SETUP_GUIDE.md` - Hardware setup for fingerprint scanner
- `RFID_SOLENOID_INTEGRATION.md` - RFID and door lock setup
- `TESTING_GUIDE_SCENARIOS.md` - System testing procedures

🔗 *Useful URLs*
- *Frontend:* http://localhost:3000
- *Backend API:* http://localhost:5000
- *API Health Check:* http://localhost:5000/api/health
- *Test Page:* http://localhost:3000/test
- *RFID Scanner:* http://localhost:3000/rfid-scanner

📱 SYSTEM PAGES OVERVIEW

🏠 THE ADMIN DASHBOARD PAGE

This is the main dashboard where administrators can view system statistics, monitor active sessions, track today's attendance, and access quick actions for managing the IoT Attendance System. The dashboard provides real-time insights into user activity, system health, and recent activities.

1. STATISTICS CARDS - Display key metrics including total users, active sessions, today's attendance, and total rooms.

2. SYSTEM HEALTH - Monitor system status and performance indicators.

3. RECENT ACTIVITY - View latest system activities and user actions.

4. QUICK ACTIONS - Access buttons for viewing attendance and managing users.

5. TODAY'S SESSIONS - Table showing scheduled sessions for the current day with status indicators.

📋 THE ATTENDANCE LOGS PAGE

This page displays comprehensive attendance records with real-time updates. Users can search, filter, and view detailed attendance logs including student information, session details, timestamps, and attendance status. The system automatically refreshes every 5 seconds to show new attendance records.

1. SEARCH FUNCTIONALITY - Search attendance records by student name, ID, or other criteria.

2. DATE FILTER - Filter attendance logs by specific dates or date ranges.

3. STATUS FILTER - Filter by attendance status (present, absent, late).

4. ATTENDANCE TABLE - Display detailed attendance records with student information, session details, and timestamps.

5. REAL-TIME UPDATES - Automatic refresh every 5 seconds to show new attendance records.

6. PAGINATION - Navigate through large sets of attendance data.

💳 THE RFID SCANNER PAGE

This is the interactive RFID scanning interface where users can scan RFID cards for attendance tracking. The page provides real-time scanning capabilities, displays scan history, and shows the status of the last scan. It's optimized for quick RFID card detection and processing.

1. SCAN INTERFACE - Real-time RFID card scanning with visual feedback.

2. SCAN STATUS - Display current scanning status and last scan information.

3. SCAN HISTORY - Show recent RFID scans with timestamps and results.

4. STUDENT INFORMATION - Display student details when a valid RFID card is scanned.

5. ATTENDANCE RECORDING - Automatically record attendance when valid cards are scanned.

⏰ THE SESSIONS PAGE

This page manages class sessions and attendance periods. Users can create, edit, and monitor active sessions, view session schedules, and track session status. It provides comprehensive session management for different subjects and time slots.

1. SESSION CREATION - Create new class sessions with subject, room, and time details.

2. SESSION LIST - View all sessions with status indicators and management options.

3. SESSION EDITING - Modify existing session details and settings.

4. SESSION STATUS - Monitor active, scheduled, and completed sessions.

5. SESSION FILTERING - Filter sessions by date, subject, room, or status.

👥 THE USERS PAGE

This page handles user management for the system. Administrators can add, edit, and manage user accounts including students, faculty, and staff. It provides user role management, account status control, and user information maintenance. This is also where fingerprint and RFID credentials are set up and managed for each user.

1. USER LIST - Display all system users with their roles and status.

2. ADD USER - Create new user accounts with role assignment.

3. EDIT USER - Modify user information and account settings.

4. USER ROLES - Manage user permissions and access levels.

5. USER SEARCH - Search and filter users by name, role, or status.

6. ACCOUNT STATUS - Enable, disable, or manage user account status.

7. FINGERPRINT SETUP - Configure and manage fingerprint authentication for users.

8. RFID SETUP - Set up and manage RFID card credentials for users.

🏢 THE ROOMS PAGE

This page manages classroom and facility information. Users can add, edit, and configure room details, assign rooms to sessions, and track room availability. It's essential for organizing attendance tracking by location.

1. ROOM LIST - Display all available rooms and facilities.

2. ADD ROOM - Create new room entries with capacity and equipment details.

3. EDIT ROOM - Modify room information and settings.

4. ROOM ASSIGNMENT - Assign rooms to specific sessions or subjects.

5. ROOM AVAILABILITY - Track room usage and availability status.
🏢 THE ROOMS PAGE

This page manages classroom and facility information. Users can add, edit, and configure room details, assign rooms to sessions, and track room availability. It's essential for organizing attendance tracking by location.

1. ROOM LIST - Display all available rooms and facilities.

2. ADD ROOM - Create new room entries with capacity and equipment details.

3. EDIT ROOM - Modify room information and settings.

4. ROOM ASSIGNMENT - Assign rooms to specific sessions or subjects.

5. ROOM AVAILABILITY - Track room usage and availability status.
📚 THE SUBJECTS PAGE

This page manages academic subjects and courses. Users can create, edit, and organize subject information, assign instructors, and link subjects to schedules. It provides the foundation for organizing attendance by academic subjects.

1. SUBJECT LIST - Display all academic subjects and courses.

2. ADD SUBJECT - Create new subjects with course codes and descriptions.

3. EDIT SUBJECT - Modify subject information and requirements.

4. INSTRUCTOR ASSIGNMENT - Assign faculty members to specific subjects.

5. SUBJECT SCHEDULING - Link subjects to class schedules and time slots.

📅 THE SCHEDULES PAGE

This page manages class schedules and timetables. Users can create, edit, and organize class schedules, assign subjects to time slots, and manage recurring schedules. It's crucial for automated attendance tracking based on scheduled classes.

1. SCHEDULE CREATION - Create new class schedules with time and date details.

2. SCHEDULE LIST - View all schedules with subject and room assignments.

3. SCHEDULE EDITING - Modify existing schedule details and timing.

4. RECURRING SCHEDULES - Set up repeating schedules for regular classes.

5. SCHEDULE CONFLICTS - Detect and resolve scheduling conflicts.

🔐 THE LOGIN PAGE

This is the secure authentication portal for the IoT Attendance System. Users can sign in with their credentials to access the system. The page features a modern, glassy design with maroon and white theming that matches the Liceo de Cagayan University branding.

1. EMAIL INPUT - Enter user email address for authentication.

2. PASSWORD INPUT - Enter user password with show/hide toggle.

3. SIGN IN BUTTON - Submit credentials to access the system.

4. SECURE ACCESS - Modern glassy design with university branding.

5. RESPONSIVE DESIGN - Optimized for desktop and mobile devices.

*This user manual provides comprehensive instructions for setting up and running the IoT Attendance System for Liceo de Cagayan University. For additional support or questions, refer to the project documentation or contact the development team.*
