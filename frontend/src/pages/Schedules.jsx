import React, { useState, useEffect } from 'react';
import { 
  PlusIcon, 
  PencilIcon, 
  TrashIcon, 
  ClockIcon, 
  UserGroupIcon, 
  XMarkIcon,
  MagnifyingGlassIcon,
  UserPlusIcon,
  UserMinusIcon,
  AcademicCapIcon,
  CheckIcon,
  ExclamationTriangleIcon
} from '@heroicons/react/24/outline';
import toast from 'react-hot-toast';
import axios from 'axios';

// Set up axios defaults
axios.defaults.baseURL = 'http://localhost:5000';

function Schedules() {
  const [schedules, setSchedules] = useState([]);
  const [subjects, setsubjects] = useState([]);
  const [instructors, setInstructors] = useState([]);
  const [rooms, setRooms] = useState([]);
  const [students, setStudents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [editingSchedule, setEditingSchedule] = useState(null);
  const [showEnrollModal, setShowEnrollModal] = useState(false);
  const [selectedSchedule, setSelectedSchedule] = useState(null);
  const [enrollmentData, setEnrollmentData] = useState(null);
  const [enrollmentLoading, setEnrollmentLoading] = useState(false);
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedStudents, setSelectedStudents] = useState([]);
  const [showAddStudents, setShowAddStudents] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleteData, setDeleteData] = useState(null);
  const [modalAnimation, setModalAnimation] = useState('hidden');

  const [formData, setFormData] = useState({
    subject_id: '',
    room_id: '',
    academic_year: '2024-2025',
    semester: 'First Semester',
    time_slots: [
      {
        id: 1,
        selected_days: [],
        start_time: '',
        end_time: ''
      }
    ]
  });
  
  const [scheduleGroups, setScheduleGroups] = useState([]);

  const daysOfWeek = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  const semesters = ['First Semester', 'Second Semester', 'Summer'];
  
  // Helper function to toggle day selection for a specific time slot
  const toggleDaySelection = (timeSlotId, day) => {
    console.log('toggleDaySelection called with:', { timeSlotId, day });
    setFormData(prev => {
      console.log('Before toggle - selected_days:', prev.time_slots.find(s => s.id === timeSlotId)?.selected_days);
      const newState = {
        ...prev,
        time_slots: prev.time_slots.map(slot => 
          slot.id === timeSlotId 
            ? {
                ...slot,
                selected_days: slot.selected_days.includes(day)
                  ? slot.selected_days.filter(d => d !== day)
                  : [...slot.selected_days, day]
              }
            : slot
        )
      };
      console.log('After toggle - selected_days:', newState.time_slots.find(s => s.id === timeSlotId)?.selected_days);
      return newState;
    });
  };

  // Helper function to add a new time slot
  const addTimeSlot = () => {
    const newId = Math.max(...formData.time_slots.map(slot => slot.id)) + 1;
    setFormData(prev => ({
      ...prev,
      time_slots: [
        ...prev.time_slots,
        {
          id: newId,
          selected_days: [],
          start_time: '',
          end_time: ''
        }
      ]
    }));
  };

  // Helper function to remove a time slot
  const removeTimeSlot = (timeSlotId) => {
    if (formData.time_slots.length > 1) {
      setFormData(prev => ({
        ...prev,
        time_slots: prev.time_slots.filter(slot => slot.id !== timeSlotId)
      }));
    }
  };

  // Helper function to update time slot data
  const updateTimeSlot = (timeSlotId, field, value) => {
    setFormData(prev => ({
      ...prev,
      time_slots: prev.time_slots.map(slot =>
        slot.id === timeSlotId
          ? { ...slot, [field]: value }
          : slot
      )
    }));
  };
  
  // Helper function to group schedules by SUBJECT ONLY
  const groupSchedulesBySubject = (schedulesList) => {
    const groups = {};
    schedulesList.forEach(schedule => {
      // Create a key that includes ONLY subject info
      const key = `${schedule.SUBJECTID}-${schedule.subject_code}-${schedule.subject_name}`;
      if (!groups[key]) {
        groups[key] = {
          subject_id: schedule.SUBJECTID,
          subject_code: schedule.subject_code,
          subject_name: schedule.subject_name,
          instructor_name: schedule.instructor_name,
          academic_year: schedule.ACADEMICYEAR,
          semester: schedule.SEMESTER,
          schedules: [] // Keep original schedules for management
        };
      }
      groups[key].schedules.push(schedule);
    });
    
    // Group schedules within each subject by time slots
    Object.values(groups).forEach(group => {
      const timeSlotGroups = {};
      group.schedules.forEach(schedule => {
        const timeKey = `${schedule.STARTTIME}-${schedule.ENDTIME}-${schedule.ROOMID}`;
        if (!timeSlotGroups[timeKey]) {
          timeSlotGroups[timeKey] = {
            start_time: schedule.STARTTIME,
            end_time: schedule.ENDTIME,
            room_id: schedule.ROOMID,
            room_number: schedule.room_number,
            room_name: schedule.room_name,
            days: [],
            schedules: []
          };
        }
        timeSlotGroups[timeKey].days.push(schedule.DAYOFWEEK);
        timeSlotGroups[timeKey].schedules.push(schedule);
      });
      
      // Sort days in each time slot by weekday order
      const dayOrder = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
      Object.values(timeSlotGroups).forEach(timeSlot => {
        timeSlot.days.sort((a, b) => dayOrder.indexOf(a) - dayOrder.indexOf(b));
      });
      
      group.time_slots = Object.values(timeSlotGroups);
    });
    
    return Object.values(groups);
  };

  useEffect(() => {
    fetchData();
  }, []);

  // Reset form when modal opens for creating new schedule
  useEffect(() => {
    if (showModal && !editingSchedule) {
      console.log('Modal opened for creating new schedule - resetting form');
      resetForm();
    }
  }, [showModal, editingSchedule]);

  const fetchData = async () => {
    try {
      setLoading(true);
      const token = localStorage.getItem('token');
      
      if (!token) {
        toast.error('Please login to access schedules');
        return;
      }

      const headers = { Authorization: `Bearer ${token}` };

      // Fetch all data in parallel
      const [schedulesRes, subjectsRes, instructorsRes, roomsRes, studentsRes] = await Promise.all([
        axios.get('/api/schedules', { headers }),
        axios.get('/api/subjects', { headers }),
        axios.get('/api/users?type=instructor', { headers }),
        axios.get('/api/rooms', { headers }),
        axios.get('/api/users?type=student', { headers })
      ]);

      const schedulesList = schedulesRes.data.schedules || [];
      setSchedules(schedulesList);
      setScheduleGroups(groupSchedulesBySubject(schedulesList));
      setsubjects(subjectsRes.data.subjects || []);
      setInstructors(instructorsRes.data.users || []);
      setRooms(roomsRes.data.rooms || []);
      setStudents(studentsRes.data.users || []);

    } catch (error) {
      console.error('Error fetching data:', error);
      if (error.response?.status === 401) {
        toast.error('Session expired. Please login again.');
        localStorage.removeItem('token');
        window.location.href = '/login';
      } else {
        toast.error('Failed to load data');
      }
    } finally {
      setLoading(false);
    }
  };

  const fetchEnrollmentData = async (scheduleId) => {
    try {
      setEnrollmentLoading(true);
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      const response = await axios.get(`/api/enrollment/schedule/${scheduleId}`, { headers });
      setEnrollmentData(response.data);
    } catch (error) {
      console.error('Error fetching enrollment data:', error);
      toast.error('Failed to load enrollment data');
    } finally {
      setEnrollmentLoading(false);
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    
    // Validate that at least one time slot has selected days
    const validTimeSlots = formData.time_slots.filter(slot => slot.selected_days.length > 0);
    if (validTimeSlots.length === 0) {
      toast.error('Please select at least one day in at least one time slot');
      return;
    }

    // Validate that all time slots with selected days have times
    const invalidTimeSlots = validTimeSlots.filter(slot => !slot.start_time || !slot.end_time);
    if (invalidTimeSlots.length > 0) {
      toast.error('Please set start and end times for all time slots with selected days');
      return;
    }

    // In edit mode, ensure only one day is selected
    if (editingSchedule) {
      const invalidEditSlots = validTimeSlots.filter(slot => slot.selected_days.length !== 1);
      if (invalidEditSlots.length > 0) {
        toast.error('In edit mode, please select exactly one day per time slot');
        return;
      }
    }
    
    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      if (editingSchedule) {
        // Update existing schedule (use first time slot for editing)
        const firstTimeSlot = validTimeSlots[0];
        const updateData = {
          subject_id: formData.subject_id,
          room_id: formData.room_id,
          day_of_week: firstTimeSlot.selected_days[0],
          start_time: firstTimeSlot.start_time,
          end_time: firstTimeSlot.end_time,
          academic_year: formData.academic_year,
          semester: formData.semester
        };
        await axios.put(`/api/schedules/${editingSchedule.SCHEDULEID}`, updateData, { headers });
        toast.success('Schedule updated successfully');
      } else {
        // Create new schedules for each time slot and each day
        const promises = [];
        
        validTimeSlots.forEach(timeSlot => {
          timeSlot.selected_days.forEach(day => {
            const scheduleData = {
              subject_id: formData.subject_id,
              room_id: formData.room_id,
              day_of_week: day,
              start_time: timeSlot.start_time,
              end_time: timeSlot.end_time,
              academic_year: formData.academic_year,
              semester: formData.semester
            };
            promises.push(axios.post('/api/schedules', scheduleData, { headers }));
          });
        });
        
        const results = await Promise.allSettled(promises);
        
        // Check for any failures
        const failures = results.filter(result => result.status === 'rejected');
        const successes = results.filter(result => result.status === 'fulfilled');
        
        if (failures.length > 0) {
          console.error('Some schedules failed to create:', failures);
          const errorMessages = failures.map(failure => failure.reason?.response?.data?.message || failure.reason?.message || 'Unknown error');
          const failedDays = failures.map(failure => {
            const requestData = failure.reason?.config?.data;
            if (requestData) {
              const parsedData = JSON.parse(requestData);
              return parsedData.day_of_week;
            }
            return 'Unknown day';
          });
          toast.error(`${failures.length} schedule(s) failed for days: ${failedDays.join(', ')}. Errors: ${errorMessages.slice(0, 2).join(', ')}`);
        }
        
        const totalSchedules = validTimeSlots.reduce((sum, slot) => sum + slot.selected_days.length, 0);
        if (successes.length > 0) {
          toast.success(`${successes.length} schedule(s) created successfully out of ${totalSchedules} total`);
        }
      }

      await fetchData(); // Refresh data
      setShowModal(false);
      resetForm();
      setEditingSchedule(null);
    } catch (error) {
      console.error('Error saving schedule:', error);
      if (error.response?.data?.message) {
        toast.error(error.response.data.message);
      } else {
        toast.error('Failed to save schedule');
      }
    }
  };

  const resetForm = () => {
    setFormData({
      subject_id: '',
      room_id: '',
      academic_year: '2024-2025',
      semester: 'First Semester',
      time_slots: [
        {
          id: 1,
          selected_days: [],
          start_time: '',
          end_time: ''
        }
      ]
    });
  };

  const handleEdit = (schedule) => {
    setEditingSchedule(schedule);
    setFormData({
      subject_id: schedule.SUBJECTID || '',
      room_id: schedule.ROOMID || '',
      academic_year: schedule.ACADEMICYEAR || '2024-2025',
      semester: schedule.SEMESTER || 'First Semester',
      time_slots: [
        {
          id: 1,
          selected_days: [schedule.DAYOFWEEK || 'Monday'],
          start_time: schedule.STARTTIME ? schedule.STARTTIME.substring(0, 5) : '',
          end_time: schedule.ENDTIME ? schedule.ENDTIME.substring(0, 5) : ''
        }
      ]
    });
    setShowModal(true);
  };

  const handleDelete = async (scheduleId) => {
    setDeleteData({
      type: 'schedule',
      id: scheduleId,
      title: 'Delete Schedule',
      message: 'Are you sure you want to delete this schedule? This action cannot be undone.',
      confirmText: 'Delete Schedule'
    });
    setShowDeleteModal(true);
    setTimeout(() => setModalAnimation('visible'), 10);
  };

  const confirmDelete = async () => {
    if (!deleteData) return;

    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };
      
      if (deleteData.type === 'schedule') {
        await axios.delete(`/api/schedules/${deleteData.id}`, { headers });
        toast.success('Schedule deleted successfully');
      } else if (deleteData.type === 'timeSlot') {
        await Promise.all(
          deleteData.schedules.map(schedule => 
            axios.delete(`/api/schedules/${schedule.SCHEDULEID}`, { headers })
          )
        );
        toast.success(`Time slot deleted (${deleteData.daysCount} day(s))`);
      } else if (deleteData.type === 'student') {
        await axios.delete(
          `/api/enrollment/schedule/${deleteData.scheduleId}/student/${deleteData.studentId}`,
          { headers }
        );
        toast.success('Student removed successfully');
      }
      
      await fetchData(); // Refresh data
      if (deleteData.type === 'student') {
        await fetchEnrollmentData(selectedSchedule.SCHEDULEID);
      }
    } catch (error) {
      console.error('Error deleting:', error);
      if (deleteData.type === 'student') {
        toast.error('Failed to remove student');
      } else {
        toast.error('Failed to delete schedule');
      }
    } finally {
      setModalAnimation('hidden');
      setTimeout(() => {
        setShowDeleteModal(false);
        setDeleteData(null);
      }, 300);
    }
  };

  const handleManageEnrollment = async (schedule) => {
    setSelectedSchedule(schedule);
    setShowEnrollModal(true);
    setSearchTerm('');
    setSelectedStudents([]);
    setShowAddStudents(false);
    await fetchEnrollmentData(schedule.SCHEDULEID);
  };

  const handleEnrollStudents = async () => {
    if (selectedStudents.length === 0) {
      toast.error('Please select students to enroll');
      return;
    }

    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      const response = await axios.post(
        `/api/enrollment/schedule/${selectedSchedule.SCHEDULEID}/enroll`,
        { studentIds: selectedStudents },
        { headers }
      );

      if (response.data.errors && response.data.errors.length > 0) {
        toast.error(`Some enrollments failed: ${response.data.errors.slice(0, 2).join(', ')}`);
      } else {
        toast.success(response.data.message);
      }

      setSelectedStudents([]);
      setShowAddStudents(false);
      await fetchEnrollmentData(selectedSchedule.SCHEDULEID);
    } catch (error) {
      console.error('Error enrolling students:', error);
      toast.error('Failed to enroll students');
    }
  };

  const handleRemoveStudent = (studentId, studentName) => {
    setDeleteData({
      type: 'student',
      studentId: studentId,
      scheduleId: selectedSchedule.SCHEDULEID,
      studentName: studentName,
      title: 'Remove Student',
      message: `Are you sure you want to remove ${studentName} from this subject? This action cannot be undone.`,
      confirmText: 'Remove Student'
    });
    setShowDeleteModal(true);
    setTimeout(() => setModalAnimation('visible'), 10);
  };

  const handleStudentSelection = (studentId) => {
    setSelectedStudents(prev => 
      prev.includes(studentId) 
        ? prev.filter(id => id !== studentId)
        : [...prev, studentId]
    );
  };

  const formatTime = (time) => {
    if (!time) return '';
    const [hours, minutes] = time.split(':');
    const hour = parseInt(hours);
    const ampm = hour >= 12 ? 'PM' : 'AM';
    const displayHour = hour % 12 || 12;
    return `${displayHour}:${minutes} ${ampm}`;
  };

  const filteredAvailableStudents = enrollmentData?.available?.filter(student =>
    student.student_name.toLowerCase().includes(searchTerm.toLowerCase()) ||
    student.EMAIL.toLowerCase().includes(searchTerm.toLowerCase()) ||
    student.STUDENTID?.toLowerCase().includes(searchTerm.toLowerCase())
  ) || [];

  const filteredEnrolledStudents = enrollmentData?.enrolled?.filter(student =>
    student.student_name.toLowerCase().includes(searchTerm.toLowerCase()) ||
    student.EMAIL.toLowerCase().includes(searchTerm.toLowerCase()) ||
    student.STUDENTID?.toLowerCase().includes(searchTerm.toLowerCase())
  ) || [];

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
          <h1 className="text-2xl font-bold text-gray-900">Schedule Management</h1>
          <p className="text-gray-600">Manage class schedules, subjects, and enrollment</p>
        </div>
        <button
          onClick={() => {
            console.log('Add Schedule button clicked');
            setEditingSchedule(null);
            setShowModal(true);
          }}
          className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700"
        >
          <PlusIcon className="h-4 w-4 mr-2" />
          Add Schedule
        </button>
      </div>

      {/* Schedule Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {scheduleGroups.map((group, groupIndex) => (
          <div key={`group-${group.subject_id}-${groupIndex}`} className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex justify-between items-start mb-4">
              <div>
                <h3 className="text-lg font-semibold text-gray-900">{group.subject_code}</h3>
                <p className="text-sm text-gray-600">{group.subject_name}</p>
                <p className="text-xs text-gray-500 mt-1">
                  <strong>Instructor:</strong> {group.instructor_name || 'Not assigned'}
                </p>
              </div>
              <div className="flex items-center space-x-2">
                <button
                  onClick={() => {
                    setFormData({
                      subject_id: group.subject_id,
                      room_id: group.time_slots[0]?.room_id || '',
                      academic_year: group.academic_year || '2024-2025',
                      semester: group.semester || 'First Semester',
                      time_slots: [
                        {
                          id: 1,
                          selected_days: [],
                          start_time: group.time_slots[0]?.start_time ? group.time_slots[0].start_time.substring(0, 5) : '',
                          end_time: group.time_slots[0]?.end_time ? group.time_slots[0].end_time.substring(0, 5) : ''
                        }
                      ]
                    });
                    setEditingSchedule(null);
                    setShowModal(true);
                  }}
                  className="text-green-500 hover:text-green-700 p-1 rounded-md hover:bg-green-50"
                  title="Add more days to this time slot"
                >
                  <PlusIcon className="h-4 w-4" />
                </button>
              </div>
            </div>

            <div className="mb-4 relative">
              {group.time_slots.length === 1 ? (
                /* Compact layout for single time slot */
                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center text-sm text-gray-700">
                      <ClockIcon className="h-4 w-4 mr-2 text-blue-500" />
                      <span className="font-medium">{formatTime(group.time_slots[0].start_time)} - {formatTime(group.time_slots[0].end_time)}</span>
                    </div>
                    <div className="flex space-x-1">
                      <button
                        onClick={() => handleEdit(group.time_slots[0].schedules[0])}
                        className="text-gray-400 hover:text-blue-600 p-1 rounded"
                        title="Edit this schedule"
                      >
                        <PencilIcon className="h-3 w-3" />
                      </button>
                      <button
                        onClick={() => handleDelete(group.time_slots[0].schedules[0].SCHEDULEID)}
                        className="text-gray-400 hover:text-red-600 p-1 rounded"
                        title="Delete this schedule"
                      >
                        <TrashIcon className="h-3 w-3" />
                      </button>
                    </div>
                  </div>
                  
                  <div className="flex flex-wrap gap-1">
                    {group.time_slots[0].days.map((day, dayIndex) => (
                      <span 
                        key={`${day}-${dayIndex}`}
                        className="inline-flex items-center px-2 py-1 text-xs font-medium bg-blue-100 text-blue-800 rounded-md"
                      >
                        {day}
                      </span>
                    ))}
                  </div>
                  
                  <div className="text-xs text-gray-600 space-y-1">
                    <div><strong>Room:</strong> {group.time_slots[0].room_number} - {group.time_slots[0].room_name}</div>
                    <div><strong>Semester:</strong> {group.semester} ({group.academic_year})</div>
                  </div>
                  
                  {/* Collapsible individual day management for single time slot */}
                  {group.time_slots[0].schedules.length > 1 && (
                    <details className="mt-2">
                      <summary className="text-xs text-gray-500 cursor-pointer hover:text-gray-700">
                        Manage individual days ({group.time_slots[0].schedules.length})
                      </summary>
                      <div className="mt-2 space-y-1">
                        {group.time_slots[0].schedules.map((schedule) => (
                          <div key={schedule.SCHEDULEID} className="flex justify-between items-center py-1 px-2 bg-gray-50 rounded border">
                            <span className="text-xs text-gray-600">{schedule.DAYOFWEEK}</span>
                            <div className="flex space-x-1">
                              <button
                                onClick={() => handleEdit(schedule)}
                                className="text-gray-400 hover:text-blue-600 p-0.5 rounded"
                                title={`Edit ${schedule.DAYOFWEEK}`}
                              >
                                <PencilIcon className="h-3 w-3" />
                              </button>
                              <button
                                onClick={() => handleDelete(schedule.SCHEDULEID)}
                                className="text-gray-400 hover:text-red-600 p-0.5 rounded"
                                title={`Delete ${schedule.DAYOFWEEK}`}
                              >
                                <TrashIcon className="h-3 w-3" />
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    </details>
                  )}
                </div>
              ) : (
                /* Full layout for multiple time slots with scrolling */
                <>
                  {/* Scroll indicator at top */}
                  {group.time_slots.length > 2 && (
                    <div className="flex items-center justify-center mb-2 text-xs text-gray-500">
                      <div className="flex items-center space-x-1">
                        <div className="w-2 h-2 bg-gray-300 rounded-full animate-pulse"></div>
                        <span>Multiple time slots - scroll to see all</span>
                        <div className="w-2 h-2 bg-gray-300 rounded-full animate-pulse"></div>
                      </div>
                    </div>
                  )}
                  
                  {/* Scrollable container for time slots */}
                  <div 
                    className={`space-y-4 ${group.time_slots.length > 2 ? 'max-h-96 overflow-y-auto pr-2' : ''}`}
                    style={group.time_slots.length > 2 ? {
                      scrollbarWidth: 'thin',
                      scrollbarColor: '#d1d5db #f3f4f6'
                    } : {}}
                  >
                    {group.time_slots.map((timeSlot, timeSlotIndex) => (
                      <div key={`timeslot-${timeSlotIndex}`} className="border border-gray-200 rounded-md p-4 bg-gray-50 relative">
                        {/* Time slot number indicator for scrollable content */}
                        {group.time_slots.length > 2 && (
                          <div className="absolute -left-1 top-2 w-6 h-6 bg-blue-500 text-white text-xs rounded-full flex items-center justify-center font-medium shadow-sm">
                            {timeSlotIndex + 1}
                          </div>
                        )}
                        
                        <div className="flex justify-between items-start">
                          <div className="flex-1">
                            <div className="flex items-center text-sm text-gray-700 mb-2">
                              <ClockIcon className="h-4 w-4 mr-2 text-blue-500" />
                              <span className="font-medium">{formatTime(timeSlot.start_time)} - {formatTime(timeSlot.end_time)}</span>
                            </div>
                            
                            {/* Display days as badges */}
                            <div className="flex flex-wrap gap-1 mb-2">
                              {timeSlot.days.map((day, dayIndex) => (
                                <span 
                                  key={`${day}-${dayIndex}`}
                                  className="inline-flex items-center px-2 py-1 text-xs font-medium bg-blue-100 text-blue-800 rounded-md"
                                >
                                  {day}
                                </span>
                              ))}
                            </div>
                            
                            <div className="text-xs text-gray-600">
                              <strong>Room:</strong> {timeSlot.room_number} - {timeSlot.room_name}
                            </div>
                            <div className="text-xs text-gray-600">
                              <strong>Semester:</strong> {group.semester} ({group.academic_year})
                            </div>
                          </div>
                          
                          <div className="flex space-x-1 ml-2">
                            {/* Edit first schedule in this time slot */}
                            <button
                              onClick={() => handleEdit(timeSlot.schedules[0])}
                              className="text-gray-400 hover:text-blue-600 p-1 rounded"
                              title="Edit this time slot"
                            >
                              <PencilIcon className="h-3 w-3" />
                            </button>
                            
                            {/* Delete all schedules in this time slot */}
                            <button
                              onClick={() => {
                                setDeleteData({
                                  type: 'timeSlot',
                                  schedules: timeSlot.schedules,
                                  daysCount: timeSlot.days.length,
                                  title: 'Delete Time Slot',
                                  message: `Are you sure you want to delete this time slot (${timeSlot.days.length} day(s))? This will remove all schedules for ${timeSlot.days.join(', ')}.`,
                                  confirmText: 'Delete Time Slot'
                                });
                                setShowDeleteModal(true);
                                setTimeout(() => setModalAnimation('visible'), 10);
                              }}
                              className="text-gray-400 hover:text-red-600 p-1 rounded"
                              title="Delete this time slot"
                            >
                              <TrashIcon className="h-3 w-3" />
                            </button>
                          </div>
                        </div>
                        
                        {/* Individual day management (collapsible) */}
                        <details className="mt-2">
                          <summary className="text-xs text-gray-500 cursor-pointer hover:text-gray-700">
                            Manage individual days ({timeSlot.schedules.length})
                          </summary>
                          <div className="mt-2 space-y-1">
                            {timeSlot.schedules.map((schedule) => (
                              <div key={schedule.SCHEDULEID} className="flex justify-between items-center py-1 px-2 bg-white rounded border">
                                <span className="text-xs text-gray-600">{schedule.DAYOFWEEK}</span>
                                <div className="flex space-x-1">
                                  <button
                                    onClick={() => handleEdit(schedule)}
                                    className="text-gray-400 hover:text-blue-600 p-0.5 rounded"
                                    title={`Edit ${schedule.DAYOFWEEK}`}
                                  >
                                    <PencilIcon className="h-3 w-3" />
                                  </button>
                                  <button
                                    onClick={() => handleDelete(schedule.SCHEDULEID)}
                                    className="text-gray-400 hover:text-red-600 p-0.5 rounded"
                                    title={`Delete ${schedule.DAYOFWEEK}`}
                                  >
                                    <TrashIcon className="h-3 w-3" />
                                  </button>
                                </div>
                              </div>
                            ))}
                          </div>
                        </details>
                      </div>
                    ))}
                  </div>
                  
                  {/* Scroll indicator at bottom */}
                  {group.time_slots.length > 2 && (
                    <div className="flex items-center justify-center mt-2 text-xs text-gray-400">
                      <div className="flex items-center space-x-1">
                        <span>↑ Scroll up/down to see all {group.time_slots.length} time slots ↓</span>
                      </div>
                    </div>
                  )}
                </>
              )}
            </div>

            <button
              onClick={() => handleManageEnrollment(group.time_slots[0]?.schedules[0])}
              className="w-full inline-flex items-center justify-center px-3 py-2 border border-gray-300 shadow-sm text-sm leading-4 font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50"
            >
              <UserGroupIcon className="h-4 w-4 mr-2" />
              Manage Enrollment
            </button>
          </div>
        ))}
      </div>

      {scheduleGroups.length === 0 && !loading && (
        <div className="text-center py-12">
          <ClockIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">No schedules</h3>
          <p className="mt-1 text-sm text-gray-500">Get started by creating a new schedule.</p>
          <div className="mt-6">
            <button
              onClick={() => {
                console.log('Empty state Add Schedule button clicked');
                setEditingSchedule(null);
                setShowModal(true);
              }}
              className="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700"
            >
              <PlusIcon className="h-4 w-4 mr-2" />
              Add Schedule
            </button>
          </div>
        </div>
      )}

      {/* Add/Edit Schedule Modal */}
      {showModal && (
        <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
          <div className="relative top-20 mx-auto p-5 border w-96 shadow-lg rounded-md bg-white">
            <div className="mt-3">
              <h3 className="text-lg font-medium text-gray-900 mb-4">
                {editingSchedule ? 'Edit Schedule' : 'Add New Schedule'}
              </h3>
              <form onSubmit={handleSubmit} className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Subject</label>
                  <select
                    value={formData.subject_id}
                    onChange={(e) => setFormData({ ...formData, subject_id: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    required
                  >
                    <option value="">Select Subject</option>
                    {subjects.map(subject => (
                      <option key={subject.SUBJECTID} value={subject.SUBJECTID}>
                        {subject.SUBJECTCODE} - {subject.SUBJECTNAME}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Room</label>
                  <select
                    value={formData.room_id}
                    onChange={(e) => setFormData({ ...formData, room_id: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    required
                  >
                    <option value="">Select Room</option>
                    {rooms.map(room => (
                      <option key={room.ROOMID} value={room.ROOMID}>
                        {room.ROOMNUMBER} - {room.ROOMNAME}
                      </option>
                    ))}
                  </select>
                </div>



                {/* Dynamic Time Slots - Single Container */}
                <div className="border border-gray-200 rounded-lg p-4 bg-gray-50">
                  <div className="flex justify-between items-center mb-4">
                    <label className="block text-sm font-medium text-gray-700">
                      Schedule Time Slots {!editingSchedule && <span className="text-gray-500 text-xs">(add multiple time slots for the same subject)</span>}
                    </label>
                    {!editingSchedule && (
                      <button
                        type="button"
                        onClick={addTimeSlot}
                        className="inline-flex items-center px-3 py-1 border border-green-300 shadow-sm text-xs font-medium rounded-md text-green-700 bg-green-50 hover:bg-green-100 transition-colors"
                      >
                        <PlusIcon className="h-3 w-3 mr-1" />
                        Add Time Slot
                      </button>
                    )}
                  </div>

                  <div className="space-y-6">
                    {formData.time_slots.map((timeSlot, index) => (
                      <div key={timeSlot.id} className={`${index > 0 ? 'border-t border-gray-300 pt-6' : ''}`}>
                        <div className="flex justify-between items-center mb-3">
                          <h4 className="text-sm font-medium text-gray-700">
                            Time Slot {index + 1}
                          </h4>
                          {!editingSchedule && formData.time_slots.length > 1 && (
                            <button
                              type="button"
                              onClick={() => removeTimeSlot(timeSlot.id)}
                              className="text-red-400 hover:text-red-600 p-1"
                              title="Remove this time slot"
                            >
                              <XMarkIcon className="h-4 w-4" />
                            </button>
                          )}
                        </div>

                        {/* Days Selection */}
                        <div className="mb-4">
                          <label className="block text-sm font-medium text-gray-700 mb-2">
                            Select Days {editingSchedule && <span className="text-gray-500 text-xs">(Edit mode: select one day only)</span>}
                          </label>
                          <div className="grid grid-cols-4 sm:grid-cols-7 gap-2">
                            {daysOfWeek.map(day => (
                              <button
                                key={day}
                                type="button"
                                onClick={() => {
                                  console.log('Day clicked:', day);
                                  console.log('editingSchedule:', editingSchedule);
                                  console.log('Form data time slots:', formData.time_slots);
                                  
                                  if (editingSchedule) {
                                    console.log('EDIT MODE: Setting single day');
                                    // In edit mode, only allow single day selection (radio button behavior)
                                    setFormData(prev => ({
                                      ...prev,
                                      time_slots: prev.time_slots.map(slot => 
                                        slot.id === timeSlot.id 
                                          ? { ...slot, selected_days: [day] }
                                          : slot
                                      )
                                    }));
                                  } else {
                                    console.log('CREATE MODE: Toggling day');
                                    // In create mode, allow multiple day selection (toggle behavior)
                                    toggleDaySelection(timeSlot.id, day);
                                  }
                                }}
                                className={`px-3 py-2 text-sm font-medium rounded-lg border transition-all duration-200 ${
                                  timeSlot.selected_days.includes(day)
                                    ? 'bg-blue-600 text-white border-blue-600 shadow-md transform scale-105'
                                    : 'bg-white text-gray-700 border-gray-300 hover:border-blue-400 hover:bg-blue-50 hover:text-blue-600'
                                }`}
                              >
                                {day.substring(0, 3)}
                              </button>
                            ))}
                          </div>
                          {timeSlot.selected_days.length > 0 && (
                            <div className="mt-2 text-sm text-gray-600">
                              Selected: {timeSlot.selected_days.join(', ')}
                              {editingSchedule && timeSlot.selected_days.length > 1 && (
                                <span className="text-orange-600 ml-2">(Edit mode: only the first day will be saved)</span>
                              )}
                            </div>
                          )}
                        </div>

                        {/* Time Selection */}
                        <div className="grid grid-cols-2 gap-4">
                          <div>
                            <label className="block text-sm font-medium text-gray-700">Start Time</label>
                            <input
                              type="time"
                              value={timeSlot.start_time}
                              onChange={(e) => updateTimeSlot(timeSlot.id, 'start_time', e.target.value)}
                              className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                              required
                            />
                          </div>
                          <div>
                            <label className="block text-sm font-medium text-gray-700">End Time</label>
                            <input
                              type="time"
                              value={timeSlot.end_time}
                              onChange={(e) => updateTimeSlot(timeSlot.id, 'end_time', e.target.value)}
                              className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                              required
                            />
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Academic Year</label>
                  <input
                    type="text"
                    value={formData.academic_year}
                    onChange={(e) => setFormData({ ...formData, academic_year: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    placeholder="2024-2025"
                    required
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Semester</label>
                  <select
                    value={formData.semester}
                    onChange={(e) => setFormData({ ...formData, semester: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    required
                  >
                    {semesters.map(semester => (
                      <option key={semester} value={semester}>{semester}</option>
                    ))}
                  </select>
                </div>

                <div className="flex justify-end space-x-3 pt-4">
                  <button
                    type="button"
                    onClick={() => {
                      setShowModal(false);
                      setEditingSchedule(null);
                      resetForm();
                    }}
                    className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700"
                  >
                    {editingSchedule ? 'Update' : 'Create'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* Enrollment Management Modal */}
      {showEnrollModal && selectedSchedule && (
        <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
          <div className="relative top-10 mx-auto p-6 border max-w-6xl shadow-lg rounded-md bg-white my-8">
            <div className="flex justify-between items-center mb-6">
              <div>
                <h3 className="text-xl font-semibold text-gray-900">
                  Manage Enrollment - {selectedSchedule.subject_code}
                </h3>
                <p className="text-sm text-gray-600">
                  {selectedSchedule.subject_name} • {selectedSchedule.instructor_name}
                </p>
                {enrollmentData && (
                  <p className="text-sm text-blue-600 mt-1">
                    {enrollmentData.statistics.totalEnrolled} enrolled • {enrollmentData.statistics.totalAvailable} available
                  </p>
                )}
              </div>
              <button
                onClick={() => setShowEnrollModal(false)}
                className="text-gray-400 hover:text-gray-600"
              >
                <XMarkIcon className="h-6 w-6" />
              </button>
            </div>

            {enrollmentLoading ? (
              <div className="flex items-center justify-center h-64">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
              </div>
            ) : enrollmentData ? (
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* Enrolled Students */}
                <div className="bg-gray-50 rounded-lg p-4">
                  <div className="flex justify-between items-center mb-4">
                    <h4 className="text-lg font-medium text-gray-900">
                      Enrolled Students ({enrollmentData.enrolled.length})
                    </h4>
                  </div>

                  <div className="mb-4">
                    <div className="relative">
                      <MagnifyingGlassIcon className="h-5 w-5 absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
                      <input
                        type="text"
                        placeholder="Search enrolled students..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        className="w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500"
                      />
                    </div>
                  </div>

                  <div className="max-h-96 overflow-y-auto space-y-2">
                    {filteredEnrolledStudents.length === 0 ? (
                      <p className="text-gray-500 text-center py-8">No enrolled students found</p>
                    ) : (
                      filteredEnrolledStudents.map((student) => (
                        <div key={student.USERID} className="bg-white rounded-lg p-3 border border-gray-200">
                          <div className="flex justify-between items-center">
                            <div className="flex items-center space-x-3">
                              <div className="flex-shrink-0 h-8 w-8 bg-green-100 rounded-full flex items-center justify-center">
                                <AcademicCapIcon className="h-4 w-4 text-green-600" />
                              </div>
                              <div>
                                <div className="text-sm font-medium text-gray-900">{student.student_name}</div>
                                <div className="text-xs text-gray-500">
                                  {student.STUDENTID} • Year {student.YEARLEVEL} • {student.DEPARTMENT}
                                </div>
                                <div className="text-xs text-gray-400">{student.EMAIL}</div>
                              </div>
                            </div>
                            <button
                              onClick={() => handleRemoveStudent(student.USERID, student.student_name)}
                              className="text-red-600 hover:text-red-800 p-1"
                              title="Remove student"
                            >
                              <UserMinusIcon className="h-4 w-4" />
                            </button>
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                </div>

                {/* Available Students */}
                <div className="bg-gray-50 rounded-lg p-4">
                  <div className="flex justify-between items-center mb-4">
                    <h4 className="text-lg font-medium text-gray-900">
                      Available Students ({enrollmentData.available.length})
                    </h4>
                    {selectedStudents.length > 0 && (
                      <div className="flex space-x-2">
                        <span className="text-sm text-gray-600">
                          {selectedStudents.length} selected
                        </span>
                        <button
                          onClick={handleEnrollStudents}
                          className="inline-flex items-center px-3 py-1 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700"
                        >
                          <UserPlusIcon className="h-3 w-3 mr-1" />
                          Enroll Selected
                        </button>
                      </div>
                    )}
                  </div>

                  <div className="mb-4">
                    <div className="relative">
                      <MagnifyingGlassIcon className="h-5 w-5 absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
                      <input
                        type="text"
                        placeholder="Search available students..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        className="w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500"
                      />
                    </div>
                  </div>

                  <div className="max-h-96 overflow-y-auto space-y-2">
                    {filteredAvailableStudents.length === 0 ? (
                      <p className="text-gray-500 text-center py-8">No available students found</p>
                    ) : (
                      filteredAvailableStudents.map((student) => (
                        <div key={student.USERID} className="bg-white rounded-lg p-3 border border-gray-200">
                          <div className="flex justify-between items-center">
                            <div className="flex items-center space-x-3">
                              <div className="flex-shrink-0 h-8 w-8 bg-gray-100 rounded-full flex items-center justify-center">
                                <AcademicCapIcon className="h-4 w-4 text-gray-600" />
                              </div>
                              <div>
                                <div className="text-sm font-medium text-gray-900">{student.student_name}</div>
                                <div className="text-xs text-gray-500">
                                  {student.STUDENTID} • Year {student.YEARLEVEL} • {student.DEPARTMENT}
                                </div>
                                <div className="text-xs text-gray-400">{student.EMAIL}</div>
                              </div>
                            </div>
                            <button
                              onClick={() => handleStudentSelection(student.USERID)}
                              className={`p-1 rounded ${
                                selectedStudents.includes(student.USERID)
                                  ? 'text-blue-600 bg-blue-100'
                                  : 'text-gray-400 hover:text-blue-600'
                              }`}
                              title={selectedStudents.includes(student.USERID) ? 'Deselect' : 'Select to enroll'}
                            >
                              {selectedStudents.includes(student.USERID) ? (
                                <CheckIcon className="h-4 w-4" />
                              ) : (
                                <UserPlusIcon className="h-4 w-4" />
                              )}
                            </button>
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </div>
            ) : (
              <div className="text-center py-8">
                <p className="text-gray-500">Failed to load enrollment data</p>
                <button
                  onClick={() => fetchEnrollmentData(selectedSchedule.SCHEDULEID)}
                  className="mt-2 text-blue-600 hover:text-blue-800"
                >
                  Retry
                </button>
              </div>
            )}

            <div className="mt-6 flex justify-end">
              <button
                onClick={() => setShowEnrollModal(false)}
                className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteModal && deleteData && (
        <div className="fixed inset-0 overflow-y-auto z-50">
          {/* Backdrop with blur and fade animation */}
          <div className={`fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ease-in-out ${
            modalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
          }`}>
            <div className="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
              {/* Modal container with scale and fade animation */}
              <div className={`relative transform overflow-hidden rounded-2xl bg-white text-left shadow-2xl transition-all duration-300 ease-out sm:my-8 sm:w-full sm:max-w-lg ${
                modalAnimation === 'visible' 
                  ? 'scale-100 opacity-100 translate-y-0' 
                  : 'scale-95 opacity-0 translate-y-4'
              }`}>
                {/* Gradient header */}
                <div className="bg-gradient-to-r from-red-50 to-red-100 px-6 py-8">
                  <div className="mx-auto flex items-center justify-center h-16 w-16 rounded-full bg-red-100 shadow-lg mb-4 animate-pulse">
                    <ExclamationTriangleIcon className="h-8 w-8 text-red-600" />
                  </div>
                  <h3 className="text-xl font-semibold text-gray-900 mb-2">
                    {deleteData.title}
                  </h3>
                  <p className="text-sm text-gray-600 leading-relaxed">
                    {deleteData.message}
                  </p>
                </div>
                
                {/* Action buttons */}
                <div className="bg-gray-50 px-6 py-4 flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-3 space-y-3 space-y-reverse sm:space-y-0">
                  <button
                    type="button"
                    onClick={() => {
                      setModalAnimation('hidden');
                      setTimeout(() => {
                        setShowDeleteModal(false);
                        setDeleteData(null);
                      }, 300);
                    }}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-gray-300 shadow-sm text-sm font-medium rounded-xl text-gray-700 bg-white hover:bg-gray-50 hover:border-gray-400 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-all duration-200 ease-in-out transform hover:scale-105"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={confirmDelete}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg"
                  >
                    <TrashIcon className="h-4 w-4 mr-2" />
                    {deleteData.confirmText}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default Schedules; 
