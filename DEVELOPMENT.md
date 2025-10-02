# IoT Attendance System - Development Guide

## 🚀 Quick Start

### Prerequisites
- Node.js (v16 or higher)
- MySQL Server
- Git

### Development Setup

1. **Navigate to frontend directory:**
   ```bash
   cd IOTCapstone/frontend
   ```

2. **Run the development environment:**
   ```bash
   npm run dev
   ```
   This will start both the frontend (React) and backend (Express) servers simultaneously.

## 📋 Available Scripts

### Frontend Scripts (run from `/frontend` directory)

- `npm run dev` - **Start both frontend and backend** (recommended for development)
- `npm run frontend` - Start only the frontend React server
- `npm run backend` - Start only the backend Express server
- `npm start` - Start only the frontend (same as frontend)
- `npm run build` - Build the React app for production

### Backend Scripts (run from `/backend` directory)

- `npm start` - Start the Express server
- `npm run dev` - Start with nodemon (auto-restart on changes)
- `npm run setup-db` - Create database and tables
- `npm run set-admin-password` - Set admin password

## 🌐 Server Information

- **Frontend (React):** http://localhost:3000
- **Backend (Express API):** http://localhost:5000
- **API Health Check:** http://localhost:5000/api/health

## 🔑 Default Login Credentials

- **Email:** admin@liceo.edu.ph
- **Password:** admin123

## 🔧 Database Setup

If you need to set up the database:

```bash
cd backend
npm run setup-db
npm run set-admin-password
```

## 📱 Development Features

- **Hot Reload:** Frontend automatically reloads on file changes
- **API Proxy:** Frontend proxies API requests to backend
- **Colored Logs:** Different colors for frontend/backend logs
- **Concurrent Execution:** Both servers run simultaneously

## 🐛 Troubleshooting

### Port Already in Use
If you get "port already in use" errors:
```bash
# Kill all node processes
taskkill /F /IM node.exe
```

### Database Connection Issues
1. Make sure MySQL is running
2. Check `.env` file in backend directory
3. Run database setup: `npm run setup-db`

### Login Issues
1. Verify admin password: `npm run set-admin-password`
2. Check browser network tab for API errors
3. Verify backend is running on port 5000 

## 🚀 Quick Start

### Prerequisites
- Node.js (v16 or higher)
- MySQL Server
- Git

### Development Setup

1. **Navigate to frontend directory:**
   ```bash
   cd IOTCapstone/frontend
   ```

2. **Run the development environment:**
   ```bash
   npm run dev
   ```
   This will start both the frontend (React) and backend (Express) servers simultaneously.

## 📋 Available Scripts

### Frontend Scripts (run from `/frontend` directory)

- `npm run dev` - **Start both frontend and backend** (recommended for development)
- `npm run frontend` - Start only the frontend React server
- `npm run backend` - Start only the backend Express server
- `npm start` - Start only the frontend (same as frontend)
- `npm run build` - Build the React app for production

### Backend Scripts (run from `/backend` directory)

- `npm start` - Start the Express server
- `npm run dev` - Start with nodemon (auto-restart on changes)
- `npm run setup-db` - Create database and tables
- `npm run set-admin-password` - Set admin password

## 🌐 Server Information

- **Frontend (React):** http://localhost:3000
- **Backend (Express API):** http://localhost:5000
- **API Health Check:** http://localhost:5000/api/health

## 🔑 Default Login Credentials

- **Email:** admin@liceo.edu.ph
- **Password:** admin123

## 🔧 Database Setup

If you need to set up the database:

```bash
cd backend
npm run setup-db
npm run set-admin-password
```

## 📱 Development Features

- **Hot Reload:** Frontend automatically reloads on file changes
- **API Proxy:** Frontend proxies API requests to backend
- **Colored Logs:** Different colors for frontend/backend logs
- **Concurrent Execution:** Both servers run simultaneously

## 🐛 Troubleshooting

### Port Already in Use
If you get "port already in use" errors:
```bash
# Kill all node processes
taskkill /F /IM node.exe
```

### Database Connection Issues
1. Make sure MySQL is running
2. Check `.env` file in backend directory
3. Run database setup: `npm run setup-db`

### Login Issues
1. Verify admin password: `npm run set-admin-password`
2. Check browser network tab for API errors
3. Verify backend is running on port 5000 