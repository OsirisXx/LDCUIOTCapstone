import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import {
  BookOpenIcon,
  BuildingOfficeIcon,
  CalendarDaysIcon,
  UsersIcon,
  ClipboardDocumentListIcon,
  ExclamationTriangleIcon,
  XMarkIcon,
  DocumentArrowDownIcon,
  DocumentArrowUpIcon,
} from '@heroicons/react/24/outline';
import axios from 'axios';
import toast from 'react-hot-toast';

axios.defaults.baseURL = 'http://localhost:5000';

function Archive() {
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('dashboard');
  const [archiveStats, setArchiveStats] = useState([]);
  const [academicYears, setAcademicYears] = useState([]);
  const [semesters, setSemesters] = useState([]);
  
  // Archive operation states
  const [showArchiveModal, setShowArchiveModal] = useState(false);
  const [archiveCategory, setArchiveCategory] = useState('');
  const [archiveAcademicYear, setArchiveAcademicYear] = useState('');
  const [archiveSemester, setArchiveSemester] = useState('');
  const [archiveReason, setArchiveReason] = useState('');
  const [archiving, setArchiving] = useState(false);
  const [unarchiving, setUnarchiving] = useState(false);
  
  // Unarchive confirmation modal states
  const [showUnarchiveModal, setShowUnarchiveModal] = useState(false);
  const [unarchiveModalAnimation, setUnarchiveModalAnimation] = useState('hidden');
  const [showSecondConfirmation, setShowSecondConfirmation] = useState(false);
  const [pendingUnarchiveKey, setPendingUnarchiveKey] = useState(null);
  const [pendingUnarchiveDate, setPendingUnarchiveDate] = useState(null);
  const [pendingUnarchiveCount, setPendingUnarchiveCount] = useState(0);
  const [selectedRoomIds, setSelectedRoomIds] = useState([]);
  const [selectedUserIds, setSelectedUserIds] = useState([]);
  const [selectedBackupFiles, setSelectedBackupFiles] = useState([]);
  const [availableStudents, setAvailableStudents] = useState([]);
  const [loadingStudents, setLoadingStudents] = useState(false);
  const [selectedUserType, setSelectedUserType] = useState('all');
  const [availableRooms, setAvailableRooms] = useState([]);
  const [loadingRooms, setLoadingRooms] = useState(false);
  const [availableBackups, setAvailableBackups] = useState([]);
  const [loadingBackups, setLoadingBackups] = useState(false);
  
  // View archived data states
  const [archivedSubjects, setArchivedSubjects] = useState([]);
  const [archivedRooms, setArchivedRooms] = useState([]);
  const [archivedSchedules, setArchivedSchedules] = useState([]);
  const [archivedUsers, setArchivedUsers] = useState([]);
  const [archivedAttendance, setArchivedAttendance] = useState([]);
  const [archivedBackups, setArchivedBackups] = useState([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [expandedGroups, setExpandedGroups] = useState({});
  

  useEffect(() => {
    fetchDashboardStats();
    fetchAcademicYears();
  }, []);

  useEffect(() => {
    if (activeTab !== 'dashboard') {
      fetchArchivedData(activeTab);
    }
  }, [activeTab, currentPage]);

  const fetchDashboardStats = async () => {
    try {
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/archive/dashboard', {
        headers: { Authorization: `Bearer ${token}` }
      });
      setArchiveStats(response.data.stats);
    } catch (error) {
      console.error('Error fetching archive stats:', error);
    } finally {
      setLoading(false);
    }
  };

  const fetchAcademicYears = async () => {
    try {
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/unified/data', {
        headers: { Authorization: `Bearer ${token}` }
      });
      
      // Derive from subjects
      const subjectYears = Array.isArray(response?.data?.subjects?.data)
        ? response.data.subjects.data.map(s => s.ACADEMICYEAR).filter(Boolean)
        : [];
      const subjectSemesters = Array.isArray(response?.data?.subjects?.data)
        ? response.data.subjects.data.map(s => s.SEMESTER).filter(Boolean)
        : [];

      // Also derive from attendance logs so the Attendance archive modal isn't empty
      let attendanceYears = [];
      let attendanceSemesters = [];
      try {
        const logsRes = await axios.get('/api/logs/attendance', {
          headers: { Authorization: `Bearer ${token}` }
        });
        const logs = Array.isArray(logsRes?.data?.logs) ? logsRes.data.logs : [];
        attendanceYears = logs.map(l => l.ACADEMICYEAR).filter(Boolean);
        attendanceSemesters = logs.map(l => l.SEMESTER).filter(Boolean);
      } catch (e) {
        // Best-effort: if attendance logs fail, proceed with subjects-only
        console.warn('Could not enrich academic years from attendance logs:', e?.message || e);
      }

      const uniqueYears = [...new Set([...
        subjectYears,
        ...attendanceYears
      ])];
      const uniqueSemesters = [...new Set([...
        subjectSemesters,
        ...attendanceSemesters
      ])];
      
      setAcademicYears(uniqueYears);
      setSemesters(uniqueSemesters);
    } catch (error) {
      console.error('Error fetching academic years:', error);
    }
  };

  const fetchArchivedData = async (category) => {
    try {
      setLoading(true);
      const token = localStorage.getItem('token');
      const params = { page: currentPage, limit: 20 };
      
      if (archiveAcademicYear) params.academic_year = archiveAcademicYear;
      if (archiveSemester) params.semester = archiveSemester;

      let response;
      switch (category) {
        case 'subjects':
          response = await axios.get('/api/archive/subjects', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedSubjects(response.data.subjects);
          break;
        case 'rooms':
          response = await axios.get('/api/archive/rooms', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedRooms(response.data.rooms);
          break;
        case 'schedules':
          response = await axios.get('/api/archive/schedules', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedSchedules(response.data.schedules);
          break;
        case 'users':
          response = await axios.get('/api/archive/users', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedUsers(response.data.users);
          break;
        case 'attendance':
          response = await axios.get('/api/archive/attendance', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedAttendance(response.data.records);
          break;
        case 'backups':
          response = await axios.get('/api/archive/backups', { 
            headers: { Authorization: `Bearer ${token}` }
          });
          setArchivedBackups(response.data.backups);
          break;
      }
      
      if (response.data.pagination) setTotalPages(response.data.pagination.pages);
    } catch (error) {
      console.error('Error fetching archived data:', error);
      toast.error('Failed to load archived data');
    } finally {
      setLoading(false);
    }
  };

  const handleArchiveClick = async (category) => {
    setArchiveCategory(category);
    setShowArchiveModal(true);
    
    // Fetch data based on category
    if (category === 'users') {
      setSelectedUserType('all'); // Reset to 'all' when opening modal
      await fetchUsers('all');
    } else if (category === 'rooms') {
      await fetchRooms();
    } else if (category === 'backups') {
      await fetchBackupsForArchiving();
    }
  };

  const fetchBackupsForArchiving = async () => {
    try {
      setLoadingBackups(true);
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/backup/list', {
        headers: { Authorization: `Bearer ${token}` }
      });
      setAvailableBackups(response.data.backups || []);
    } catch (error) {
      console.error('Error fetching backups:', error);
      toast.error('Failed to load backups');
    } finally {
      setLoadingBackups(false);
    }
  };

  const fetchUsers = async (userType = selectedUserType) => {
    try {
      setLoadingStudents(true);
      const token = localStorage.getItem('token');
      const url = userType === 'all' 
        ? '/api/users?limit=1000'
        : `/api/users?type=${userType}&limit=1000`;
      const response = await axios.get(url, {
        headers: { Authorization: `Bearer ${token}` }
      });
      setAvailableStudents(response.data.users || []);
    } catch (error) {
      console.error('Error fetching users:', error);
      toast.error('Failed to load users');
    } finally {
      setLoadingStudents(false);
    }
  };

  const handleUserTypeChange = async (newType) => {
    setSelectedUserType(newType);
    setSelectedUserIds([]); // Clear selections when changing type
    await fetchUsers(newType);
  };

  const fetchRooms = async () => {
    try {
      setLoadingRooms(true);
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/rooms?limit=1000', {
        headers: { Authorization: `Bearer ${token}` }
      });
      setAvailableRooms(response.data.rooms || []);
    } catch (error) {
      console.error('Error fetching rooms:', error);
      toast.error('Failed to load rooms');
    } finally {
      setLoadingRooms(false);
    }
  };

  const toggleStudentSelection = (userId) => {
    setSelectedUserIds(prev => {
      if (prev.includes(userId)) {
        return prev.filter(id => id !== userId);
      } else {
        return [...prev, userId];
      }
    });
  };

  const toggleRoomSelection = (roomId) => {
    setSelectedRoomIds(prev => {
      if (prev.includes(roomId)) {
        return prev.filter(id => id !== roomId);
      } else {
        return [...prev, roomId];
      }
    });
  };

  const selectAllStudents = () => {
    if (selectedUserIds.length === availableStudents.length) {
      setSelectedUserIds([]);
    } else {
      setSelectedUserIds(availableStudents.map(s => s.USERID));
    }
  };

  const selectAllRooms = () => {
    if (selectedRoomIds.length === availableRooms.length) {
      setSelectedRoomIds([]);
    } else {
      setSelectedRoomIds(availableRooms.map(r => r.ROOMID));
    }
  };

  const selectAllBackups = () => {
    if (selectedBackupFiles.length === availableBackups.length) {
      setSelectedBackupFiles([]);
    } else {
      setSelectedBackupFiles(availableBackups.map(b => b.filename));
    }
  };

  const handleArchive = async () => {
    try {
      setArchiving(true);
      const token = localStorage.getItem('token');
      
      let payload = { reason: archiveReason || null };
      
      switch (archiveCategory) {
        case 'subjects':
        case 'schedules':
        case 'attendance':
          if (!archiveAcademicYear || !archiveSemester) {
            toast.error('Please select academic year and semester');
            return;
          }
          payload.academic_year = archiveAcademicYear;
          payload.semester = archiveSemester;
          break;
        case 'rooms':
          if (selectedRoomIds.length === 0) {
            toast.error('Please select at least one room to archive');
            return;
          }
          payload.room_ids = selectedRoomIds;
          break;
        case 'users':
          if (selectedUserIds.length === 0) {
            const userTypeLabel = selectedUserType === 'all' ? 'user' : 
              selectedUserType === 'student' ? 'student' :
              selectedUserType === 'instructor' ? 'instructor' :
              selectedUserType === 'custodian' ? 'custodian' :
              selectedUserType === 'dean' ? 'dean' : 'user';
            toast.error(`Please select at least one ${userTypeLabel} to archive`);
            return;
          }
          payload.user_ids = selectedUserIds;
          break;
        case 'backups':
          if (selectedBackupFiles.length === 0) {
            toast.error('Please select at least one backup to archive');
            return;
          }
          payload = { filenames: selectedBackupFiles };
          break;
      }

      const endpoint = archiveCategory === 'backups' ? '/api/archive/backups' : `/api/archive/${archiveCategory}`;
      const response = await axios.post(endpoint, payload, {
        headers: { Authorization: `Bearer ${token}` }
      });

      toast.success(response.data.message);
      setShowArchiveModal(false);
      setArchiveAcademicYear('');
      setArchiveSemester('');
      setArchiveReason('');
      setSelectedRoomIds([]);
      setSelectedUserIds([]);
      setAvailableStudents([]);
      setAvailableRooms([]);
      setSelectedUserType('all');
      fetchDashboardStats();
      
      if (activeTab === archiveCategory) {
        fetchArchivedData(archiveCategory);
      }
    } catch (error) {
      console.error('Archive error:', error);
      toast.error(error.response?.data?.message || 'Failed to archive data');
    } finally {
      setArchiving(false);
    }
  };

  const handleUnarchiveClick = (groupKey, groupDate, itemCount) => {
    setPendingUnarchiveKey(groupKey);
    setPendingUnarchiveDate(groupDate);
    setPendingUnarchiveCount(itemCount);
    setShowSecondConfirmation(false);
    setShowUnarchiveModal(true);
    setTimeout(() => setUnarchiveModalAnimation('visible'), 10);
  };

  const handleUnarchiveConfirm = async () => {
    if (!showSecondConfirmation) {
      setShowSecondConfirmation(true);
      return;
    }

    try {
      setUnarchiving(true);
      const token = localStorage.getItem('token');
      
      const response = await axios.put('/api/archive/attendance/unarchive', 
        { archived_at: pendingUnarchiveKey },
        { headers: { Authorization: `Bearer ${token}` } }
      );
      
      toast.success(response.data.message);
      fetchDashboardStats();
      fetchArchivedData('attendance');
      
      // Close modal
      setUnarchiveModalAnimation('hidden');
      setTimeout(() => {
        setShowUnarchiveModal(false);
        setShowSecondConfirmation(false);
        setPendingUnarchiveKey(null);
        setPendingUnarchiveDate(null);
        setPendingUnarchiveCount(0);
      }, 300);
    } catch (error) {
      console.error('Unarchive error:', error);
      toast.error(error.response?.data?.message || 'Failed to unarchive records');
    } finally {
      setUnarchiving(false);
    }
  };

  const handleUnarchiveCancel = () => {
    setUnarchiveModalAnimation('hidden');
    setTimeout(() => {
      setShowUnarchiveModal(false);
      setShowSecondConfirmation(false);
      setPendingUnarchiveKey(null);
      setPendingUnarchiveDate(null);
      setPendingUnarchiveCount(0);
    }, 300);
  };

  const getCategoryStats = (category) => {
    if (!Array.isArray(archiveStats)) return 0;
    const stat = archiveStats.find(s => s.category === category);
    return stat ? stat.count : 0;
  };

  const formatDate = (dateString) => {
    if (!dateString) return 'N/A';
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const getGroupKey = (category, item) => {
    const raw = category === 'backups' ? item?.date : item?.ARCHIVED_AT;
    if (!raw) return 'unknown';
    const ms = new Date(raw).getTime();
    if (Number.isNaN(ms)) return 'unknown';
    // Group to seconds using local interpretation; use numeric key
    return Math.floor(ms / 1000).toString();
  };

  const groupArchivedItems = (category, items) => {
    const groups = {};
    (items || []).forEach((item) => {
      const key = getGroupKey(category, item);
      if (!groups[key]) groups[key] = { items: [], displayDate: null };
      groups[key].items.push(item);
      const rawDate = category === 'backups' ? item?.date : item?.ARCHIVED_AT;
      if (!groups[key].displayDate) groups[key].displayDate = rawDate;
    });
    return Object.entries(groups)
      .sort((a, b) => parseInt(b[0], 10) - parseInt(a[0], 10))
      .map(([key, data]) => ({ key, date: data.displayDate, items: data.items }));
  };

  const toggleGroup = (category, key) => {
    setExpandedGroups(prev => {
      const cat = prev[category] || {};
      const next = { ...cat, [key]: !cat[key] };
      return { ...prev, [category]: next };
    });
  };

  const isGroupExpanded = (category, key) => {
    return !!expandedGroups?.[category]?.[key];
  };

  if (loading && activeTab === 'dashboard') {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Archive Management</h1>
          <p className="text-gray-600">Archive and view historical data from your system</p>
        </div>
      </div>

      {/* Archive Statistics */}
      <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-4">
        <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <BookOpenIcon className="h-8 w-8 text-blue-500" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Archived Subjects</p>
              <p className="text-2xl font-semibold text-gray-900">{getCategoryStats('subjects')}</p>
            </div>
          </div>
        </div>
        <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <BuildingOfficeIcon className="h-8 w-8 text-green-500" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Archived Rooms</p>
              <p className="text-2xl font-semibold text-gray-900">{getCategoryStats('rooms')}</p>
            </div>
          </div>
        </div>
        <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <CalendarDaysIcon className="h-8 w-8 text-purple-500" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Archived Schedules</p>
              <p className="text-2xl font-semibold text-gray-900">{getCategoryStats('schedules')}</p>
            </div>
          </div>
        </div>
        <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <UsersIcon className="h-8 w-8 text-yellow-500" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Archived Users</p>
              <p className="text-2xl font-semibold text-gray-900">{getCategoryStats('users')}</p>
            </div>
          </div>
        </div>
        <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <ClipboardDocumentListIcon className="h-8 w-8 text-red-500" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Archived Attendance</p>
              <p className="text-2xl font-semibold text-gray-900">{getCategoryStats('attendance')}</p>
            </div>
          </div>
        </div>
        <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <DocumentArrowDownIcon className="h-8 w-8 text-indigo-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Archived Backups</p>
                <p className="text-2xl font-semibold text-gray-900">{getCategoryStats('backups')}</p>
              </div>
            </div>
        </div>
      </div>

      {/* Tabs for Archive Actions and View Archived Data */}
      <div className="bg-white rounded-lg shadow border border-gray-200">
        <div className="border-b border-gray-200">
          <nav className="flex space-x-8 px-6" aria-label="Tabs">
            <button
              onClick={() => setActiveTab('dashboard')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'dashboard'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archive Actions
            </button>
            <button
              onClick={() => setActiveTab('subjects')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'subjects'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archived Subjects
            </button>
            <button
              onClick={() => setActiveTab('rooms')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'rooms'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archived Rooms
            </button>
            <button
              onClick={() => setActiveTab('schedules')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'schedules'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archived Schedules
            </button>
            <button
              onClick={() => setActiveTab('users')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'users'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archived Users
            </button>
            <button
              onClick={() => setActiveTab('attendance')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'attendance'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archived Attendance
            </button>
            <button
              onClick={() => setActiveTab('backups')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'backups'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archived Backups
            </button>
          </nav>
        </div>

        <div className="p-6">
          {activeTab === 'dashboard' && (
            <div className="space-y-6">
              <div>
                <h2 className="text-lg font-semibold text-gray-900 mb-4">Archive Data</h2>
                <p className="text-sm text-gray-600 mb-6">
                  Select a category below to archive data. Archived data will be moved to the archive tables and removed from active records.
                </p>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {/* Archive Subjects */}
                <div className="bg-yellow-50 border-2 border-yellow-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <BookOpenIcon className="h-8 w-8 text-yellow-600" />
                    <span className="text-sm font-medium text-yellow-800">Subjects</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive subjects by academic year and semester. Related schedules and enrollments will also be archived.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('subjects')}
                    className="w-full px-4 py-2 bg-yellow-600 text-white rounded-md hover:bg-yellow-700 transition-colors"
                  >
                    Archive Subjects
                  </button>
                </div>

                {/* Archive Rooms */}
                <div className="bg-green-50 border-2 border-green-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <BuildingOfficeIcon className="h-8 w-8 text-green-600" />
                    <span className="text-sm font-medium text-green-800">Rooms</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive specific rooms. Related schedules will also be archived.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('rooms')}
                    className="w-full px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 transition-colors"
                  >
                    Archive Rooms
                  </button>
                </div>

                {/* Archive Schedules */}
                <div className="bg-purple-50 border-2 border-purple-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <CalendarDaysIcon className="h-8 w-8 text-purple-600" />
                    <span className="text-sm font-medium text-purple-800">Schedules</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive schedules by academic year and semester.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('schedules')}
                    className="w-full px-4 py-2 bg-purple-600 text-white rounded-md hover:bg-purple-700 transition-colors"
                  >
                    Archive Schedules
                  </button>
                </div>

                {/* Archive Users */}
                <div className="bg-yellow-50 border-2 border-yellow-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <UsersIcon className="h-8 w-8 text-yellow-600" />
                    <span className="text-sm font-medium text-yellow-800">Users</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive users by type (All, Students, Instructors, Custodians, Deans). Related enrollments and attendance will be archived.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('users')}
                    className="w-full px-4 py-2 bg-yellow-600 text-white rounded-md hover:bg-yellow-700 transition-colors"
                  >
                    Archive Users
                  </button>
                </div>

                {/* Archive Attendance */}
                <div className="bg-red-50 border-2 border-red-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <ClipboardDocumentListIcon className="h-8 w-8 text-red-600" />
                    <span className="text-sm font-medium text-red-800">Attendance</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive attendance records by academic year and semester.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('attendance')}
                    className="w-full px-4 py-2 bg-red-600 text-white rounded-md hover:bg-red-700 transition-colors"
                  >
                    Archive Attendance
                  </button>
                </div>

                {/* Archive Backup Copies */}
                <div className="bg-indigo-50 border-2 border-indigo-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <DocumentArrowDownIcon className="h-8 w-8 text-indigo-600" />
                    <span className="text-sm font-medium text-indigo-800">Backup Copies</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive backup zip files so they are hidden from Backup History.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('backups')}
                    className="w-full px-4 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700 transition-colors"
                  >
                    Archive Backup Copies
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* View Archived Data Tabs */}
          {activeTab !== 'dashboard' && (
            <div>
              {loading ? (
                <div className="flex items-center justify-center h-64">
                  <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
                </div>
              ) : (
                <>
                  {/* Archived Subjects View (Grouped) */}
                  {activeTab === 'subjects' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Subjects</h3>
                      {archivedSubjects.length === 0 ? (
                        <div className="text-center py-12">
                          <BookOpenIcon className="mx-auto h-12 w-12 text-gray-400" />
                          <h3 className="mt-2 text-sm font-medium text-gray-900">No archived subjects</h3>
                          <p className="mt-1 text-sm text-gray-500">No subjects have been archived yet.</p>
                        </div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Count</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {groupArchivedItems('subjects', archivedSubjects).map((group) => (
                                <React.Fragment key={group.key}>
                                  <tr className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(group.date)}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{group.items.length}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                      <button
                                        onClick={() => toggleGroup('subjects', group.key)}
                                        className="px-3 py-1.5 text-sm font-medium rounded-md text-white"
                                        style={{
                                          background: isGroupExpanded('subjects', group.key)
                                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                                            : 'linear-gradient(135deg, #1d4ed8 0%, #1e40af 100%)'
                                        }}
                                      >
                                        {isGroupExpanded('subjects', group.key) ? 'Hide Archived Contents' : 'Show Archived Contents'}
                                      </button>
                                    </td>
                                  </tr>
                                  {isGroupExpanded('subjects', group.key) && (
                                    <tr>
                                      <td colSpan={3} className="px-6 py-4 bg-gray-50">
                                        <div className="overflow-x-auto border border-gray-200 rounded">
                                          <table className="min-w-full divide-y divide-gray-200">
                                            <thead className="bg-white">
                                              <tr>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Academic Year</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Semester</th>
                                              </tr>
                                            </thead>
                                            <tbody className="bg-white divide-y divide-gray-200">
                                              {group.items.map((subject) => (
                                                <tr key={subject.SUBJECTID}>
                                                  <td className="px-4 py-2 text-sm">
                                                    <div className="text-gray-900 font-medium">{subject.SUBJECTCODE}</div>
                                                    <div className="text-gray-500">{subject.SUBJECTNAME}</div>
                                                  </td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{subject.ACADEMICYEAR}</td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{subject.SEMESTER}</td>
                                                </tr>
                                              ))}
                                            </tbody>
                                          </table>
                                        </div>
                                      </td>
                                    </tr>
                                  )}
                                </React.Fragment>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Rooms View (Grouped) */}
                  {activeTab === 'rooms' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Rooms</h3>
                      {archivedRooms.length > 0 && (
                        <div className="text-xs text-gray-500">Diagnostics: {archivedRooms.length} rooms • Most recent archived at {formatDate(archivedRooms[0]?.ARCHIVED_AT)}</div>
                      )}
                      {archivedRooms.length === 0 ? (
                        <div className="text-center py-12">
                          <BuildingOfficeIcon className="mx-auto h-12 w-12 text-gray-400" />
                          <h3 className="mt-2 text-sm font-medium text-gray-900">No archived rooms</h3>
                          <p className="mt-1 text-sm text-gray-500">No rooms have been archived yet.</p>
                        </div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Count</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {groupArchivedItems('rooms', archivedRooms).map((group) => (
                                <React.Fragment key={group.key}>
                                  <tr className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(group.date)}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{group.items.length}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                      <button
                                        onClick={() => toggleGroup('rooms', group.key)}
                                        className="px-3 py-1.5 text-sm font-medium rounded-md text-white"
                                        style={{
                                          background: isGroupExpanded('rooms', group.key)
                                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                                            : 'linear-gradient(135deg, #1d4ed8 0%, #1e40af 100%)'
                                        }}
                                      >
                                        {isGroupExpanded('rooms', group.key) ? 'Hide Archived Contents' : 'Show Archived Contents'}
                                      </button>
                                    </td>
                                  </tr>
                                  {isGroupExpanded('rooms', group.key) && (
                                    <tr>
                                      <td colSpan={3} className="px-6 py-4 bg-gray-50">
                                        <div className="overflow-x-auto border border-gray-200 rounded">
                                          <table className="min-w-full divide-y divide-gray-200">
                                            <thead className="bg-white">
                                              <tr>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Building</th>
                                              </tr>
                                            </thead>
                                            <tbody className="bg-white divide-y divide-gray-200">
                                              {group.items.map((room) => (
                                                <tr key={room.ROOMID}>
                                                  <td className="px-4 py-2 text-sm">
                                                    <div className="text-gray-900 font-medium">{room.ROOMNUMBER}</div>
                                                    <div className="text-gray-500">{room.ROOMNAME}</div>
                                                  </td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{room.BUILDING}</td>
                                                </tr>
                                              ))}
                                            </tbody>
                                          </table>
                                        </div>
                                      </td>
                                    </tr>
                                  )}
                                </React.Fragment>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Schedules View (Grouped) */}
                  {activeTab === 'schedules' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Schedules</h3>
                      {archivedSchedules.length === 0 ? (
                        <div className="text-center py-12">
                          <CalendarDaysIcon className="mx-auto h-12 w-12 text-gray-400" />
                          <h3 className="mt-2 text-sm font-medium text-gray-900">No archived schedules</h3>
                          <p className="mt-1 text-sm text-gray-500">No schedules have been archived yet.</p>
                        </div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Count</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {groupArchivedItems('schedules', archivedSchedules).map((group) => (
                                <React.Fragment key={group.key}>
                                  <tr className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(group.date)}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{group.items.length}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                      <button
                                        onClick={() => toggleGroup('schedules', group.key)}
                                        className="px-3 py-1.5 text-sm font-medium rounded-md text-white"
                                        style={{
                                          background: isGroupExpanded('schedules', group.key)
                                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                                            : 'linear-gradient(135deg, #1d4ed8 0%, #1e40af 100%)'
                                        }}
                                      >
                                        {isGroupExpanded('schedules', group.key) ? 'Hide Archived Contents' : 'Show Archived Contents'}
                                      </button>
                                    </td>
                                  </tr>
                                  {isGroupExpanded('schedules', group.key) && (
                                    <tr>
                                      <td colSpan={3} className="px-6 py-4 bg-gray-50">
                                        <div className="overflow-x-auto border border-gray-200 rounded">
                                          <table className="min-w-full divide-y divide-gray-200">
                                            <thead className="bg-white">
                                              <tr>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Day</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Time</th>
                                              </tr>
                                            </thead>
                                            <tbody className="bg-white divide-y divide-gray-200">
                                              {group.items.map((schedule) => (
                                                <tr key={schedule.SCHEDULEID}>
                                                  <td className="px-4 py-2 text-sm">
                                                    <div className="text-gray-900 font-medium">{schedule.SUBJECTCODE}</div>
                                                    <div className="text-gray-500">{schedule.SUBJECTNAME}</div>
                                                  </td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{schedule.ROOMNUMBER}</td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{schedule.DAYOFWEEK}</td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{schedule.STARTTIME} - {schedule.ENDTIME}</td>
                                                </tr>
                                              ))}
                                            </tbody>
                                          </table>
                                        </div>
                                      </td>
                                    </tr>
                                  )}
                                </React.Fragment>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Users View (Grouped) */}
                  {activeTab === 'users' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Users</h3>
                      {archivedUsers.length === 0 ? (
                        <div className="text-center py-12">
                          <UsersIcon className="mx-auto h-12 w-12 text-gray-400" />
                          <h3 className="mt-2 text-sm font-medium text-gray-900">No archived users</h3>
                          <p className="mt-1 text-sm text-gray-500">No users have been archived yet.</p>
                        </div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Count</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {groupArchivedItems('users', archivedUsers).map((group) => (
                                <React.Fragment key={group.key}>
                                  <tr className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(group.date)}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{group.items.length}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                      <button
                                        onClick={() => toggleGroup('users', group.key)}
                                        className="px-3 py-1.5 text-sm font-medium rounded-md text-white"
                                        style={{
                                          background: isGroupExpanded('users', group.key)
                                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                                            : 'linear-gradient(135deg, #1d4ed8 0%, #1e40af 100%)'
                                        }}
                                      >
                                        {isGroupExpanded('users', group.key) ? 'Hide Archived Contents' : 'Show Archived Contents'}
                                      </button>
                                    </td>
                                  </tr>
                                  {isGroupExpanded('users', group.key) && (
                                    <tr>
                                      <td colSpan={3} className="px-6 py-4 bg-gray-50">
                                        <div className="overflow-x-auto border border-gray-200 rounded">
                                          <table className="min-w-full divide-y divide-gray-200">
                                            <thead className="bg-white">
                                              <tr>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Student</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Student ID</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Year Level</th>
                                              </tr>
                                            </thead>
                                            <tbody className="bg-white divide-y divide-gray-200">
                                              {group.items.map((user) => (
                                                <tr key={user.USERID}>
                                                  <td className="px-4 py-2 text-sm">
                                                    <div className="text-gray-900 font-medium">{user.FIRSTNAME} {user.LASTNAME}</div>
                                                  </td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{user.STUDENTID}</td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{user.YEARLEVEL || 'N/A'}</td>
                                                </tr>
                                              ))}
                                            </tbody>
                                          </table>
                                        </div>
                                      </td>
                                    </tr>
                                  )}
                                </React.Fragment>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Attendance View (Grouped) */}
                  {activeTab === 'attendance' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Attendance Records</h3>
                      {archivedAttendance.length === 0 ? (
                        <div className="text-center py-12">
                          <ClipboardDocumentListIcon className="mx-auto h-12 w-12 text-gray-400" />
                          <h3 className="mt-2 text-sm font-medium text-gray-900">No archived attendance</h3>
                          <p className="mt-1 text-sm text-gray-500">No attendance records have been archived yet.</p>
                        </div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Count</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {groupArchivedItems('attendance', archivedAttendance).map((group) => (
                                <React.Fragment key={group.key}>
                                  <tr className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(group.date)}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{group.items.length}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                      <div className="flex items-center justify-end space-x-2">
                                        <button
                                          onClick={() => toggleGroup('attendance', group.key)}
                                          className="px-3 py-1.5 text-sm font-medium rounded-md text-white"
                                          style={{
                                            background: isGroupExpanded('attendance', group.key)
                                              ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                                              : 'linear-gradient(135deg, #1d4ed8 0%, #1e40af 100%)'
                                          }}
                                        >
                                          {isGroupExpanded('attendance', group.key) ? 'Hide Archived Contents' : 'Show Archived Contents'}
                                        </button>
                                        <button
                                          onClick={() => handleUnarchiveClick(group.key, group.date, group.items.length)}
                                          disabled={unarchiving}
                                          className="px-3 py-1.5 text-sm font-medium rounded-md text-white flex items-center space-x-1 disabled:opacity-50"
                                          style={{
                                            background: 'linear-gradient(135deg, #059669 0%, #047857 100%)'
                                          }}
                                        >
                                          <DocumentArrowUpIcon className="h-4 w-4" />
                                          <span>Unarchive</span>
                                        </button>
                                      </div>
                                    </td>
                                  </tr>
                                  {isGroupExpanded('attendance', group.key) && (
                                    <tr>
                                      <td colSpan={3} className="px-6 py-4 bg-gray-50">
                                        <div className="mb-2 text-xs text-gray-500">
                                          Showing {group.items.length} archived record{group.items.length !== 1 ? 's' : ''} 
                                          ({group.items.filter(r => r.RECORD_TYPE === 'attendance_record').length} attendance record{group.items.filter(r => r.RECORD_TYPE === 'attendance_record').length !== 1 ? 's' : ''}, 
                                          {' '}{group.items.filter(r => r.RECORD_TYPE === 'unknown_scan' || r.RECORD_TYPE === 'denied_access').length} access log{group.items.filter(r => r.RECORD_TYPE === 'unknown_scan' || r.RECORD_TYPE === 'denied_access').length !== 1 ? 's' : ''})
                                        </div>
                                        <div className="overflow-x-auto border border-gray-200 rounded">
                                          <table className="min-w-full divide-y divide-gray-200">
                                            <thead className="bg-white">
                                              <tr>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">User</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Action</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Method</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Location</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Date</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Reason</th>
                                              </tr>
                                            </thead>
                                            <tbody className="bg-white divide-y divide-gray-200">
                                              {group.items.map((record) => {
                                                const isAccessLog = record.RECORD_TYPE === 'unknown_scan' || record.RECORD_TYPE === 'denied_access';
                                                const isUnknownUser = record.RECORD_TYPE === 'unknown_scan';
                                                
                                                return (
                                                  <tr key={record.ATTENDANCEID}>
                                                    <td className="px-4 py-2 text-sm">
                                                      <div className="text-gray-900 font-medium">
                                                        {isUnknownUser ? 'Unknown User' : `${record.FIRSTNAME} ${record.LASTNAME}`}
                                                      </div>
                                                      {record.STUDENTID && (
                                                        <div className="text-gray-500">ID: {record.STUDENTID}</div>
                                                      )}
                                                      {isAccessLog && record.RECORD_TYPE === 'unknown_scan' && (
                                                        <div className="text-gray-500 text-xs italic">Unknown Scan</div>
                                                      )}
                                                    </td>
                                                    <td className="px-4 py-2 text-sm">
                                                      <span className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${
                                                        record.STATUS === 'Present' ? 'bg-green-100 text-green-800' :
                                                        record.STATUS === 'Late' ? 'bg-yellow-100 text-yellow-800' :
                                                        record.STATUS === 'Unknown' ? 'bg-gray-100 text-gray-800' :
                                                        record.STATUS === 'Denied' ? 'bg-red-100 text-red-800' :
                                                        'bg-red-100 text-red-800'
                                                      }`}>
                                                        {record.STATUS}
                                                      </span>
                                                    </td>
                                                    <td className="px-4 py-2 text-sm text-gray-900">{record.ACTIONTYPE || 'N/A'}</td>
                                                    <td className="px-4 py-2 text-sm">
                                                      <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${
                                                        record.AUTHMETHOD === 'RFID' ? 'bg-blue-100 text-blue-800' :
                                                        record.AUTHMETHOD === 'Fingerprint' ? 'bg-purple-100 text-purple-800' :
                                                        record.AUTHMETHOD === 'RFID + Fingerprint' ? 'bg-indigo-100 text-indigo-800' :
                                                        'bg-gray-100 text-gray-800'
                                                      }`}>
                                                        {record.AUTHMETHOD || 'N/A'}
                                                      </span>
                                                    </td>
                                                    <td className="px-4 py-2 text-sm">
                                                      <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${
                                                        record.LOCATION === 'inside' ? 'bg-green-100 text-green-800' :
                                                        'bg-orange-100 text-orange-800'
                                                      }`}>
                                                        {record.LOCATION === 'inside' ? 'Inside' : 'Outside'}
                                                      </span>
                                                    </td>
                                                    <td className="px-4 py-2 text-sm text-gray-900">{record.ROOMNUMBER || 'N/A'}</td>
                                                    <td className="px-4 py-2 text-sm text-gray-900">
                                                      {isAccessLog ? (
                                                        <span className="text-gray-400 italic">N/A</span>
                                                      ) : (
                                                        record.SUBJECTCODE || 'N/A'
                                                      )}
                                                    </td>
                                                    <td className="px-4 py-2 text-sm text-gray-900">
                                                      {record.DATE ? formatDate(record.DATE) : (record.TIMESTAMP ? formatDate(record.TIMESTAMP) : 'N/A')}
                                                    </td>
                                                    <td className="px-4 py-2 text-sm text-gray-900">
                                                      {record.REASON ? (
                                                        <span className="text-xs text-gray-600" title={record.REASON}>
                                                          {record.REASON.length > 50 ? `${record.REASON.substring(0, 50)}...` : record.REASON}
                                                        </span>
                                                      ) : (
                                                        <span className="text-gray-400">-</span>
                                                      )}
                                                    </td>
                                                  </tr>
                                                );
                                              })}
                                            </tbody>
                                          </table>
                                        </div>
                                      </td>
                                    </tr>
                                  )}
                                </React.Fragment>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Backups View (Grouped) */}
                  {activeTab === 'backups' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Backup Copies</h3>
                      {archivedBackups.length === 0 ? (
                        <div className="text-center py-8 text-gray-500">No archived backups found</div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Count</th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {groupArchivedItems('backups', archivedBackups).map((group) => (
                                <React.Fragment key={group.key}>
                                  <tr className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(group.date)}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{group.items.length}</td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                                      <button
                                        onClick={() => toggleGroup('backups', group.key)}
                                        className="px-3 py-1.5 text-sm font-medium rounded-md text-white"
                                        style={{
                                          background: isGroupExpanded('backups', group.key)
                                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                                            : 'linear-gradient(135deg, #1d4ed8 0%, #1e40af 100%)'
                                        }}
                                      >
                                        {isGroupExpanded('backups', group.key) ? 'Hide Archived Contents' : 'Show Archived Contents'}
                                      </button>
                                    </td>
                                  </tr>
                                  {isGroupExpanded('backups', group.key) && (
                                    <tr>
                                      <td colSpan={3} className="px-6 py-4 bg-gray-50">
                                        <div className="overflow-x-auto border border-gray-200 rounded">
                                          <table className="min-w-full divide-y divide-gray-200">
                                            <thead className="bg-white">
                                              <tr>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Filename</th>
                                                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Size (MB)</th>
                                              </tr>
                                            </thead>
                                            <tbody className="bg-white divide-y divide-gray-200">
                                              {group.items.map((b) => (
                                                <tr key={b.filename}>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{b.filename}</td>
                                                  <td className="px-4 py-2 text-sm text-gray-900">{b.size}</td>
                                                </tr>
                                              ))}
                                            </tbody>
                                          </table>
                                        </div>
                                      </td>
                                    </tr>
                                  )}
                                </React.Fragment>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Pagination */}
                  {totalPages > 1 && (
                    <div className="mt-6 flex items-center justify-between border-t border-gray-200 pt-4">
                      <div className="text-sm text-gray-700">
                        Page {currentPage} of {totalPages}
                      </div>
                      <div className="flex space-x-2">
                        <button
                          onClick={() => setCurrentPage(Math.max(1, currentPage - 1))}
                          disabled={currentPage === 1}
                          className="px-3 py-1 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                          Previous
                        </button>
                        <button
                          onClick={() => setCurrentPage(Math.min(totalPages, currentPage + 1))}
                          disabled={currentPage === totalPages}
                          className="px-3 py-1 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                          Next
                        </button>
                      </div>
                    </div>
                  )}
                </>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Archive Confirmation Modal */}
      {showArchiveModal && createPortal(
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
          <div className="relative top-20 mx-auto p-5 border w-full max-w-2xl shadow-lg rounded-md bg-white">
            <div className="mt-3">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-lg font-medium text-gray-900">
                  {archiveCategory === 'users' ? 'Archive Users' : `Archive ${archiveCategory.charAt(0).toUpperCase() + archiveCategory.slice(1)}`}
                </h3>
                <button
                  onClick={() => setShowArchiveModal(false)}
                  className="text-gray-400 hover:text-gray-600"
                >
                  <XMarkIcon className="h-6 w-6" />
                </button>
              </div>

              <div className="bg-yellow-50 border border-yellow-200 rounded-md p-4 mb-4">
                <div className="flex items-start">
                  <ExclamationTriangleIcon className="h-5 w-5 text-yellow-600 mr-2 mt-0.5" />
                  <div className="text-sm text-yellow-800">
                    <p className="font-medium mb-1">Warning: This action cannot be undone!</p>
                    <p>Archiving will move the selected data to archive tables and remove it from active records.</p>
                  </div>
                </div>
              </div>

              {(archiveCategory === 'subjects' || archiveCategory === 'schedules' || archiveCategory === 'attendance') && (
                <div className="space-y-4 mb-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">Academic Year</label>
                    <select
                      value={archiveAcademicYear}
                      onChange={(e) => setArchiveAcademicYear(e.target.value)}
                      className="w-full border border-gray-300 rounded-md px-3 py-2"
                    >
                      <option value="">Select Academic Year</option>
                      {academicYears.map(year => (
                        <option key={year} value={year}>{year}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">Semester</label>
                    <select
                      value={archiveSemester}
                      onChange={(e) => setArchiveSemester(e.target.value)}
                      className="w-full border border-gray-300 rounded-md px-3 py-2"
                    >
                      <option value="">Select Semester</option>
                      {semesters.map(sem => (
                        <option key={sem} value={sem}>{sem}</option>
                      ))}
                    </select>
                  </div>
                </div>
              )}

            {archiveCategory === 'backups' && (
                <div className="mb-4">
                  <div className="flex items-center justify-between mb-2">
                    <p className="text-sm text-gray-600">Select backup files to archive:</p>
                    {availableBackups.length > 0 && (
                      <button
                        onClick={selectAllBackups}
                        className="px-3 py-1.5 text-sm font-medium rounded-md transition-all duration-200"
                        style={{
                          background: selectedBackupFiles.length === availableBackups.length 
                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                            : 'linear-gradient(135deg, #800020 0%, #8b1538 100%)',
                          color: 'white',
                          boxShadow: '0 2px 4px rgba(128, 0, 32, 0.3)'
                        }}
                        onMouseEnter={(e) => {
                          if (selectedBackupFiles.length !== availableBackups.length) {
                            e.target.style.transform = 'scale(1.05)';
                            e.target.style.boxShadow = '0 4px 8px rgba(128, 0, 32, 0.4)';
                          }
                        }}
                        onMouseLeave={(e) => {
                          e.target.style.transform = 'scale(1)';
                          e.target.style.boxShadow = '0 2px 4px rgba(128, 0, 32, 0.3)';
                        }}
                      >
                        {selectedBackupFiles.length === availableBackups.length ? '✓ Deselect All' : 'Select All'}
                      </button>
                    )}
                  </div>
                  {loadingBackups ? (
                    <div className="flex items-center justify-center py-8">
                      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
                    </div>
                  ) : (
                    <div className="border border-gray-300 rounded-md max-h-64 overflow-y-auto">
                      {availableBackups.length === 0 ? (
                        <div className="text-center py-8 text-gray-500">No backups found</div>
                      ) : (
                        <div className="divide-y divide-gray-200">
                          {availableBackups.map((b) => (
                            <label key={b.filename} className="flex items-center px-4 py-3 hover:bg-gray-50 cursor-pointer">
                              <input
                                type="checkbox"
                                checked={selectedBackupFiles.includes(b.filename)}
                                onChange={() => {
                                  setSelectedBackupFiles(prev => prev.includes(b.filename)
                                    ? prev.filter(f => f !== b.filename)
                                    : [...prev, b.filename]
                                  );
                                }}
                                className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                              />
                              <div className="ml-3 flex-1">
                                <div className="text-sm font-medium text-gray-900">{b.filename}</div>
                                <div className="text-sm text-gray-500">{b.size} MB • {formatDate(b.date)}</div>
                              </div>
                            </label>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                  {selectedBackupFiles.length > 0 && (
                    <p className="mt-2 text-sm text-blue-600">{selectedBackupFiles.length} backup{selectedBackupFiles.length > 1 ? 's' : ''} selected</p>
                  )}
                </div>
              )}

              {archiveCategory === 'users' && (
                <div className="mb-4">
                  <div className="mb-4">
                    <label className="block text-sm font-medium text-gray-700 mb-2">Archive User Type</label>
                    <select
                      value={selectedUserType}
                      onChange={(e) => handleUserTypeChange(e.target.value)}
                      className="w-full border border-gray-300 rounded-md px-3 py-2"
                    >
                      <option value="all">Archive All</option>
                      <option value="student">Archive Students</option>
                      <option value="instructor">Archive Instructors</option>
                      <option value="custodian">Archive Custodians</option>
                      <option value="dean">Archive Deans</option>
                    </select>
                  </div>
                  <div className="flex items-center justify-between mb-2">
                    <p className="text-sm text-gray-600">
                      Select {selectedUserType === 'all' ? 'users' : 
                        selectedUserType === 'student' ? 'students' :
                        selectedUserType === 'instructor' ? 'instructors' :
                        selectedUserType === 'custodian' ? 'custodians' :
                        selectedUserType === 'dean' ? 'deans' : 'users'} to archive:
                    </p>
                    {availableStudents.length > 0 && (
                      <button
                        onClick={selectAllStudents}
                        className="px-3 py-1.5 text-sm font-medium rounded-md transition-all duration-200"
                        style={{
                          background: selectedUserIds.length === availableStudents.length 
                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                            : 'linear-gradient(135deg, #800020 0%, #8b1538 100%)',
                          color: 'white',
                          boxShadow: '0 2px 4px rgba(128, 0, 32, 0.3)'
                        }}
                        onMouseEnter={(e) => {
                          if (selectedUserIds.length !== availableStudents.length) {
                            e.target.style.transform = 'scale(1.05)';
                            e.target.style.boxShadow = '0 4px 8px rgba(128, 0, 32, 0.4)';
                          }
                        }}
                        onMouseLeave={(e) => {
                          e.target.style.transform = 'scale(1)';
                          e.target.style.boxShadow = '0 2px 4px rgba(128, 0, 32, 0.3)';
                        }}
                      >
                        {selectedUserIds.length === availableStudents.length ? '✓ Deselect All' : 'Select All'}
                      </button>
                    )}
                  </div>
                  
                  {loadingStudents ? (
                    <div className="flex items-center justify-center py-8">
                      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
                    </div>
                  ) : (
                    <div className="border border-gray-300 rounded-md max-h-64 overflow-y-auto">
                      {availableStudents.length === 0 ? (
                        <div className="text-center py-8 text-gray-500">
                          No {selectedUserType === 'all' ? 'users' : 
                            selectedUserType === 'student' ? 'students' :
                            selectedUserType === 'instructor' ? 'instructors' :
                            selectedUserType === 'custodian' ? 'custodians' :
                            selectedUserType === 'dean' ? 'deans' : 'users'} found
                        </div>
                      ) : (
                        <div className="divide-y divide-gray-200">
                          {availableStudents.map((user) => (
                            <label
                              key={user.USERID}
                              className="flex items-center px-4 py-3 hover:bg-gray-50 cursor-pointer"
                            >
                              <input
                                type="checkbox"
                                checked={selectedUserIds.includes(user.USERID)}
                                onChange={() => toggleStudentSelection(user.USERID)}
                                className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                              />
                              <div className="ml-3 flex-1">
                                <div className="text-sm font-medium text-gray-900">
                                  {user.FIRSTNAME} {user.LASTNAME}
                                </div>
                                <div className="text-sm text-gray-500">
                                  {user.STUDENTID || user.FACULTYID || 'N/A'} • {user.USERTYPE}
                                </div>
                              </div>
                            </label>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                  
                  {selectedUserIds.length > 0 && (
                    <p className="mt-2 text-sm text-blue-600">
                      {selectedUserIds.length} {selectedUserType === 'all' ? 'user' : 
                        selectedUserType === 'student' ? 'student' :
                        selectedUserType === 'instructor' ? 'instructor' :
                        selectedUserType === 'custodian' ? 'custodian' :
                        selectedUserType === 'dean' ? 'dean' : 'user'}{selectedUserIds.length > 1 ? 's' : ''} selected
                    </p>
                  )}
                </div>
              )}

              {archiveCategory === 'rooms' && (
                <div className="mb-4">
                  <div className="flex items-center justify-between mb-2">
                    <p className="text-sm text-gray-600">
                      Select rooms to archive:
                    </p>
                    {availableRooms.length > 0 && (
                      <button
                        onClick={selectAllRooms}
                        className="px-3 py-1.5 text-sm font-medium rounded-md transition-all duration-200"
                        style={{
                          background: selectedRoomIds.length === availableRooms.length 
                            ? 'linear-gradient(135deg, #dc2626 0%, #b91c1c 100%)'
                            : 'linear-gradient(135deg, #800020 0%, #8b1538 100%)',
                          color: 'white',
                          boxShadow: '0 2px 4px rgba(128, 0, 32, 0.3)'
                        }}
                        onMouseEnter={(e) => {
                          if (selectedRoomIds.length !== availableRooms.length) {
                            e.target.style.transform = 'scale(1.05)';
                            e.target.style.boxShadow = '0 4px 8px rgba(128, 0, 32, 0.4)';
                          }
                        }}
                        onMouseLeave={(e) => {
                          e.target.style.transform = 'scale(1)';
                          e.target.style.boxShadow = '0 2px 4px rgba(128, 0, 32, 0.3)';
                        }}
                      >
                        {selectedRoomIds.length === availableRooms.length ? '✓ Deselect All' : 'Select All'}
                      </button>
                    )}
                  </div>
                  <p className="text-xs text-gray-500 mb-3">
                    Note: Related schedules will also be archived.
                  </p>
                  
                  {loadingRooms ? (
                    <div className="flex items-center justify-center py-8">
                      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
                    </div>
                  ) : (
                    <div className="border border-gray-300 rounded-md max-h-64 overflow-y-auto">
                      {availableRooms.length === 0 ? (
                        <div className="text-center py-8 text-gray-500">
                          No rooms found
                        </div>
                      ) : (
                        <div className="divide-y divide-gray-200">
                          {availableRooms.map((room) => (
                            <label
                              key={room.ROOMID}
                              className="flex items-center px-4 py-3 hover:bg-gray-50 cursor-pointer"
                            >
                              <input
                                type="checkbox"
                                checked={selectedRoomIds.includes(room.ROOMID)}
                                onChange={() => toggleRoomSelection(room.ROOMID)}
                                className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                              />
                              <div className="ml-3 flex-1">
                                <div className="text-sm font-medium text-gray-900">
                                  {room.ROOMNUMBER}
                                </div>
                                <div className="text-sm text-gray-500">
                                  {room.ROOMNAME} • {room.BUILDING}
                                </div>
                              </div>
                            </label>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                  
                  {selectedRoomIds.length > 0 && (
                    <p className="mt-2 text-sm text-blue-600">
                      {selectedRoomIds.length} room{selectedRoomIds.length > 1 ? 's' : ''} selected
                    </p>
                  )}
                </div>
              )}

              <div className="mb-4">
                <label className="block text-sm font-medium text-gray-700 mb-2">Archive Reason (Optional)</label>
                <textarea
                  value={archiveReason}
                  onChange={(e) => setArchiveReason(e.target.value)}
                  placeholder="Enter reason for archiving..."
                  rows={3}
                  className="w-full border border-gray-300 rounded-md px-3 py-2"
                />
              </div>

              <div className="flex justify-end space-x-3 pt-4">
                <button
                  onClick={() => setShowArchiveModal(false)}
                  className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                  disabled={archiving}
                >
                  Cancel
                </button>
                <button
                  onClick={handleArchive}
                  disabled={archiving}
                  className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {archiving ? (
                    <div className="flex items-center">
                      <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                      Archiving...
                    </div>
                  ) : (
                    'Archive'
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>,
        document.body
      )}

      {/* Unarchive Confirmation Modal */}
      {showUnarchiveModal && createPortal(
        <div className="fixed inset-0 overflow-y-auto z-[60]">
          {/* Backdrop with blur and fade animation */}
          <div className={`fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ease-in-out ${
            unarchiveModalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
          }`}>
            <div className="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
              {/* Modal container with scale and fade animation */}
              <div className={`relative transform overflow-hidden rounded-2xl bg-white text-left shadow-2xl transition-all duration-300 ease-out sm:my-8 sm:w-full sm:max-w-lg ${
                unarchiveModalAnimation === 'visible' 
                  ? 'scale-100 opacity-100 translate-y-0' 
                  : 'scale-95 opacity-0 translate-y-4'
              }`}>
                {/* Gradient header */}
                <div className="bg-gradient-to-r from-green-50 to-emerald-100 px-6 py-8">
                  <div className="mx-auto flex items-center justify-center h-16 w-16 rounded-full bg-emerald-100 shadow-lg mb-4 animate-pulse">
                    <DocumentArrowUpIcon className="h-8 w-8 text-green-600" />
                  </div>
                  
                  {!showSecondConfirmation ? (
                    <>
                      <h3 className="text-xl font-semibold text-gray-900 mb-2">
                        Unarchive Attendance Records
                      </h3>
                      <p className="text-sm text-gray-600 leading-relaxed">
                        Are you sure you want to unarchive <strong>{pendingUnarchiveCount} attendance records</strong> from <strong>{formatDate(pendingUnarchiveDate)}</strong>?
                        This will restore them to active view.
                      </p>
                      <div className="mt-4 p-3 bg-blue-50 border border-blue-200 rounded-lg">
                        <p className="text-sm text-blue-800 font-medium">
                          ℹ️ These records will be restored to active attendance logs and will be visible in regular reports.
                        </p>
                      </div>
                    </>
                  ) : (
                    <>
                      <h3 className="text-xl font-semibold text-gray-900 mb-2">
                        Final Confirmation Required
                      </h3>
                      <p className="text-sm text-gray-600 leading-relaxed">
                        You are about to unarchive <strong>{pendingUnarchiveCount} attendance records</strong> archived on <strong>{formatDate(pendingUnarchiveDate)}</strong>.
                        This will restore them to active view immediately.
                      </p>
                      <div className="mt-4 p-3 bg-green-50 border border-green-200 rounded-lg">
                        <p className="text-sm text-green-800 font-medium">
                          ✅ Confirm to restore these archived attendance records to active status.
                        </p>
                      </div>
                    </>
                  )}
                </div>
                
                {/* Action buttons */}
                <div className="bg-gray-50 px-6 py-4 flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-3 space-y-3 space-y-reverse sm:space-y-0">
                  <button
                    type="button"
                    onClick={handleUnarchiveCancel}
                    disabled={unarchiving}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-gray-300 shadow-sm text-sm font-medium rounded-xl text-gray-700 bg-white hover:bg-gray-50 hover:border-gray-400 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-all duration-200 ease-in-out transform hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  
                  {!showSecondConfirmation ? (
                    <button
                      type="button"
                      onClick={handleUnarchiveConfirm}
                      disabled={unarchiving}
                      className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r from-blue-600 to-blue-700 hover:from-blue-700 hover:to-blue-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      Continue to Final Confirmation
                    </button>
                  ) : (
                    <button
                      type="button"
                      onClick={handleUnarchiveConfirm}
                      disabled={unarchiving}
                      className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r from-green-600 to-emerald-700 hover:from-green-700 hover:to-emerald-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      {unarchiving ? (
                        <>
                          <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                          Unarchiving Records...
                        </>
                      ) : (
                        <>
                          <DocumentArrowUpIcon className="h-4 w-4 mr-2" />
                          CONFIRM UNARCHIVE
                        </>
                      )}
                    </button>
                  )}
                </div>
              </div>
            </div>
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}

export default Archive;

