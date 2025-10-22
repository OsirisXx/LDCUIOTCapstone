import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { 
  ChartBarIcon,
  CalendarDaysIcon,
  ClockIcon,
  UserGroupIcon,
  MagnifyingGlassIcon,
  FunnelIcon,
  DocumentArrowDownIcon,
  EyeIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  XCircleIcon,
  AcademicCapIcon,
  MapPinIcon,
  BuildingOfficeIcon,
  BookOpenIcon,
  ChevronRightIcon,
  ChevronDownIcon,
  PresentationChartBarIcon,
  CalendarIcon,
  UsersIcon
} from '@heroicons/react/24/outline';
import toast from 'react-hot-toast';
import axios from 'axios';

// Set up axios defaults
axios.defaults.baseURL = 'http://localhost:5000';

function Reports() {
  const [reports, setReports] = useState([]);
  const [loading, setLoading] = useState(true);
  const [filters, setFilters] = useState({
    startDate: '',
    endDate: '',
    subjectId: '',
    academicYear: '',
    semester: '',
    status: '',
    search: ''
  });
  const [subjects, setSubjects] = useState([]);
  const [academicYears, setAcademicYears] = useState([]);
  const [semesters, setSemesters] = useState([]);
  const [statistics, setStatistics] = useState(null);
  const [showFilters, setShowFilters] = useState(false);
  const [selectedReport, setSelectedReport] = useState(null);
  const [showDetailModal, setShowDetailModal] = useState(false);
  const [groupedReports, setGroupedReports] = useState({});
  const [groupBy, setGroupBy] = useState('subject'); // 'subject', 'date', 'session'
  const [expandedCards, setExpandedCards] = useState(new Set());
  const [viewMode, setViewMode] = useState('cards'); // 'cards', 'table'
  const [selectedGroup, setSelectedGroup] = useState(null);
  const [showGroupDetails, setShowGroupDetails] = useState(false);
  const [showClearDialog, setShowClearDialog] = useState(false);
  const [clearing, setClearing] = useState(false);

  useEffect(() => {
    fetchInitialData();
  }, []);

  useEffect(() => {
    fetchReports();
  }, [filters]);

  useEffect(() => {
    if (reports.length > 0) {
      groupReports(reports);
    }
  }, [groupBy, reports]);

  const fetchInitialData = async () => {
    try {
      const token = localStorage.getItem('token');
      if (!token) {
        toast.error('Please login to access reports');
        return;
      }

      const headers = { Authorization: `Bearer ${token}` };

      // Fetch subjects, academic years, and semesters in parallel
      const [subjectsRes, academicYearsRes, semestersRes] = await Promise.all([
        axios.get('/api/subjects', { headers }),
        axios.get('/api/logs/attendance/stats', { headers }),
        axios.get('/api/logs/attendance/stats', { headers })
      ]);

      setSubjects(subjectsRes.data.subjects || []);
      
      // Extract unique academic years and semesters from the data
      const uniqueYears = [...new Set((subjectsRes.data.subjects || []).map(s => s.ACADEMICYEAR).filter(Boolean))];
      const uniqueSemesters = [...new Set((subjectsRes.data.subjects || []).map(s => s.SEMESTER).filter(Boolean))];
      
      setAcademicYears(uniqueYears);
      setSemesters(uniqueSemesters);

    } catch (error) {
      console.error('Error fetching initial data:', error);
      toast.error('Failed to load initial data');
    }
  };

  const fetchReports = async () => {
    try {
      setLoading(true);
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      // For now, use the basic endpoint without complex filtering
      const response = await axios.get(`/api/logs/attendance`, { headers });
      let reports = response.data.logs || [];
      
      // Apply client-side filtering for now
      if (filters.search) {
        const searchLower = filters.search.toLowerCase();
        reports = reports.filter(report => 
          report.FIRSTNAME?.toLowerCase().includes(searchLower) ||
          report.LASTNAME?.toLowerCase().includes(searchLower) ||
          report.STUDENTID?.toLowerCase().includes(searchLower) ||
          report.SUBJECTCODE?.toLowerCase().includes(searchLower) ||
          report.SUBJECTNAME?.toLowerCase().includes(searchLower)
        );
      }
      
      if (filters.status) {
        reports = reports.filter(report => report.STATUS === filters.status);
      }
      
      if (filters.startDate) {
        reports = reports.filter(report => {
          const reportDate = new Date(report.SCANDATETIME).toISOString().split('T')[0];
          return reportDate >= filters.startDate;
        });
      }
      
      if (filters.endDate) {
        reports = reports.filter(report => {
          const reportDate = new Date(report.SCANDATETIME).toISOString().split('T')[0];
          return reportDate <= filters.endDate;
        });
      }

      setReports(reports);
      
      // Group reports based on selected grouping
      groupReports(reports);
      
      // Calculate statistics
      const stats = {
        totalStudents: new Set(reports.map(r => r.USERID || r.STUDENTID)).size,
        presentCount: reports.filter(r => r.STATUS === 'Present').length,
        lateCount: reports.filter(r => r.STATUS === 'Late').length,
        absentCount: reports.filter(r => r.STATUS === 'Absent').length
      };
      setStatistics(stats);

    } catch (error) {
      console.error('Error fetching reports:', error);
      if (error.response?.status === 401) {
        toast.error('Session expired. Please login again.');
        localStorage.removeItem('token');
        window.location.href = '/login';
      } else {
        toast.error('Failed to load reports');
      }
    } finally {
      setLoading(false);
    }
  };

  const groupReports = (reports) => {
    const grouped = {};
    
    reports.forEach(report => {
      let key;
      let title;
      let subtitle;
      
      switch (groupBy) {
        case 'subject':
          key = `${report.SUBJECTCODE}-${report.SUBJECTNAME}`;
          title = `${report.SUBJECTCODE} - ${report.SUBJECTNAME}`;
          subtitle = `${report.ACADEMICYEAR} • ${report.SEMESTER}`;
          break;
        case 'date':
          const date = new Date(report.SCANDATETIME).toISOString().split('T')[0];
          key = date;
          title = new Date(date).toLocaleDateString('en-US', { 
            weekday: 'long', 
            year: 'numeric', 
            month: 'long', 
            day: 'numeric' 
          });
          subtitle = `${reports.filter(r => new Date(r.SCANDATETIME).toISOString().split('T')[0] === date).length} records`;
          break;
        case 'session':
          key = `${report.SUBJECTCODE}-${report.DAYOFWEEK}-${report.STARTTIME}`;
          title = `${report.SUBJECTCODE} - ${report.DAYOFWEEK}`;
          subtitle = `${formatTime(report.STARTTIME)} - ${formatTime(report.ENDTIME)} • ${report.ROOMNUMBER}`;
          break;
        default:
          key = 'all';
          title = 'All Reports';
          subtitle = `${reports.length} records`;
      }
      
      if (!grouped[key]) {
        grouped[key] = {
          title,
          subtitle,
          reports: [],
          stats: {
            total: 0,
            present: 0,
            late: 0,
            absent: 0
          }
        };
      }
      
      grouped[key].reports.push(report);
      grouped[key].stats.total++;
      if (report.STATUS === 'Present') grouped[key].stats.present++;
      else if (report.STATUS === 'Late') grouped[key].stats.late++;
      else if (report.STATUS === 'Absent') grouped[key].stats.absent++;
    });
    
    setGroupedReports(grouped);
  };

  const handleFilterChange = (key, value) => {
    setFilters(prev => ({
      ...prev,
      [key]: value
    }));
  };

  const toggleCardExpansion = (cardKey) => {
    setExpandedCards(prev => {
      const newSet = new Set(prev);
      if (newSet.has(cardKey)) {
        newSet.delete(cardKey);
      } else {
        newSet.add(cardKey);
      }
      return newSet;
    });
  };

  const viewGroupDetails = (groupKey, groupData) => {
    setSelectedGroup({ key: groupKey, ...groupData });
    setShowGroupDetails(true);
  };

  const backToMainView = () => {
    setShowGroupDetails(false);
    setSelectedGroup(null);
  };

  const clearGroupAttendanceRecords = async () => {
    try {
      setClearing(true);
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      // Get the attendance IDs for this specific group
      const attendanceIds = selectedGroup.reports.map(report => report.ATTENDANCEID);
      
      console.log('Attempting to clear group records:', {
        attendanceIds,
        count: attendanceIds.length,
        endpoint: '/api/logs/attendance/clear-group'
      });
      
      const response = await axios.delete('/api/logs/attendance/clear-group', { 
        headers,
        data: { attendanceIds }
      });
      
      if (response.data.success) {
        toast.success(`${response.data.affectedRows} attendance records have been cleared successfully!`);
        // Refresh the reports data
        await fetchReports();
        setShowClearDialog(false);
        // Go back to main view since the group is now empty
        backToMainView();
      } else {
        toast.error('Failed to clear attendance records');
      }
    } catch (error) {
      console.error('Error clearing attendance records:', error);
      if (error.response?.status === 401) {
        toast.error('Session expired. Please login again.');
        localStorage.removeItem('token');
        window.location.href = '/login';
      } else {
        toast.error('Failed to clear attendance records. Please try again.');
      }
    } finally {
      setClearing(false);
    }
  };

  const clearFilters = () => {
    setFilters({
      startDate: '',
      endDate: '',
      subjectId: '',
      academicYear: '',
      semester: '',
      status: '',
      search: ''
    });
  };

  const exportReports = async () => {
    try {
      // Export the currently filtered reports
      const exportData = {
        reports: reports,
        statistics: statistics,
        filters: filters,
        exportDate: new Date().toISOString(),
        totalRecords: reports.length
      };

      const dataStr = JSON.stringify(exportData, null, 2);
      const dataBlob = new Blob([dataStr], { type: 'application/json' });
      const url = window.URL.createObjectURL(dataBlob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `attendance-reports-${new Date().toISOString().split('T')[0]}.json`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);

      toast.success('Reports exported successfully!');

    } catch (error) {
      console.error('Error exporting reports:', error);
      toast.error('Failed to export reports');
    }
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
    const date = new Date(dateTime);
    return date.toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const formatTime = (time) => {
    if (!time) return '';
    const [hours, minutes] = time.split(':');
    const hour = parseInt(hours);
    const ampm = hour >= 12 ? 'PM' : 'AM';
    const displayHour = hour % 12 || 12;
    return `${displayHour}:${minutes} ${ampm}`;
  };

  const viewReportDetails = (report) => {
    setSelectedReport(report);
    setShowDetailModal(true);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  // Show detailed group view
  if (showGroupDetails && selectedGroup) {
    return (
      <div className="space-y-6">
        {/* Breadcrumb Navigation */}
        <div className="bg-white rounded-lg shadow border border-gray-200 p-4">
          <nav className="flex items-center space-x-2 text-sm">
            <button
              onClick={backToMainView}
              className="text-blue-600 hover:text-blue-800 flex items-center"
            >
              <ChevronRightIcon className="h-4 w-4 mr-1 rotate-180" />
              Back to Reports
            </button>
            <span className="text-gray-400">/</span>
            <span className="text-gray-900 font-medium">{selectedGroup.title}</span>
          </nav>
        </div>

        {/* Group Header */}
        <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
          <div className="flex items-start justify-between">
            <div>
              <h1 className="text-2xl font-bold text-gray-900">{selectedGroup.title}</h1>
              <p className="text-gray-600 mt-1">{selectedGroup.subtitle}</p>
            </div>
            <div className="flex space-x-4">
              <button
                onClick={() => exportReports()}
                className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700"
              >
                <DocumentArrowDownIcon className="h-4 w-4 mr-2" />
                Export Group
              </button>
              <button
                onClick={() => setShowClearDialog(true)}
                className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-red-600 hover:bg-red-700"
              >
                <XCircleIcon className="h-4 w-4 mr-2" />
                Clear Group Records
              </button>
            </div>
          </div>
        </div>

        {/* Group Statistics */}
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <CheckCircleIcon className="h-8 w-8 text-green-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Present</p>
                <p className="text-2xl font-semibold text-gray-900">{selectedGroup.stats.present}</p>
              </div>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <ExclamationTriangleIcon className="h-8 w-8 text-yellow-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Late</p>
                <p className="text-2xl font-semibold text-gray-900">{selectedGroup.stats.late}</p>
              </div>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <XCircleIcon className="h-8 w-8 text-red-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Absent</p>
                <p className="text-2xl font-semibold text-gray-900">{selectedGroup.stats.absent}</p>
              </div>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <UserGroupIcon className="h-8 w-8 text-blue-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Total</p>
                <p className="text-2xl font-semibold text-gray-900">{selectedGroup.stats.total}</p>
              </div>
            </div>
          </div>
        </div>

        {/* Detailed Attendance Records */}
        <div className="bg-white rounded-lg shadow border border-gray-200">
          <div className="px-6 py-4 border-b border-gray-200">
            <h3 className="text-lg font-medium text-gray-900">Attendance Records</h3>
            <p className="text-sm text-gray-500">
              {selectedGroup.reports.length} record{selectedGroup.reports.length !== 1 ? 's' : ''} found
            </p>
          </div>
          
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Student
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Date & Time
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Method
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Location
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {selectedGroup.reports.map((report) => (
                  <tr key={report.ATTENDANCEID} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <div className="flex-shrink-0 h-10 w-10">
                          <div className="h-10 w-10 rounded-full bg-blue-100 flex items-center justify-center">
                            <AcademicCapIcon className="h-5 w-5 text-blue-600" />
                          </div>
                        </div>
                        <div className="ml-4">
                          <div className="text-sm font-medium text-gray-900">
                            {report.FIRSTNAME} {report.LASTNAME}
                          </div>
                          <div className="text-sm text-gray-500">
                            ID: {report.STUDENTID || 'N/A'}
                          </div>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm text-gray-900">
                        {formatDateTime(report.SCANDATETIME)}
                      </div>
                      <div className="text-sm text-gray-500">
                        {report.AUTHMETHOD || 'Fingerprint'}
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        {getStatusIcon(report.STATUS)}
                        <span className={`ml-2 inline-flex px-2 py-1 text-xs font-semibold rounded-full ${getStatusColor(report.STATUS)}`}>
                          {report.STATUS || 'Unknown'}
                        </span>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm text-gray-900">
                        {report.AUTHMETHOD || 'Fingerprint'}
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center text-sm text-gray-900">
                        <MapPinIcon className="h-4 w-4 mr-2 text-gray-400" />
                        <div>
                          <div>{report.ROOMNUMBER || 'N/A'}</div>
                          <div className="text-xs text-gray-500">{report.ROOMNAME || ''}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                      <button
                        onClick={() => viewReportDetails(report)}
                        className="text-blue-600 hover:text-blue-900 flex items-center"
                      >
                        <EyeIcon className="h-4 w-4 mr-1" />
                        View Details
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Clear Group Records Confirmation Dialog */}
        {showClearDialog && createPortal(
          <div 
            className="fixed bg-gray-600 bg-opacity-50 overflow-y-auto z-[60]"
            style={{
              top: 0,
              left: 0,
              right: 0,
              bottom: 0,
              width: '100vw',
              height: '100vh',
              zIndex: 60
            }}
          >
            <div className="relative top-20 mx-auto p-5 border w-96 shadow-lg rounded-md bg-white">
              <div className="mt-3 text-center">
                <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-red-100">
                  <XCircleIcon className="h-6 w-6 text-red-600" />
                </div>
                <h3 className="text-lg font-medium text-gray-900 mt-4">Clear Group Attendance Records</h3>
                <div className="mt-2 px-7 py-3">
                  <p className="text-sm text-gray-500">
                    This action will permanently delete {selectedGroup.reports.length} attendance record{selectedGroup.reports.length !== 1 ? 's' : ''} for <strong>{selectedGroup.title}</strong>.
                  </p>
                  <p className="text-sm text-red-600 font-medium mt-2">
                    This action cannot be undone. Are you sure you want to proceed?
                  </p>
                </div>
                <div className="flex justify-center space-x-4 mt-4">
                  <button
                    onClick={() => setShowClearDialog(false)}
                    className="px-4 py-2 bg-gray-300 text-gray-800 rounded-md hover:bg-gray-400 text-sm font-medium"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={clearGroupAttendanceRecords}
                    disabled={clearing}
                    className="px-4 py-2 bg-red-600 text-white rounded-md hover:bg-red-700 text-sm font-medium disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {clearing ? 'Clearing...' : 'Yes, Clear Group Records'}
                  </button>
                </div>
              </div>
            </div>
          </div>,
          document.body
        )}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Attendance Reports</h1>
          <p className="text-gray-600">View attendance logs for each class schedule with comprehensive filtering</p>
        </div>
        <div className="flex space-x-3">
          <button
            onClick={() => setShowFilters(!showFilters)}
            className="inline-flex items-center px-4 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
          >
            <FunnelIcon className="h-4 w-4 mr-2" />
            {showFilters ? 'Hide Filters' : 'Show Filters'}
          </button>
          <button
            onClick={exportReports}
            className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700"
          >
            <DocumentArrowDownIcon className="h-4 w-4 mr-2" />
            Export Reports
          </button>
        </div>
      </div>

      {/* Statistics Cards */}
      {statistics && (
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <UserGroupIcon className="h-8 w-8 text-blue-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Total Students</p>
                <p className="text-2xl font-semibold text-gray-900">{statistics.totalStudents || 0}</p>
              </div>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <CheckCircleIcon className="h-8 w-8 text-green-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Present</p>
                <p className="text-2xl font-semibold text-gray-900">{statistics.presentCount || 0}</p>
              </div>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <ExclamationTriangleIcon className="h-8 w-8 text-yellow-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Late</p>
                <p className="text-2xl font-semibold text-gray-900">{statistics.lateCount || 0}</p>
              </div>
            </div>
          </div>
          <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <XCircleIcon className="h-8 w-8 text-red-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Absent</p>
                <p className="text-2xl font-semibold text-gray-900">{statistics.absentCount || 0}</p>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Filters */}
      {showFilters && (
        <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
          <h3 className="text-lg font-medium text-gray-900 mb-4">Filter Reports</h3>
          <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-4">
            {/* Date Range */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Start Date</label>
              <input
                type="date"
                value={filters.startDate}
                onChange={(e) => handleFilterChange('startDate', e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">End Date</label>
              <input
                type="date"
                value={filters.endDate}
                onChange={(e) => handleFilterChange('endDate', e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
              />
            </div>

            {/* Subject Filter */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Subject</label>
              <select
                value={filters.subjectId}
                onChange={(e) => handleFilterChange('subjectId', e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
              >
                <option value="">All Subjects</option>
                {subjects.map(subject => (
                  <option key={subject.SUBJECTID} value={subject.SUBJECTID}>
                    {subject.SUBJECTCODE} - {subject.SUBJECTNAME}
                  </option>
                ))}
              </select>
            </div>

            {/* Academic Year Filter */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Academic Year</label>
              <select
                value={filters.academicYear}
                onChange={(e) => handleFilterChange('academicYear', e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
              >
                <option value="">All Years</option>
                {academicYears.map(year => (
                  <option key={year} value={year}>{year}</option>
                ))}
              </select>
            </div>

            {/* Semester Filter */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Semester</label>
              <select
                value={filters.semester}
                onChange={(e) => handleFilterChange('semester', e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
              >
                <option value="">All Semesters</option>
                {semesters.map(semester => (
                  <option key={semester} value={semester}>{semester}</option>
                ))}
              </select>
            </div>

            {/* Status Filter */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Status</label>
              <select
                value={filters.status}
                onChange={(e) => handleFilterChange('status', e.target.value)}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
              >
                <option value="">All Status</option>
                <option value="Present">Present</option>
                <option value="Late">Late</option>
                <option value="Absent">Absent</option>
              </select>
            </div>

            {/* Search */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Search</label>
              <div className="relative">
                <MagnifyingGlassIcon className="h-5 w-5 absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
                <input
                  type="text"
                  placeholder="Search by name, student ID..."
                  value={filters.search}
                  onChange={(e) => handleFilterChange('search', e.target.value)}
                  className="w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md text-sm"
                />
              </div>
            </div>

            {/* Clear Filters */}
            <div className="flex items-end">
              <button
                onClick={clearFilters}
                className="w-full px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
              >
                Clear Filters
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Grouping and View Controls */}
      <div className="bg-white rounded-lg shadow border border-gray-200 p-6">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h3 className="text-lg font-medium text-gray-900">Attendance Reports</h3>
            <p className="text-sm text-gray-500">
              {reports.length} report{reports.length !== 1 ? 's' : ''} found • {Object.keys(groupedReports).length} group{Object.keys(groupedReports).length !== 1 ? 's' : ''}
            </p>
          </div>
          
          <div className="flex flex-col sm:flex-row gap-4">
            {/* Group By Selector */}
            <div className="flex items-center space-x-2">
              <label className="text-sm font-medium text-gray-700">Group by:</label>
              <select
                value={groupBy}
                onChange={(e) => setGroupBy(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-1 text-sm"
              >
                <option value="subject">Subject</option>
                <option value="date">Date</option>
                <option value="session">Session</option>
              </select>
            </div>

            {/* View Mode Toggle */}
            <div className="flex items-center space-x-2">
              <label className="text-sm font-medium text-gray-700">View:</label>
              <div className="flex border border-gray-300 rounded-md">
                <button
                  onClick={() => setViewMode('cards')}
                  className={`px-3 py-1 text-sm ${viewMode === 'cards' ? 'bg-blue-600 text-white' : 'bg-white text-gray-700'}`}
                >
                  Cards
                </button>
                <button
                  onClick={() => setViewMode('table')}
                  className={`px-3 py-1 text-sm ${viewMode === 'table' ? 'bg-blue-600 text-white' : 'bg-white text-gray-700'}`}
                >
                  Table
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Reports Content */}
      {reports.length === 0 ? (
        <div className="text-center py-12 bg-white rounded-lg shadow border border-gray-200">
          <ChartBarIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">No reports found</h3>
          <p className="mt-1 text-sm text-gray-500">
            Try adjusting your filters or check if there are any attendance records.
          </p>
        </div>
      ) : viewMode === 'cards' ? (
        /* Card View */
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {Object.entries(groupedReports).map(([key, group]) => (
            <div key={key} className="bg-white rounded-lg shadow border border-gray-200 overflow-hidden">
              <div className="p-6">
                <div className="flex items-start justify-between">
                  <div className="flex-1">
                    <h3 className="text-lg font-semibold text-gray-900 mb-1">{group.title}</h3>
                    <p className="text-sm text-gray-600 mb-4">{group.subtitle}</p>
                    
                    {/* Statistics */}
                    <div className="grid grid-cols-2 gap-4 mb-4">
                      <div className="text-center">
                        <div className="text-2xl font-bold text-green-600">{group.stats.present}</div>
                        <div className="text-xs text-gray-500">Present</div>
                      </div>
                      <div className="text-center">
                        <div className="text-2xl font-bold text-yellow-600">{group.stats.late}</div>
                        <div className="text-xs text-gray-500">Late</div>
                      </div>
                      <div className="text-center">
                        <div className="text-2xl font-bold text-red-600">{group.stats.absent}</div>
                        <div className="text-xs text-gray-500">Absent</div>
                      </div>
                      <div className="text-center">
                        <div className="text-2xl font-bold text-blue-600">{group.stats.total}</div>
                        <div className="text-xs text-gray-500">Total</div>
                      </div>
                    </div>
                  </div>
                  
                  <button
                    onClick={() => viewGroupDetails(key, group)}
                    className="ml-4 p-2 text-gray-400 hover:text-gray-600 rounded-md hover:bg-gray-100"
                  >
                    <EyeIcon className="h-5 w-5" />
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>
      ) : (
        /* Table View */
        <div className="bg-white rounded-lg shadow border border-gray-200">
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Student
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Subject & Schedule
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Date & Time
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Location
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {reports.map((report) => (
                  <tr key={report.ATTENDANCEID} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <div className="flex-shrink-0 h-10 w-10">
                          <div className="h-10 w-10 rounded-full bg-blue-100 flex items-center justify-center">
                            <AcademicCapIcon className="h-5 w-5 text-blue-600" />
                          </div>
                        </div>
                        <div className="ml-4">
                          <div className="text-sm font-medium text-gray-900">
                            {report.FIRSTNAME} {report.LASTNAME}
                          </div>
                          <div className="text-sm text-gray-500">
                            ID: {report.STUDENTID || 'N/A'}
                          </div>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4">
                      <div className="text-sm text-gray-900">
                        <div className="font-medium">{report.SUBJECTCODE}</div>
                        <div className="text-gray-600">{report.SUBJECTNAME}</div>
                        <div className="text-xs text-gray-500 mt-1">
                          {report.DAYOFWEEK} • {formatTime(report.STARTTIME)} - {formatTime(report.ENDTIME)}
                        </div>
                        <div className="text-xs text-gray-500">
                          {report.ACADEMICYEAR} • {report.SEMESTER}
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="text-sm text-gray-900">
                        {formatDateTime(report.SCANDATETIME)}
                      </div>
                      <div className="text-sm text-gray-500">
                        {report.AUTHMETHOD || 'Fingerprint'}
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        {getStatusIcon(report.STATUS)}
                        <span className={`ml-2 inline-flex px-2 py-1 text-xs font-semibold rounded-full ${getStatusColor(report.STATUS)}`}>
                          {report.STATUS || 'Unknown'}
                        </span>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center text-sm text-gray-900">
                        <MapPinIcon className="h-4 w-4 mr-2 text-gray-400" />
                        <div>
                          <div>{report.ROOMNUMBER || 'N/A'}</div>
                          <div className="text-xs text-gray-500">{report.ROOMNAME || ''}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                      <button
                        onClick={() => viewReportDetails(report)}
                        className="text-blue-600 hover:text-blue-900 flex items-center"
                      >
                        <EyeIcon className="h-4 w-4 mr-1" />
                        View Details
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Report Details Modal */}
      {showDetailModal && selectedReport && createPortal(
        <div 
          className="fixed bg-gray-600 bg-opacity-50 overflow-y-auto z-[60]"
          style={{
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            width: '100vw',
            height: '100vh',
            zIndex: 60
          }}
        >
          <div className="relative top-20 mx-auto p-5 border w-11/12 max-w-4xl shadow-lg rounded-md bg-white">
            <div className="mt-3">
              <div className="flex justify-between items-center mb-6">
                <h3 className="text-lg font-semibold text-gray-900">Report Details</h3>
                <button
                  onClick={() => setShowDetailModal(false)}
                  className="text-gray-400 hover:text-gray-600"
                >
                  <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                {/* Student Information */}
                <div className="bg-gray-50 rounded-lg p-4">
                  <h4 className="text-lg font-medium text-gray-900 mb-3">Student Information</h4>
                  <div className="space-y-2">
                    <div>
                      <span className="text-sm font-medium text-gray-500">Name:</span>
                      <span className="ml-2 text-sm text-gray-900">
                        {selectedReport.FIRSTNAME} {selectedReport.LASTNAME}
                      </span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Student ID:</span>
                      <span className="ml-2 text-sm text-gray-900">{selectedReport.STUDENTID || 'N/A'}</span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Status:</span>
                      <span className={`ml-2 inline-flex px-2 py-1 text-xs font-semibold rounded-full ${getStatusColor(selectedReport.STATUS)}`}>
                        {selectedReport.STATUS || 'Unknown'}
                      </span>
                    </div>
                  </div>
                </div>

                {/* Schedule Information */}
                <div className="bg-gray-50 rounded-lg p-4">
                  <h4 className="text-lg font-medium text-gray-900 mb-3">Schedule Information</h4>
                  <div className="space-y-2">
                    <div>
                      <span className="text-sm font-medium text-gray-500">Subject:</span>
                      <span className="ml-2 text-sm text-gray-900">
                        {selectedReport.SUBJECTCODE} - {selectedReport.SUBJECTNAME}
                      </span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Day:</span>
                      <span className="ml-2 text-sm text-gray-900">{selectedReport.DAYOFWEEK}</span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Time:</span>
                      <span className="ml-2 text-sm text-gray-900">
                        {formatTime(selectedReport.STARTTIME)} - {formatTime(selectedReport.ENDTIME)}
                      </span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Room:</span>
                      <span className="ml-2 text-sm text-gray-900">
                        {selectedReport.ROOMNUMBER} - {selectedReport.ROOMNAME}
                      </span>
                    </div>
                  </div>
                </div>

                {/* Attendance Details */}
                <div className="bg-gray-50 rounded-lg p-4">
                  <h4 className="text-lg font-medium text-gray-900 mb-3">Attendance Details</h4>
                  <div className="space-y-2">
                    <div>
                      <span className="text-sm font-medium text-gray-500">Scan Time:</span>
                      <span className="ml-2 text-sm text-gray-900">
                        {formatDateTime(selectedReport.SCANDATETIME)}
                      </span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Authentication Method:</span>
                      <span className="ml-2 text-sm text-gray-900">{selectedReport.AUTHMETHOD || 'Fingerprint'}</span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Location:</span>
                      <span className="ml-2 text-sm text-gray-900">{selectedReport.LOCATION || 'Unknown'}</span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Academic Period:</span>
                      <span className="ml-2 text-sm text-gray-900">
                        {selectedReport.ACADEMICYEAR} • {selectedReport.SEMESTER}
                      </span>
                    </div>
                  </div>
                </div>

                {/* Additional Information */}
                <div className="bg-gray-50 rounded-lg p-4">
                  <h4 className="text-lg font-medium text-gray-900 mb-3">Additional Information</h4>
                  <div className="space-y-2">
                    <div>
                      <span className="text-sm font-medium text-gray-500">Attendance ID:</span>
                      <span className="ml-2 text-sm text-gray-900 font-mono text-xs">
                        {selectedReport.ATTENDANCEID}
                      </span>
                    </div>
                    <div>
                      <span className="text-sm font-medium text-gray-500">Schedule ID:</span>
                      <span className="ml-2 text-sm text-gray-900 font-mono text-xs">
                        {selectedReport.SCHEDULEID || 'N/A'}
                      </span>
                    </div>
                  </div>
                </div>
              </div>

              <div className="mt-6 flex justify-end">
                <button
                  onClick={() => setShowDetailModal(false)}
                  className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>,
        document.body
      )}

    </div>
  );
}

export default Reports;
