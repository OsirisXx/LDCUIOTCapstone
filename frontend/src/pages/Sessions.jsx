import React, { useState, useEffect, useMemo } from 'react';
import { 
  PlayIcon, 
  StopIcon, 
  CheckCircleIcon,
  ExclamationTriangleIcon,
  LockClosedIcon,
  LockOpenIcon,
  ClockIcon,
  CalendarDaysIcon,
  MapPinIcon,
  AcademicCapIcon
} from '@heroicons/react/24/outline';
import axios from 'axios';
import toast from 'react-hot-toast';

function Sessions() {
  const [sessions, setSessions] = useState([]);
  const [allSchedules, setAllSchedules] = useState([]);
  const [todaysSchedule, setTodaysSchedule] = useState([]);
  const [instructors, setInstructors] = useState([]);
  const [rooms, setRooms] = useState([]);
  const [loading, setLoading] = useState(true);
  const [currentTime, setCurrentTime] = useState(new Date());
  const [upcomingClasses, setUpcomingClasses] = useState([]);
  const [currentOngoingClasses, setCurrentOngoingClasses] = useState([]);
  const [selectedDay, setSelectedDay] = useState('');
  const [selectedRoomFilter, setSelectedRoomFilter] = useState('');
  const [subjectSearchTerm, setSubjectSearchTerm] = useState('');
  const [currentPage, setCurrentPage] = useState(1);

  useEffect(() => {
    fetchData();
    // Set up real-time updates
    const interval = setInterval(fetchData, 30000); // Update every 30 seconds
    // Update current time every second
    const timeInterval = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => {
      clearInterval(interval);
      clearInterval(timeInterval);
    };
  }, []);

  // Reset to page 1 when filters change
  useEffect(() => {
    setCurrentPage(1);
  }, [selectedDay, selectedRoomFilter, subjectSearchTerm]);

  const fetchData = async () => {
    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      // Use the same unified data endpoint as the management page
      const unifiedResponse = await axios.get('http://localhost:5000/api/unified/data', { headers });
      const unifiedData = unifiedResponse.data;
      
      // Extract data from unified response
      const allSchedulesData = unifiedData.schedules?.data || [];
      setAllSchedules(allSchedulesData);

      // Filter today's schedules
      const currentDay = new Date().toLocaleDateString('en-US', { weekday: 'long' });
      const todaysSchedules = allSchedulesData.filter(schedule => schedule.DAYOFWEEK === currentDay);
      setTodaysSchedule(todaysSchedules);

      // Get instructors and rooms from unified data
      const instructorsData = unifiedData.subjects?.data || [];
      const roomsData = unifiedData.rooms?.data || [];
      
      // Extract unique instructors
      const uniqueInstructors = [...new Map(instructorsData.map(item => [item.INSTRUCTORID, {
        USERID: item.INSTRUCTORID,
        FIRSTNAME: item.instructor_name?.split(' ')[0] || '',
        LASTNAME: item.instructor_name?.split(' ').slice(1).join(' ') || '',
        EMPLOYEEID: item.INSTRUCTORID
      }])).values()];
      setInstructors(uniqueInstructors);

      // Extract unique rooms
      const uniqueRooms = roomsData.map(room => ({
        ROOMID: room.ROOMID,
        ROOMNUMBER: room.ROOMNUMBER,
        ROOMNAME: room.ROOMNAME,
        BUILDING: room.BUILDING,
        DOORSTATUS: 'locked'
      }));
      setRooms(uniqueRooms);

      // Create virtual active sessions based on today's schedule
      const now = new Date();
      const currentTimeStr = now.toTimeString().slice(0, 8); // HH:MM:SS format
      const currentDate = now.toISOString().split('T')[0]; // YYYY-MM-DD format
      
      const virtualSessions = todaysSchedules
        .filter(schedule => {
          const startTime = schedule.STARTTIME;
          const endTime = schedule.ENDTIME;
          return currentTimeStr >= startTime && currentTimeStr <= endTime;
        })
        .map(schedule => ({
          id: schedule.SCHEDULEID,
          session_date: currentDate,
          start_time: `${currentDate}T${schedule.STARTTIME}`,
          end_time: null,
          status: 'active',
          door_unlocked_at: `${currentDate}T${schedule.STARTTIME}`,
          door_locked_at: null,
          instructor_name: schedule.instructor_name,
          course_code: schedule.SUBJECTCODE,
          course_name: schedule.SUBJECTNAME,
          room_number: schedule.ROOMNUMBER,
          room_name: schedule.ROOMNAME,
          attendance_count: 0
        }));
      
      setSessions(virtualSessions);

    } catch (error) {
      console.error('Error fetching data:', error);
      console.error('Error response:', error.response?.data);
      console.error('Error status:', error.response?.status);
      toast.error(`Failed to load session data: ${error.response?.data?.message || error.message}`);
      
      // Fallback to mock data if API fails
      const mockInstructors = [
        { USERID: '1', FIRSTNAME: 'John', LASTNAME: 'Smith', EMPLOYEEID: 'EMP001' },
        { USERID: '2', FIRSTNAME: 'Jane', LASTNAME: 'Doe', EMPLOYEEID: 'EMP002' },
        { USERID: '3', FIRSTNAME: 'Bob', LASTNAME: 'Johnson', EMPLOYEEID: 'EMP003' }
      ];

      const mockRooms = [
        { ROOMID: '1', ROOMNUMBER: 'A101', ROOMNAME: 'Computer Lab 1', BUILDING: 'Main Building', DOORSTATUS: 'locked' },
        { ROOMID: '2', ROOMNUMBER: 'B201', ROOMNAME: 'Mathematics Room', BUILDING: 'Academic Building', DOORSTATUS: 'locked' },
        { ROOMID: '3', ROOMNUMBER: 'C301', ROOMNAME: 'English Classroom', BUILDING: 'Liberal Arts Building', DOORSTATUS: 'locked' }
      ];

      const currentDay = new Date().toLocaleDateString('en-US', { weekday: 'long' });
      const mockTodaysSchedule = [
        {
          SCHEDULEID: '1',
          SUBJECTID: '1',
          SUBJECTCODE: 'CS101',
          SUBJECTNAME: 'Introduction to Computer Science',
          INSTRUCTORID: '1',
          instructor_name: 'John Smith',
          ROOMID: '1',
          ROOMNUMBER: 'A101',
          ROOMNAME: 'Computer Lab 1',
          DAYOFWEEK: currentDay,
          STARTTIME: '08:00:00',
          ENDTIME: '10:00:00',
          ACADEMICYEAR: '2024-2025',
          SEMESTER: 'First Semester'
        }
      ];

      setInstructors(mockInstructors);
      setRooms(mockRooms);
      setTodaysSchedule(mockTodaysSchedule);
    } finally {
      setLoading(false);
    }
  };

  // Analyze current and upcoming classes
  useEffect(() => {
    if (todaysSchedule.length > 0) {
      const now = new Date();
      const currentTimeStr = now.toTimeString().slice(0, 8); // HH:MM:SS format
      
      // Find all current ongoing classes
      const ongoing = todaysSchedule.filter(schedule => {
        const startTime = schedule.STARTTIME;
        const endTime = schedule.ENDTIME;
        const isOngoing = currentTimeStr >= startTime && currentTimeStr <= endTime;
        return isOngoing;
      });
      setCurrentOngoingClasses(ongoing || []);
      
      // Find upcoming classes (next 3 classes after current time)
      const upcoming = todaysSchedule
        .filter(schedule => schedule.STARTTIME > currentTimeStr)
        .sort((a, b) => a.STARTTIME.localeCompare(b.STARTTIME))
        .slice(0, 3);
      
      setUpcomingClasses(upcoming);
    }
  }, [todaysSchedule, currentTime]);


  const formatTime = (timeString) => {
    if (!timeString) return '';
    const date = new Date(timeString);
    return date.toLocaleTimeString('en-US', { 
      hour: '2-digit', 
      minute: '2-digit',
      hour12: true 
    });
  };

  const formatScheduleTime = (time) => {
    if (!time) return '';
    const [hours, minutes] = time.split(':');
    const hour = parseInt(hours);
    const ampm = hour >= 12 ? 'PM' : 'AM';
    const displayHour = hour % 12 || 12;
    return `${displayHour}:${minutes} ${ampm}`;
  };

  const formatCurrentTime = () => {
    return currentTime.toLocaleTimeString('en-US', { 
      hour: '2-digit', 
      minute: '2-digit',
      second: '2-digit',
      hour12: true 
    });
  };

  const formatCurrentDate = () => {
    return currentTime.toLocaleDateString('en-US', { 
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  };

  const getClassStatus = (schedule) => {
    const now = new Date();
    const currentTimeStr = now.toTimeString().slice(0, 8);
    const startTime = schedule.STARTTIME;
    const endTime = schedule.ENDTIME;
    
    if (currentTimeStr >= startTime && currentTimeStr <= endTime) {
      return { status: 'ongoing', color: 'text-green-600', bgColor: 'bg-green-100' };
    } else if (currentTimeStr < startTime) {
      return { status: 'upcoming', color: 'text-blue-600', bgColor: 'bg-blue-100' };
    } else {
      return { status: 'ended', color: 'text-gray-600', bgColor: 'bg-gray-100' };
    }
  };

  const getTimeUntilClass = (schedule) => {
    const now = new Date();
    const today = now.toISOString().split('T')[0];
    const classStart = new Date(`${today}T${schedule.STARTTIME}`);
    const diffMs = classStart.getTime() - now.getTime();
    
    if (diffMs <= 0) return null;
    
    const hours = Math.floor(diffMs / (1000 * 60 * 60));
    const minutes = Math.floor((diffMs % (1000 * 60 * 60)) / (1000 * 60));
    
    if (hours > 0) {
      return `${hours}h ${minutes}m`;
    } else {
      return `${minutes}m`;
    }
  };

  const getSessionDuration = (startTime, endTime) => {
    if (!startTime) return '';
    const start = new Date(startTime);
    const end = endTime ? new Date(endTime) : new Date();
    
    // Check if start time is valid and not too far in the past
    const now = new Date();
    const maxReasonableDuration = 24 * 60 * 60 * 1000; // 24 hours in milliseconds
    
    if (start.getTime() < now.getTime() - maxReasonableDuration) {
      return 'Invalid start time';
    }
    
    const duration = Math.floor((end - start) / (1000 * 60)); // minutes
    const hours = Math.floor(duration / 60);
    const mins = duration % 60;
    
    // Cap duration display at 24 hours
    if (hours > 24) {
      return '24h+';
    }
    
    return hours > 0 ? `${hours}h ${mins}m` : `${mins}m`;
  };

  const groupSchedulesByDay = (schedules) => {
    const dayOrder = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
    const grouped = {};
    
    schedules.forEach(schedule => {
      const day = schedule.DAYOFWEEK;
      if (!grouped[day]) {
        grouped[day] = [];
      }
      grouped[day].push(schedule);
    });

    // Sort schedules within each day by start time
    Object.keys(grouped).forEach(day => {
      grouped[day].sort((a, b) => a.STARTTIME.localeCompare(b.STARTTIME));
    });

    // Return as array sorted by day order
    return dayOrder.map(day => ({
      day,
      schedules: grouped[day] || []
    })).filter(dayGroup => dayGroup.schedules.length > 0);
  };

  const getUniqueDays = () => {
    const days = [...new Set(allSchedules.map(schedule => schedule.DAYOFWEEK).filter(day => day))];
    return ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'].filter(day => days.includes(day));
  };

  const uniqueRooms = useMemo(() => {
    if (!allSchedules || allSchedules.length === 0) {
      return [];
    }

    const rooms = [...new Map(allSchedules.map(schedule => (
      [
        schedule.ROOMID,
        {
          id: schedule.ROOMID,
          number: schedule.room_number || schedule.ROOMNUMBER || 'Unknown',
          name: schedule.room_name || schedule.ROOMNAME || 'Unknown Room',
          building: schedule.BUILDING || 'Unknown Building'
        }
      ]
    ))).values()];

    return rooms.sort((a, b) => {
      const aNum = a.number || 'Unknown';
      const bNum = b.number || 'Unknown';
      return aNum.localeCompare(bNum);
    });
  }, [allSchedules]);

  const getFilteredSchedules = () => {
    let filtered = allSchedules;
    
    // Filter by day
    if (selectedDay) {
      filtered = filtered.filter(schedule => schedule.DAYOFWEEK === selectedDay);
    }
    
    // Filter by room
    if (selectedRoomFilter) {
      filtered = filtered.filter(schedule => schedule.ROOMID === selectedRoomFilter);
    }
    
    // Filter by subject search
    if (subjectSearchTerm) {
      const searchLower = subjectSearchTerm.toLowerCase();
      filtered = filtered.filter(schedule => 
        schedule.SUBJECTCODE?.toLowerCase().includes(searchLower) ||
        schedule.SUBJECTNAME?.toLowerCase().includes(searchLower)
      );
    }
    
    return filtered;
  };

  // Use useMemo to calculate pagination without side effects
  const paginationData = useMemo(() => {
    let filtered = allSchedules;
    
    // Filter by day
    if (selectedDay) {
      filtered = filtered.filter(schedule => schedule.DAYOFWEEK === selectedDay);
    }
    
    // Filter by room
    if (selectedRoomFilter) {
      filtered = filtered.filter(schedule => schedule.ROOMID === selectedRoomFilter);
    }
    
    // Filter by subject search
    if (subjectSearchTerm) {
      const searchLower = subjectSearchTerm.toLowerCase();
      filtered = filtered.filter(schedule => 
        schedule.SUBJECTCODE?.toLowerCase().includes(searchLower) ||
        schedule.SUBJECTNAME?.toLowerCase().includes(searchLower)
      );
    }
    
    const schedulesPerPage = 10;
    
    // Calculate pagination
    const totalFiltered = filtered.length;
    const pages = Math.ceil(totalFiltered / schedulesPerPage);
    
    // Paginate the filtered schedules
    const startIndex = (currentPage - 1) * schedulesPerPage;
    const endIndex = startIndex + schedulesPerPage;
    const paginated = filtered.slice(startIndex, endIndex);
    
    return { paginated, totalFiltered, totalPages: pages };
  }, [allSchedules, selectedDay, selectedRoomFilter, subjectSearchTerm, currentPage]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Session Management</h1>
          <p className="text-gray-600">Monitor and manage class sessions</p>
        </div>
      </div>

      {/* Current Time and Date Display */}
      <div className="bg-gradient-to-r from-blue-50 to-indigo-50 border-2 border-blue-200 rounded-xl p-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-4">
            <div className="flex items-center space-x-2">
              <ClockIcon className="h-8 w-8 text-blue-600" />
              <div>
                <div className="text-3xl font-bold text-blue-900">{formatCurrentTime()}</div>
                <div className="text-lg text-blue-700">{formatCurrentDate()}</div>
              </div>
            </div>
          </div>
          <div className="text-right">
            <div className="flex items-center space-x-2 text-blue-700">
              <CalendarDaysIcon className="h-6 w-6" />
              <span className="text-lg font-semibold">
                {todaysSchedule.length} classes scheduled today
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Current Ongoing Classes */}
      {currentOngoingClasses.length > 0 && (
        <div className="bg-gradient-to-r from-green-50 to-emerald-50 border-2 border-green-200 rounded-xl p-6">
          <div className="flex items-center space-x-2 mb-4">
            <PlayIcon className="h-6 w-6 text-green-600" />
            <h2 className="text-lg font-semibold text-green-900">Classes Currently in Progress</h2>
            <span className="bg-green-100 text-green-800 px-2 py-1 rounded-full text-sm font-medium">
              {currentOngoingClasses.length} active
            </span>
          </div>
          
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {currentOngoingClasses.map((classItem, index) => (
              <div key={classItem.SCHEDULEID} className="bg-white rounded-lg p-4 border border-green-200 shadow-sm">
                <div className="flex items-center justify-between mb-3">
                  <div className="flex items-center space-x-2">
                    <div className="w-8 h-8 bg-green-100 rounded-full flex items-center justify-center">
                      <PlayIcon className="h-4 w-4 text-green-600" />
                    </div>
                    <div>
                      <div className="font-semibold text-green-900">{classItem.SUBJECTCODE}</div>
                      <div className="text-sm text-green-700">{classItem.SUBJECTNAME}</div>
                    </div>
                  </div>
                  <div className="text-right">
                    <div className="text-sm font-bold text-green-600">
                      {formatScheduleTime(classItem.STARTTIME)} - {formatScheduleTime(classItem.ENDTIME)}
                    </div>
                  </div>
                </div>
                
                <div className="space-y-2">
                  <div className="flex items-center space-x-2 text-sm text-gray-600">
                    <AcademicCapIcon className="h-4 w-4" />
                    <span>{classItem.instructor_name}</span>
                  </div>
                  <div className="flex items-center space-x-2 text-sm text-gray-600">
                    <MapPinIcon className="h-4 w-4" />
                    <span>{classItem.ROOMNUMBER} - {classItem.ROOMNAME}</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Upcoming Classes */}
      {upcomingClasses.length > 0 && (
        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex items-center space-x-2 mb-4">
            <ClockIcon className="h-6 w-6 text-blue-600" />
            <h2 className="text-lg font-semibold text-gray-900">Upcoming Classes Today</h2>
          </div>
          
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {upcomingClasses.map((schedule, index) => {
              const timeUntil = getTimeUntilClass(schedule);
              return (
                <div key={schedule.SCHEDULEID} className="border border-gray-200 rounded-lg p-4 hover:shadow-md transition-shadow">
                  <div className="flex items-center justify-between mb-3">
                    <div className="flex items-center space-x-2">
                      <div className="w-8 h-8 bg-blue-100 rounded-full flex items-center justify-center">
                        <span className="text-sm font-bold text-blue-600">{index + 1}</span>
                      </div>
                      <div>
                        <div className="font-semibold text-gray-900">{schedule.SUBJECTCODE}</div>
                        <div className="text-sm text-gray-600">{schedule.SUBJECTNAME}</div>
                      </div>
                    </div>
                    <div className="text-right">
                      <div className="text-lg font-bold text-blue-600">
                        {formatScheduleTime(schedule.STARTTIME)}
                      </div>
                      {timeUntil && (
                        <div className="text-xs text-gray-500">in {timeUntil}</div>
                      )}
                    </div>
                  </div>
                  
                  <div className="space-y-2">
                    <div className="flex items-center space-x-2 text-sm text-gray-600">
                      <AcademicCapIcon className="h-4 w-4" />
                      <span>{schedule.instructor_name}</span>
                    </div>
                    <div className="flex items-center space-x-2 text-sm text-gray-600">
                      <MapPinIcon className="h-4 w-4" />
                      <span>{schedule.ROOMNUMBER} - {schedule.ROOMNAME}</span>
                    </div>
                    <div className="flex items-center space-x-2 text-sm text-gray-600">
                      <ClockIcon className="h-4 w-4" />
                      <span>{formatScheduleTime(schedule.STARTTIME)} - {formatScheduleTime(schedule.ENDTIME)}</span>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}


      {/* Active Sessions - Moved to Top */}
      <div className="bg-white rounded-lg shadow p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Current Active Sessions</h2>
        
        {sessions.length === 0 ? (
          <p className="text-gray-500">No active sessions for today</p>
        ) : (
          <div className="space-y-4">
            {sessions.map((session) => (
              <div key={session.id} className="border border-gray-200 rounded-lg p-4">
                <div className="flex items-center justify-between mb-3">
                  <div className="flex items-center space-x-3">
                    <div className={`flex items-center space-x-2 px-3 py-1 rounded-full text-sm ${
                      session.status === 'active' 
                        ? 'bg-green-100 text-green-800' 
                        : session.status === 'waiting'
                        ? 'bg-yellow-100 text-yellow-800'
                        : session.status === 'ended'
                        ? 'bg-red-100 text-red-800'
                        : 'bg-gray-100 text-gray-800'
                    }`}>
                      {session.status === 'active' ? (
                        <PlayIcon className="h-4 w-4" />
                      ) : session.status === 'waiting' ? (
                        <ExclamationTriangleIcon className="h-4 w-4" />
                      ) : (
                        <StopIcon className="h-4 w-4" />
                      )}
                      <span className="capitalize">{session.status}</span>
                    </div>
                    
                    {session.door_unlocked_at && !session.door_locked_at && (
                      <div className="flex items-center space-x-1 px-2 py-1 bg-yellow-100 text-yellow-800 rounded-full text-xs">
                        <LockOpenIcon className="h-3 w-3" />
                        <span>Door Unlocked</span>
                      </div>
                    )}
                    
                    {session.door_locked_at && (
                      <div className="flex items-center space-x-1 px-2 py-1 bg-red-100 text-red-800 rounded-full text-xs">
                        <LockClosedIcon className="h-3 w-3" />
                        <span>Door Locked</span>
                      </div>
                    )}
                  </div>
                  
                  <div className="text-sm text-gray-500">
                    Duration: {getSessionDuration(session.start_time, session.end_time)}
                  </div>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                  <div>
                    <p className="text-sm font-medium text-gray-900">{session.course_code || session.SUBJECTCODE}</p>
                    <p className="text-xs text-gray-600">{session.course_name || session.SUBJECTNAME}</p>
                  </div>
                  
                  <div>
                    <p className="text-sm font-medium text-gray-900">{session.instructor_name}</p>
                    <p className="text-xs text-gray-600">Instructor</p>
                  </div>
                  
                  <div>
                    <p className="text-sm font-medium text-gray-900">{session.room_number}</p>
                    <p className="text-xs text-gray-600">{session.room_name}</p>
                  </div>
                  
                  <div>
                    <p className="text-sm font-medium text-gray-900">
                      {formatTime(session.start_time)}
                      {session.end_time && ` - ${formatTime(session.end_time)}`}
                    </p>
                    <p className="text-xs text-gray-600">
                      {session.attendance_count || 0} students present
                    </p>
                  </div>
                </div>
              </div>
              ))}
          </div>
        )}
      </div>

      {/* Weekly Schedule */}
      <div className="bg-white rounded-lg shadow p-6">
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-lg font-semibold text-gray-900">Weekly Schedule</h2>
          
          {/* Filters */}
          <div className="flex space-x-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Search Subject</label>
              <input
                type="text"
                placeholder="Search by code or name..."
                value={subjectSearchTerm}
                onChange={(e) => setSubjectSearchTerm(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-2 text-sm w-48"
              />
            </div>
            
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Day Filter</label>
              <select
                value={selectedDay}
                onChange={(e) => setSelectedDay(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-2 text-sm"
              >
                <option value="">All Days</option>
                {allSchedules.length > 0 && getUniqueDays().map(day => (
                  <option key={day} value={day}>{day}</option>
                ))}
              </select>
            </div>
            
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Room Filter</label>
              <select
                value={selectedRoomFilter}
                onChange={(e) => setSelectedRoomFilter(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-2 text-sm"
              >
                <option value="">All Rooms</option>
                {allSchedules.length > 0 && uniqueRooms.map(room => (
                  <option key={room.id} value={room.id}>
                    {room.number} - {room.name}
                  </option>
                ))}
              </select>
            </div>
            
            <div className="flex items-end">
              <button
                onClick={() => {
                  setSelectedDay('');
                  setSelectedRoomFilter('');
                  setSubjectSearchTerm('');
                }}
                className="px-3 py-2 text-sm text-gray-600 hover:text-gray-800 underline"
              >
                Clear Filters
              </button>
            </div>
          </div>
        </div>
        
        {allSchedules.length === 0 ? (
          <p className="text-gray-500">No schedules found</p>
        ) : (
          <div className="space-y-6">
            {(() => {
              const { paginated: paginatedSchedules, totalFiltered } = paginationData;
              const dayGroups = groupSchedulesByDay(paginatedSchedules);
              
              if (dayGroups.length === 0) {
                return (
                  <div className="text-center py-8">
                    <CalendarDaysIcon className="mx-auto h-12 w-12 text-gray-400" />
                    <h3 className="mt-2 text-sm font-medium text-gray-900">No schedules found</h3>
                    <p className="mt-1 text-sm text-gray-500">
                      Try adjusting your filters to see more results.
                    </p>
                  </div>
                );
              }
              
              return dayGroups.map((dayGroup) => {
                const currentDay = new Date().toLocaleDateString('en-US', { weekday: 'long' });
                const isToday = dayGroup.day === currentDay;
                
                return (
                  <div key={dayGroup.day} className="border border-gray-200 rounded-lg overflow-hidden">
                    <div className={`px-4 py-3 ${isToday ? 'bg-blue-50 border-b border-blue-200' : 'bg-gray-50 border-b border-gray-200'}`}>
                      <h3 className={`text-lg font-semibold ${isToday ? 'text-blue-900' : 'text-gray-900'}`}>
                        {dayGroup.day}
                        {isToday && <span className="ml-2 text-sm font-normal text-blue-600">(Today)</span>}
                        <span className="ml-2 text-sm font-normal text-gray-500">({dayGroup.schedules.length} classes)</span>
                      </h3>
                    </div>
                    <div className="p-4">
                      {dayGroup.schedules.length === 0 ? (
                        <p className="text-gray-500 text-sm">No classes scheduled</p>
                      ) : (
                        <div className="space-y-3">
                          {dayGroup.schedules.map((schedule) => {
                            const classStatus = getClassStatus(schedule);
                            const timeUntil = getTimeUntilClass(schedule);
                            const isToday = dayGroup.day === new Date().toLocaleDateString('en-US', { weekday: 'long' });
                            
                            return (
                              <div key={schedule.SCHEDULEID} className={`flex items-center justify-between p-3 rounded-lg border-2 ${
                                isToday ? classStatus.bgColor : 'bg-gray-50'
                              } ${isToday ? 'border-gray-300' : 'border-gray-200'}`}>
                                <div className="flex-1">
                                  <div className="flex items-center space-x-2 mb-1">
                                    <h4 className="font-medium text-gray-900">{schedule.SUBJECTCODE} - {schedule.SUBJECTNAME}</h4>
                                    {isToday && (
                                      <span className={`inline-flex items-center px-2 py-1 rounded-full text-xs font-medium ${classStatus.bgColor} ${classStatus.color}`}>
                                        {classStatus.status === 'ongoing' && <PlayIcon className="h-3 w-3 mr-1" />}
                                        {classStatus.status === 'upcoming' && <ClockIcon className="h-3 w-3 mr-1" />}
                                        {classStatus.status === 'ended' && <CheckCircleIcon className="h-3 w-3 mr-1" />}
                                        {classStatus.status}
                                      </span>
                                    )}
                                  </div>
                                  <p className="text-sm text-gray-600">
                                    <AcademicCapIcon className="h-4 w-4 inline mr-1" />
                                    {schedule.instructor_name}
                                  </p>
                                  <p className="text-sm text-gray-600">
                                    <MapPinIcon className="h-4 w-4 inline mr-1" />
                                    {schedule.ROOMNUMBER} - {schedule.ROOMNAME} ({schedule.BUILDING})
                                  </p>
                                  <p className="text-xs text-gray-500">{schedule.ACADEMICYEAR} • {schedule.SEMESTER}</p>
                                  {isToday && timeUntil && classStatus.status === 'upcoming' && (
                                    <p className="text-xs text-blue-600 font-medium">
                                      Starts in {timeUntil}
                                    </p>
                                  )}
                                </div>
                                <div className="text-right ml-4">
                                  <p className="text-sm font-medium text-gray-900">
                                    {formatScheduleTime(schedule.STARTTIME)} - {formatScheduleTime(schedule.ENDTIME)}
                                  </p>
                                  <p className="text-xs text-gray-500">Class Duration</p>
                                  {isToday && classStatus.status === 'ongoing' && (
                                    <p className="text-xs text-green-600 font-medium">Currently Active</p>
                                  )}
                                </div>
                              </div>
                            );
                          })}
                        </div>
                      )}
                    </div>
                  </div>
                );
              });
            })()}
          </div>
        )}

        {/* Pagination */}
        {(() => {
          const { totalFiltered, totalPages } = paginationData;
          if (totalFiltered === 0) return null;
          
          return (
            <div className="mt-6 flex items-center justify-between border-t border-gray-200 pt-4">
              <div className="text-sm text-gray-700">
                Showing {Math.min((currentPage - 1) * 10 + 1, totalFiltered)} to {Math.min(currentPage * 10, totalFiltered)} of {totalFiltered} schedules
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
          );
        })()}
      </div>

    </div>
  );
}

export default Sessions; 