import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import {
  ArchiveBoxIcon,
  BookOpenIcon,
  BuildingOfficeIcon,
  CalendarDaysIcon,
  UsersIcon,
  ClipboardDocumentListIcon,
  ClockIcon,
  ExclamationTriangleIcon,
  CheckCircleIcon,
  XMarkIcon,
  MagnifyingGlassIcon,
  TrashIcon,
  DocumentArrowDownIcon,
  EyeIcon,
  CalendarIcon,
  AcademicCapIcon,
  MapPinIcon,
  UserGroupIcon,
  ClockIcon as ClockOutlineIcon,
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
  const [selectedRoomIds, setSelectedRoomIds] = useState([]);
  const [selectedUserIds, setSelectedUserIds] = useState([]);
  const [availableStudents, setAvailableStudents] = useState([]);
  const [loadingStudents, setLoadingStudents] = useState(false);
  const [availableRooms, setAvailableRooms] = useState([]);
  const [loadingRooms, setLoadingRooms] = useState(false);
  
  // View archived data states
  const [archivedSubjects, setArchivedSubjects] = useState([]);
  const [archivedRooms, setArchivedRooms] = useState([]);
  const [archivedSchedules, setArchivedSchedules] = useState([]);
  const [archivedUsers, setArchivedUsers] = useState([]);
  const [archivedAttendance, setArchivedAttendance] = useState([]);
  const [archivedSessions, setArchivedSessions] = useState([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [paginationData, setPaginationData] = useState({});

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
      
      const uniqueYears = [...new Set(response.data.subjects.data.map(s => s.ACADEMICYEAR).filter(Boolean))];
      const uniqueSemesters = [...new Set(response.data.subjects.data.map(s => s.SEMESTER).filter(Boolean))];
      
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
          setPaginationData(response.data.pagination);
          break;
        case 'rooms':
          response = await axios.get('/api/archive/rooms', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedRooms(response.data.rooms);
          setPaginationData(response.data.pagination);
          break;
        case 'schedules':
          response = await axios.get('/api/archive/schedules', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedSchedules(response.data.schedules);
          setPaginationData(response.data.pagination);
          break;
        case 'users':
          response = await axios.get('/api/archive/users', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedUsers(response.data.users);
          setPaginationData(response.data.pagination);
          break;
        case 'attendance':
          response = await axios.get('/api/archive/attendance', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedAttendance(response.data.records);
          setPaginationData(response.data.pagination);
          break;
        case 'sessions':
          response = await axios.get('/api/archive/sessions', { 
            headers: { Authorization: `Bearer ${token}` },
            params 
          });
          setArchivedSessions(response.data.sessions);
          setPaginationData(response.data.pagination);
          break;
      }
      
      setTotalPages(response.data.pagination.pages);
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
      await fetchStudents();
    } else if (category === 'rooms') {
      await fetchRooms();
    }
  };

  const fetchStudents = async () => {
    try {
      setLoadingStudents(true);
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/users?type=student&limit=1000', {
        headers: { Authorization: `Bearer ${token}` }
      });
      setAvailableStudents(response.data.users || []);
    } catch (error) {
      console.error('Error fetching students:', error);
      toast.error('Failed to load students');
    } finally {
      setLoadingStudents(false);
    }
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

  const handleArchive = async () => {
    try {
      setArchiving(true);
      const token = localStorage.getItem('token');
      
      let payload = { reason: archiveReason || null };
      
      switch (archiveCategory) {
        case 'subjects':
        case 'schedules':
        case 'attendance':
        case 'sessions':
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
            toast.error('Please select at least one student to archive');
            return;
          }
          payload.user_ids = selectedUserIds;
          break;
      }

      const response = await axios.post(`/api/archive/${archiveCategory}`, payload, {
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
              <p className="text-sm font-medium text-gray-500">Archived Students</p>
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
              <ClockIcon className="h-8 w-8 text-indigo-500" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Archived Sessions</p>
              <p className="text-2xl font-semibold text-gray-900">{getCategoryStats('sessions')}</p>
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
              Archived Students
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
              onClick={() => setActiveTab('sessions')}
              className={`py-4 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'sessions'
                  ? 'border-blue-500 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              Archived Sessions
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

                {/* Archive Students */}
                <div className="bg-yellow-50 border-2 border-yellow-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <UsersIcon className="h-8 w-8 text-yellow-600" />
                    <span className="text-sm font-medium text-yellow-800">Students</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive students (admins and instructors excluded). Related enrollments and attendance will be archived.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('users')}
                    className="w-full px-4 py-2 bg-yellow-600 text-white rounded-md hover:bg-yellow-700 transition-colors"
                  >
                    Archive Students
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

                {/* Archive Sessions */}
                <div className="bg-indigo-50 border-2 border-indigo-200 rounded-lg p-6">
                  <div className="flex items-center justify-between mb-4">
                    <ClockIcon className="h-8 w-8 text-indigo-600" />
                    <span className="text-sm font-medium text-indigo-800">Sessions</span>
                  </div>
                  <p className="text-sm text-gray-600 mb-4">
                    Archive sessions by academic year and semester.
                  </p>
                  <button
                    onClick={() => handleArchiveClick('sessions')}
                    className="w-full px-4 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700 transition-colors"
                  >
                    Archive Sessions
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
                  {/* Archived Subjects View */}
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
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Academic Year</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Semester</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived At</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {archivedSubjects.map((subject) => (
                                <tr key={subject.SUBJECTID} className="hover:bg-gray-50">
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <div className="text-sm font-medium text-gray-900">{subject.SUBJECTCODE}</div>
                                    <div className="text-sm text-gray-500">{subject.SUBJECTNAME}</div>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{subject.ACADEMICYEAR}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{subject.SEMESTER}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(subject.ARCHIVED_AT)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Rooms View */}
                  {activeTab === 'rooms' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Rooms</h3>
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
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Building</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived At</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {archivedRooms.map((room) => (
                                <tr key={room.ROOMID} className="hover:bg-gray-50">
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <div className="text-sm font-medium text-gray-900">{room.ROOMNUMBER}</div>
                                    <div className="text-sm text-gray-500">{room.ROOMNAME}</div>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{room.BUILDING}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(room.ARCHIVED_AT)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Schedules View */}
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
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Day</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Time</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived At</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {archivedSchedules.map((schedule) => (
                                <tr key={schedule.SCHEDULEID} className="hover:bg-gray-50">
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <div className="text-sm font-medium text-gray-900">{schedule.SUBJECTCODE}</div>
                                    <div className="text-sm text-gray-500">{schedule.SUBJECTNAME}</div>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{schedule.ROOMNUMBER}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{schedule.DAYOFWEEK}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{schedule.STARTTIME} - {schedule.ENDTIME}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(schedule.ARCHIVED_AT)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Users View */}
                  {activeTab === 'users' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Students</h3>
                      {archivedUsers.length === 0 ? (
                        <div className="text-center py-12">
                          <UsersIcon className="mx-auto h-12 w-12 text-gray-400" />
                          <h3 className="mt-2 text-sm font-medium text-gray-900">No archived students</h3>
                          <p className="mt-1 text-sm text-gray-500">No students have been archived yet.</p>
                        </div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Student</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Student ID</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Year Level</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived At</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {archivedUsers.map((user) => (
                                <tr key={user.USERID} className="hover:bg-gray-50">
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <div className="text-sm font-medium text-gray-900">{user.FIRSTNAME} {user.LASTNAME}</div>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{user.STUDENTID}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{user.YEARLEVEL || 'N/A'}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(user.ARCHIVED_AT)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Attendance View */}
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
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Student</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Action Type</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Method</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Location</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived At</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {archivedAttendance.map((record) => (
                                <tr key={record.ATTENDANCEID} className="hover:bg-gray-50">
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <div className="text-sm font-medium text-gray-900">{record.FIRSTNAME} {record.LASTNAME}</div>
                                    <div className="text-sm text-gray-500">{record.STUDENTID}</div>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <span className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${
                                      record.STATUS === 'Present' ? 'bg-green-100 text-green-800' :
                                      record.STATUS === 'Late' ? 'bg-yellow-100 text-yellow-800' :
                                      'bg-red-100 text-red-800'
                                    }`}>
                                      {record.STATUS}
                                    </span>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{record.ACTIONTYPE || 'N/A'}</td>
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${
                                      record.AUTHMETHOD === 'RFID' ? 'bg-blue-100 text-blue-800' :
                                      record.AUTHMETHOD === 'Fingerprint' ? 'bg-purple-100 text-purple-800' :
                                      record.AUTHMETHOD === 'RFID + Fingerprint' ? 'bg-indigo-100 text-indigo-800' :
                                      'bg-gray-100 text-gray-800'
                                    }`}>
                                      {record.AUTHMETHOD || 'Fingerprint'}
                                    </span>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${
                                      record.LOCATION === 'inside' ? 'bg-green-100 text-green-800' :
                                      'bg-orange-100 text-orange-800'
                                    }`}>
                                      {record.LOCATION === 'inside' ? 'Inside' : 'Outside'}
                                    </span>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{record.ROOMNUMBER || 'N/A'}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{record.SUBJECTCODE || 'N/A'}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(record.DATE)}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(record.ARCHIVED_AT)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Archived Sessions View */}
                  {activeTab === 'sessions' && (
                    <div className="space-y-4">
                      <h3 className="text-lg font-semibold text-gray-900">Archived Sessions</h3>
                      {archivedSessions.length === 0 ? (
                        <div className="text-center py-12">
                          <ClockIcon className="mx-auto h-12 w-12 text-gray-400" />
                          <h3 className="mt-2 text-sm font-medium text-gray-900">No archived sessions</h3>
                          <p className="mt-1 text-sm text-gray-500">No sessions have been archived yet.</p>
                        </div>
                      ) : (
                        <div className="overflow-x-auto">
                          <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Subject</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Room</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Date</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Archived At</th>
                              </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                              {archivedSessions.map((session) => (
                                <tr key={session.SESSIONID} className="hover:bg-gray-50">
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <div className="text-sm font-medium text-gray-900">{session.SUBJECTCODE}</div>
                                    <div className="text-sm text-gray-500">{session.SUBJECTNAME}</div>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{session.ROOMNUMBER}</td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatDate(session.SESSIONDATE)}</td>
                                  <td className="px-6 py-4 whitespace-nowrap">
                                    <span className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${
                                      session.STATUS === 'active' ? 'bg-green-100 text-green-800' :
                                      session.STATUS === 'ended' ? 'bg-gray-100 text-gray-800' :
                                      'bg-yellow-100 text-yellow-800'
                                    }`}>
                                      {session.STATUS}
                                    </span>
                                  </td>
                                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(session.ARCHIVED_AT)}</td>
                                </tr>
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
                  Archive {archiveCategory.charAt(0).toUpperCase() + archiveCategory.slice(1)}
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

              {(archiveCategory === 'subjects' || archiveCategory === 'schedules' || archiveCategory === 'attendance' || archiveCategory === 'sessions') && (
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

              {archiveCategory === 'users' && (
                <div className="mb-4">
                  <div className="flex items-center justify-between mb-2">
                    <p className="text-sm text-gray-600">
                      Select students to archive:
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
                  <p className="text-xs text-gray-500 mb-3">
                    Note: Only students can be archived. Admins and instructors are excluded.
                  </p>
                  
                  {loadingStudents ? (
                    <div className="flex items-center justify-center py-8">
                      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
                    </div>
                  ) : (
                    <div className="border border-gray-300 rounded-md max-h-64 overflow-y-auto">
                      {availableStudents.length === 0 ? (
                        <div className="text-center py-8 text-gray-500">
                          No students found
                        </div>
                      ) : (
                        <div className="divide-y divide-gray-200">
                          {availableStudents.map((student) => (
                            <label
                              key={student.USERID}
                              className="flex items-center px-4 py-3 hover:bg-gray-50 cursor-pointer"
                            >
                              <input
                                type="checkbox"
                                checked={selectedUserIds.includes(student.USERID)}
                                onChange={() => toggleStudentSelection(student.USERID)}
                                className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                              />
                              <div className="ml-3 flex-1">
                                <div className="text-sm font-medium text-gray-900">
                                  {student.FIRSTNAME} {student.LASTNAME}
                                </div>
                                <div className="text-sm text-gray-500">
                                  {student.STUDENTID}
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
                      {selectedUserIds.length} student{selectedUserIds.length > 1 ? 's' : ''} selected
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
    </div>
  );
}

export default Archive;

