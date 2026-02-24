import React from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import Layout from './components/Layout';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import AttendanceLogs from './pages/AttendanceLogs';
import Sessions from './pages/Sessions';
import Users from './pages/Users';
import Rooms from './pages/Rooms';
import Subjects from './pages/Subjects'; // Changed from Subjects to Subjects
import Schedules from './pages/Schedules';
import UnifiedManagement from './pages/UnifiedManagement';
import Reports from './pages/Reports';
import Archive from './pages/Archive';
import Backup from './pages/Backup';
import SuperAdmin from './pages/SuperAdmin';
import Settings from './pages/Settings';

// Protected Route Component - Only allows admin/superadmin users
const ProtectedRoute = ({ children }) => {
  const { user, loading } = useAuth();
  
  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="animate-spin rounded-full h-32 w-32 border-b-2 border-primary-600"></div>
      </div>
    );
  }
  
  // If no user, redirect to login
  if (!user) {
    return <Navigate to="/login" />;
  }
  
  // If user is not admin or superadmin, redirect to login with cleanup
  if (user.role !== 'admin' && user.role !== 'superadmin') {
    // Clear any stale session data
    localStorage.removeItem('token');
    return <Navigate to="/login" />;
  }
  
  return children;
};

// Access Denied Component for non-admin users
const AccessDenied = () => {
  const { logout } = useAuth();
  
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="text-center max-w-md mx-auto px-4">
        <h1 className="text-3xl font-bold text-gray-900 mb-4">Access Restricted</h1>
        <p className="text-gray-600 mb-2">
          Your account does not have permission to access the administration dashboard.
        </p>
        <p className="text-sm text-gray-500 mb-6">
          Only administrators can access this system. Please contact your system administrator if you believe this is an error.
        </p>
        <button
          onClick={logout}
          className="px-4 py-2 bg-maroon-600 text-white rounded-lg hover:bg-maroon-700 transition-colors"
        >
          Sign Out
        </button>
      </div>
    </div>
  );
};

// Public Route Component (redirect if authenticated)
const PublicRoute = ({ children }) => {
  const { user, loading } = useAuth();
  
  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="animate-spin rounded-full h-32 w-32 border-b-2 border-primary-600"></div>
      </div>
    );
  }
  
  if (user) {
    // Redirect admin/superadmin to dashboard
    if (user.role === 'admin' || user.role === 'superadmin') {
      return <Navigate to="/dashboard" />;
    }
    // Students and other roles should not be logged in - clear session and show login
    // This handles stale sessions
    return children;
  }
  
  return children;
};

const AdminRoute = ({ children }) => {
  const { user, loading } = useAuth();

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="animate-spin rounded-full h-32 w-32 border-b-2 border-primary-600"></div>
      </div>
    );
  }

  if (user?.role === 'admin' || user?.role === 'superadmin') {
    return children;
  }

  // Students and other roles get access denied
  return <AccessDenied />;
};

const SuperAdminRoute = ({ children }) => {
  const { user, loading } = useAuth();

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="animate-spin rounded-full h-32 w-32 border-b-2 border-primary-600"></div>
      </div>
    );
  }

  if (user?.role === 'superadmin') {
    return children;
  }

  return <Navigate to="/dashboard" />;
};

function AppRoutes() {
  return (
    <Router>
      <Routes>
        <Route path="/login" element={<PublicRoute><Login /></PublicRoute>} />
        <Route path="/" element={<ProtectedRoute><Layout /></ProtectedRoute>}>
          <Route index element={<Navigate to="/dashboard" />} />
          <Route path="dashboard" element={<AdminRoute><Dashboard /></AdminRoute>} />
          <Route path="management" element={<AdminRoute><UnifiedManagement /></AdminRoute>} />
          <Route path="subjects" element={<AdminRoute><Subjects /></AdminRoute>} />
          <Route path="schedules" element={<AdminRoute><Schedules /></AdminRoute>} />
          <Route path="rooms" element={<AdminRoute><Rooms /></AdminRoute>} />
          <Route path="users" element={<AdminRoute><Users /></AdminRoute>} />
          <Route path="sessions" element={<AdminRoute><Sessions /></AdminRoute>} />
          <Route path="attendance-logs" element={<AdminRoute><AttendanceLogs /></AdminRoute>} />
          <Route path="reports" element={<AdminRoute><Reports /></AdminRoute>} />
          <Route path="archive" element={<AdminRoute><Archive /></AdminRoute>} />
          <Route path="backup" element={<AdminRoute><Backup /></AdminRoute>} />
          <Route path="settings" element={<AdminRoute><Settings /></AdminRoute>} />
          <Route
            path="superadmin"
            element={
              <SuperAdminRoute>
                <SuperAdmin />
              </SuperAdminRoute>
            }
          />
        </Route>
      </Routes>
    </Router>
  );
}

function App() {
  return (
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  );
}

export default App; 
