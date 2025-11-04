import React, { useState, useEffect, useRef } from 'react';
import axios from 'axios';
import { toast } from 'react-hot-toast';
import {
  MagnifyingGlassIcon,
  CalendarDaysIcon,
  ClockIcon,
  UserIcon,
  MapPinIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  XCircleIcon,
  FingerPrintIcon,
  ArrowPathIcon
} from '@heroicons/react/24/outline';

function AttendanceLogs() {
  const [logs, setLogs] = useState([]);
  const [loading, setLoading] = useState(true);
  const [searchTerm, setSearchTerm] = useState('');
  const [dateFilter, setDateFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [lastUpdated, setLastUpdated] = useState(null);
  const [newlyInsertedIds, setNewlyInsertedIds] = useState([]);
  const logsPerPage = 10;
  const isRefreshingRef = useRef(false);
  const seenAnimatedIdsRef = useRef(new Set());

  const getRowId = (log) => {
    return (
      log?.ATTENDANCEID ??
      (log?.STUDENTID && log?.SCANDATETIME ? `${log.STUDENTID}-${log.SCANDATETIME}` : undefined)
    );
  };

  useEffect(() => {
    fetchAttendanceLogs();
  }, [currentPage, searchTerm, dateFilter, statusFilter]);

  // Auto-refresh every 5 seconds to show new attendance records
  // Recreate interval when filters/pagination change so it uses fresh params
  useEffect(() => {
    const interval = setInterval(() => {
      if (!isRefreshingRef.current) {
        fetchAttendanceLogsSilently();
      }
    }, 5000);

    return () => clearInterval(interval);
  }, [currentPage, searchTerm, dateFilter, statusFilter]);

  const fetchAttendanceLogs = async () => {
    try {
      setLoading(true);
      const params = new URLSearchParams({
        page: currentPage,
        limit: logsPerPage,
        ...(searchTerm && { search: searchTerm }),
        ...(dateFilter && { date: dateFilter }),
        ...(statusFilter && { status: statusFilter })
      });

      const response = await axios.get(`http://localhost:5000/api/logs/attendance?${params}`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });

      setLogs(response.data.logs || []);
      // Use totalPages from response if available, otherwise calculate from total
      setTotalPages(response.data.totalPages || Math.ceil((response.data.total || 0) / logsPerPage));
      setLastUpdated(new Date());
    } catch (error) {
      console.error('Error fetching attendance logs:', error);
      toast.error('Failed to load attendance logs');
      setLogs([]);
    } finally {
      setLoading(false);
    }
  };

  const fetchAttendanceLogsSilently = async () => {
    try {
      if (isRefreshingRef.current) return;
      isRefreshingRef.current = true;

      const params = new URLSearchParams({
        page: currentPage,
        limit: logsPerPage,
        ...(searchTerm && { search: searchTerm }),
        ...(dateFilter && { date: dateFilter }),
        ...(statusFilter && { status: statusFilter })
      });

      const response = await axios.get(`http://localhost:5000/api/logs/attendance?${params}`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });

      // Calculate new items for smooth live insert animation
      const fetchedLogs = response.data.logs || [];
      const currentIds = new Set((logs || []).map(l => getRowId(l)).filter(Boolean));
      const newItems = fetchedLogs.filter(l => {
        const id = getRowId(l);
        return id && !currentIds.has(id);
      });

              if (newItems.length > 0) {
        const merged = [...newItems, ...logs].slice(0, logsPerPage);
        setLogs(merged);
        // Use totalPages from response if available, otherwise calculate from total
        setTotalPages(response.data.totalPages || Math.ceil((response.data.total || 0) / logsPerPage));
        setLastUpdated(new Date());

        // Animate only once per ID: pick the top-most unseen new ID
        const firstNewId = getRowId(newItems[0]);
        if (firstNewId && !seenAnimatedIdsRef.current.has(firstNewId)) {
          seenAnimatedIdsRef.current.add(firstNewId);
          setNewlyInsertedIds([firstNewId]);
          setTimeout(() => {
            setNewlyInsertedIds([]);
          }, 800);
        } else {
          // Ensure no animation if we've already animated this ID
          setNewlyInsertedIds([]);
        }
      }
    } catch (error) {
      console.error('Error silently fetching attendance logs:', error);
      // Don't show error toast for silent refresh
    } finally {
      isRefreshingRef.current = false;
    }
  };

  const LogRow = ({ log, isNew }) => {
    const [mounted, setMounted] = useState(false);

    useEffect(() => {
      // Trigger transition on mount for new rows
      if (isNew) {
        const t = requestAnimationFrame(() => setMounted(true));
        return () => cancelAnimationFrame(t);
      }
    }, [isNew]);

    const baseRowClasses = "hover:bg-gray-50 transition-colors duration-200";
    const highlightClasses = isNew ? ' bg-green-50' : '';
    const animateClasses = isNew
      ? `${mounted ? 'opacity-100 translate-x-0' : 'opacity-0 translate-x-8'} transition-all duration-500 ease-out will-change-transform will-change-opacity transform`
      : '';

    return (
      <tr className={baseRowClasses + highlightClasses}>
        <td className="px-6 py-4 whitespace-nowrap">
          <div className={`flex items-center ${animateClasses}`}>
            <UserIcon className="h-8 w-8 text-gray-400 mr-3" />
            <div>
              <div className="text-sm font-medium text-gray-900">
                {log.FIRSTNAME} {log.LASTNAME}
              </div>
              <div className="text-sm text-gray-500">
                ID: {log.STUDENTID || 'N/A'}
              </div>
            </div>
          </div>
        </td>
        <td className="px-6 py-4 whitespace-nowrap">
          <div className={`flex items-center ${animateClasses}`}>
            <ClockIcon className="h-5 w-5 text-gray-400 mr-2" />
            <div>
              <div className="text-sm font-medium text-gray-900">
                {formatDate(log.SCANDATETIME || log.DATE)}
              </div>
              <div className="text-sm text-gray-500">
                {formatTime(log.SCANDATETIME)}
              </div>
            </div>
          </div>
        </td>
        <td className="px-6 py-4 whitespace-nowrap">
          <div className={`flex items-center ${animateClasses}`}>
            {getStatusIcon(log.STATUS)}
            <span className={`ml-2 inline-flex px-2 py-1 text-xs font-semibold rounded-full ${getStatusColor(log.STATUS)}`}>
              {log.STATUS || 'Unknown'}
            </span>
          </div>
        </td>
        <td className="px-6 py-4 whitespace-nowrap">
          <div className={`text-sm text-gray-900 ${animateClasses}`}>
            {log.ACTIONTYPE || 'N/A'}
          </div>
        </td>
        {/* Methods column removed */}
        <td className="px-6 py-4 whitespace-nowrap">
          <div className={`flex items-center ${animateClasses}`}>
            <MapPinIcon className="h-5 w-5 text-gray-400 mr-2" />
            <span className="text-sm text-gray-900">{log.LOCATION || 'Unknown'}</span>
          </div>
        </td>
        <td className="px-6 py-4 whitespace-nowrap">
          <div className={`text-sm text-gray-900 ${animateClasses}`}>
            {log.ROOMNUMBER || 'N/A'}
          </div>
          <div className={`text-sm text-gray-500 ${animateClasses}`}>
            {log.ROOMNAME || ''}
          </div>
        </td>
        <td className="px-6 py-4 whitespace-nowrap">
          <div className={`text-sm text-gray-900 ${animateClasses}`}>
            {log.SUBJECTCODE || 'N/A'}
          </div>
          <div className={`text-sm text-gray-500 ${animateClasses}`}>
            {log.SUBJECTNAME || ''}
          </div>
          <div className={`text-xs text-gray-400 ${animateClasses}`}>
            {log.ACADEMICYEAR} - {log.SEMESTER}
          </div>
        </td>
      </tr>
    );
  };

  const getStatusIcon = (status) => {
    switch (status?.toLowerCase()) {
      case 'present':
        return <CheckCircleIcon className="h-5 w-5 text-green-500" />;
      case 'late':
        return <ExclamationTriangleIcon className="h-5 w-5 text-yellow-500" />;
      case 'absent':
        return <XCircleIcon className="h-5 w-5 text-red-500" />;
      default:
        return <ClockIcon className="h-5 w-5 text-gray-500" />;
    }
  };

  const getStatusColor = (status) => {
    switch (status?.toLowerCase()) {
      case 'present':
        return 'bg-green-100 text-green-800';
      case 'late':
        return 'bg-yellow-100 text-yellow-800';
      case 'absent':
        return 'bg-red-100 text-red-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const formatDateTime = (dateTime) => {
    if (!dateTime) return 'N/A';

    // Parse the datetime string from database (assumes it's in local time)
    const date = new Date(dateTime);

    // Display in user's local timezone (which should be Philippine time)
    return date.toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: true
    });
  };

  const formatDate = (dateValue) => {
    if (!dateValue) return '';
    
    // Handle both date strings and datetime strings
    const date = new Date(dateValue);
    
    // Format as a simple date: "Nov 3, 2025"
    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    });
  };

  const formatTime = (dateTime) => {
    if (!dateTime) return '';
    
    const date = new Date(dateTime);
    
    // Format as time only: "08:14 PM"
    return date.toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    });
  };

  const handleSearch = (e) => {
    setSearchTerm(e.target.value);
    setCurrentPage(1);
  };

  const handleDateFilter = (e) => {
    setDateFilter(e.target.value);
    setCurrentPage(1);
  };

  const handleStatusFilter = (e) => {
    setStatusFilter(e.target.value);
    setCurrentPage(1);
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Attendance Logs</h1>
          <p className="text-gray-600">View and manage attendance records from fingerprint and RFID scans</p>
          {lastUpdated && (
            <p className="text-sm text-gray-500 mt-1">
              Last updated: {lastUpdated.toLocaleTimeString('en-US', { timeZone: 'Asia/Manila' })}
              <span className="ml-2 inline-flex items-center px-2 py-1 rounded-full text-xs bg-green-100 text-green-800">
                Auto-refresh: 5s
              </span>
            </p>
          )}
        </div>
        <button
          onClick={fetchAttendanceLogs}
          disabled={loading}
          className="flex items-center px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors duration-200"
        >
          <ArrowPathIcon className={`h-4 w-4 mr-2 ${loading ? 'animate-spin' : ''}`} />
          {loading ? 'Refreshing...' : 'Refresh'}
        </button>
      </div>

      {/* Filters */}
      <div className="card">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
          {/* Search */}
          <div className="relative">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 transform -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="text"
              placeholder="Search by name, student ID..."
              value={searchTerm}
              onChange={handleSearch}
              className="pl-10 w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500"
            />
          </div>

          {/* Date Filter */}
          <div className="relative">
            <CalendarDaysIcon className="absolute left-3 top-1/2 transform -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="date"
              value={dateFilter}
              onChange={handleDateFilter}
              className="pl-10 w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500"
            />
          </div>

          {/* Status Filter */}
          <div>
            <select
              value={statusFilter}
              onChange={handleStatusFilter}
              className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500"
            >
              <option value="">All Status</option>
              <option value="Present">Present</option>
              <option value="Late">Late</option>
              <option value="Absent">Absent</option>
            </select>
          </div>

          {/* Refresh Button */}
          <div>
            <button
              onClick={() => {
                setSearchTerm('');
                setDateFilter('');
                setStatusFilter('');
                setCurrentPage(1);
                fetchAttendanceLogs();
              }}
              className="w-full px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors duration-200"
            >
              Refresh
            </button>
          </div>
        </div>
      </div>

      {/* Attendance Logs Table */}
      <div className="card">
        {loading ? (
          <div className="text-center py-12">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto"></div>
            <p className="mt-4 text-gray-500">Loading attendance logs...</p>
          </div>
        ) : logs.length === 0 ? (
          <div className="text-center py-12">
            <FingerPrintIcon className="h-12 w-12 text-gray-400 mx-auto mb-4" />
            <h3 className="text-lg font-medium text-gray-900 mb-2">No Attendance Records</h3>
            <p className="text-gray-500">No attendance logs found. Try adjusting your filters or scan some fingerprints!</p>
          </div>
        ) : (
          <>
            <div className="overflow-x-auto">
              <div>
                <table className="min-w-full divide-y divide-gray-200 transition-opacity duration-300">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      User
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Date & Time
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Status
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Action Type
                    </th>
                    {/* Methods column header removed */}
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Location
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Room
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Subject
                    </th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {logs.map((log) => (
                    <LogRow
                      key={getRowId(log)}
                      log={log}
                      isNew={newlyInsertedIds.includes(getRowId(log))}
                    />
                  ))}
                </tbody>
                </table>
              </div>
            </div>

            {/* Pagination */}
            {totalPages > 1 && (
              <div className="bg-white px-4 py-3 flex items-center justify-between border-t border-gray-200 sm:px-6">
                <div className="flex-1 flex justify-between sm:hidden">
                  <button
                    onClick={() => setCurrentPage(Math.max(1, currentPage - 1))}
                    disabled={currentPage === 1}
                    className="relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Previous
                  </button>
                  <button
                    onClick={() => setCurrentPage(Math.min(totalPages, currentPage + 1))}
                    disabled={currentPage === totalPages}
                    className="ml-3 relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Next
                  </button>
                </div>
                <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                  <div>
                    <p className="text-sm text-gray-700">
                      Showing page <span className="font-medium">{currentPage}</span> of{' '}
                      <span className="font-medium">{totalPages}</span>
                    </p>
                  </div>
                  <div>
                    <nav className="relative z-0 inline-flex rounded-md shadow-sm -space-x-px">
                      <button
                        onClick={() => setCurrentPage(Math.max(1, currentPage - 1))}
                        disabled={currentPage === 1}
                        className="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        Previous
                      </button>
                      <button
                        onClick={() => setCurrentPage(Math.min(totalPages, currentPage + 1))}
                        disabled={currentPage === totalPages}
                        className="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        Next
                      </button>
                    </nav>
                  </div>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

export default AttendanceLogs;