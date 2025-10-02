import React from 'react';
import { useQuery } from 'react-query';
import axios from 'axios';
import {
  UsersIcon,
  ClipboardDocumentListIcon,
  BuildingOfficeIcon,
  ClockIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon
} from '@heroicons/react/24/outline';

const StatCard = ({ title, value, icon: Icon, color = 'primary', change, changeType }) => {
  const colorClasses = {
    primary: 'bg-primary-50 text-primary-600',
    success: 'bg-success-50 text-success-600',
    warning: 'bg-warning-50 text-warning-600',
    danger: 'bg-danger-50 text-danger-600'
  };

  return (
    <div className="card">
      <div className="flex items-center">
        <div className={`p-3 rounded-lg ${colorClasses[color]}`}>
          <Icon className="h-6 w-6" />
        </div>
        <div className="ml-4 flex-1">
          <p className="text-sm font-medium text-gray-600">{title}</p>
          <p className="text-2xl font-semibold text-gray-900">{value}</p>
          {change && (
            <p className={`text-sm ${changeType === 'increase' ? 'text-success-600' : 'text-danger-600'}`}>
              {changeType === 'increase' ? '↑' : '↓'} {change}
            </p>
          )}
        </div>
      </div>
    </div>
  );
};

function Dashboard() {
  const { data: stats, isLoading } = useQuery('dashboard-stats', async () => {
    const response = await axios.get('/api/dashboard/stats');
    return response.data;
  });

  const { data: recentActivity } = useQuery('recent-activity', async () => {
    const response = await axios.get('/api/dashboard/recent-activity');
    return response.data;
  });

  // Poll online IoT devices every 5 seconds
  const { data: devicesOnline } = useQuery('devices-online', async () => {
    const response = await axios.get('/api/devices/online');
    return response.data?.devices || [];
  }, {
    refetchInterval: 5000
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
        <p className="text-gray-600">Welcome to the IoT Attendance System</p>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
        <StatCard
          title="Total Users"
          value={stats?.totalUsers || 0}
          icon={UsersIcon}
          color="primary"
        />
        <StatCard
          title="Active Sessions"
          value={stats?.activeSessions || 0}
          icon={ClockIcon}
          color="success"
        />
        <StatCard
          title="Today's Attendance"
          value={stats?.todayAttendance || 0}
          icon={ClipboardDocumentListIcon}
          color="warning"
        />
        <StatCard
          title="Total Rooms"
          value={stats?.totalRooms || 0}
          icon={BuildingOfficeIcon}
          color="primary"
        />
      </div>

      {/* System Health */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="card">
          <h3 className="text-lg font-medium text-gray-900 mb-4">System Health</h3>
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center">
                <CheckCircleIcon className="h-5 w-5 text-success-500 mr-2" />
                <span className="text-sm text-gray-700">Database Connection</span>
              </div>
              <span className="badge-success">Online</span>
            </div>
            <div className="flex items-center justify-between">
              <div className="flex items-center">
                <CheckCircleIcon className="h-5 w-5 text-success-500 mr-2" />
                <span className="text-sm text-gray-700">API Server</span>
              </div>
              <span className="badge-success">Running</span>
            </div>
            <div className="flex items-center justify-between">
              <div className="flex items-center">
                <ExclamationTriangleIcon className={`h-5 w-5 mr-2 ${devicesOnline && devicesOnline.length > 0 ? 'text-success-500' : 'text-danger-500'}`} />
                <span className="text-sm text-gray-700">IoT Devices</span>
              </div>
              {devicesOnline && devicesOnline.length > 0 ? (
                <span className="badge-success">Online: {devicesOnline.length}</span>
              ) : (
                <span className="badge-danger">Offline</span>
              )}
            </div>
            {devicesOnline && devicesOnline.length > 0 && (
              <div className="mt-2 space-y-1">
                {devicesOnline.slice(0, 5).map((d) => (
                  <div key={d.deviceId} className="flex text-xs text-gray-600 justify-between">
                    <span>{d.deviceType || 'Device'}{d.roomNumber ? ` • Room ${d.roomNumber}` : d.location ? ` • ${d.location}` : ''}</span>
                    <span className="text-gray-400">{d.ipAddress || d.hostname || ''}</span>
                  </div>
                ))}
                {devicesOnline.length > 5 && (
                  <div className="text-xs text-gray-400">and {devicesOnline.length - 5} more…</div>
                )}
              </div>
            )}
          </div>
        </div>

        <div className="card">
          <h3 className="text-lg font-medium text-gray-900 mb-4">Recent Activity</h3>
          <div className="space-y-3">
            {recentActivity?.length > 0 ? (
              recentActivity.slice(0, 5).map((activity, index) => (
                <div key={index} className="flex items-center text-sm">
                  <div className="w-2 h-2 bg-primary-400 rounded-full mr-3"></div>
                  <span className="text-gray-700">{activity.description}</span>
                  <span className="text-gray-500 ml-auto">{activity.time}</span>
                </div>
              ))
            ) : (
              <p className="text-gray-500 text-sm">No recent activity</p>
            )}
          </div>
        </div>
      </div>

      {/* Quick Actions */}
      <div className="card">
        <h3 className="text-lg font-medium text-gray-900 mb-4">Quick Actions</h3>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <button className="btn-outline">
            View Attendance
          </button>
          <button className="btn-outline">
            Manage Users
          </button>
        </div>
      </div>

      {/* Today's Sessions */}
      <div className="card">
        <h3 className="text-lg font-medium text-gray-900 mb-4">Today's Sessions</h3>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Time</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Instructor</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {stats?.todaySessions?.length > 0 ? (
                stats.todaySessions.map((session, index) => (
                  <tr key={index}>
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                      {session.subject_name}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {session.room_number}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {session.start_time} - {session.end_time}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className={`badge ${
                        session.status === 'active' ? 'badge-success' :
                        session.status === 'scheduled' ? 'badge-warning' :
                        'badge-gray'
                      }`}>
                        {session.status}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {session.instructor_name}
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan="5" className="px-6 py-4 text-center text-gray-500">
                    No sessions scheduled for today
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

export default Dashboard; 