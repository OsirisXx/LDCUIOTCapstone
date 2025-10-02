import React, { useState, useEffect, useRef } from 'react';
import { 
  BuildingOfficeIcon,
  BookOpenIcon,
  CalendarDaysIcon,
  MagnifyingGlassIcon,
  EyeIcon,
  XMarkIcon,
  ClockIcon,
  UserGroupIcon,
  MapPinIcon,
  AcademicCapIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  UsersIcon,
  DocumentArrowUpIcon,
  CloudArrowUpIcon,
  DocumentIcon,
  CheckCircleIcon,
  ArrowDownTrayIcon,
  TrashIcon,
  ExclamationTriangleIcon
} from '@heroicons/react/24/outline';
import toast from 'react-hot-toast';
import axios from 'axios';

// Set up axios defaults
axios.defaults.baseURL = 'http://localhost:5000';

function UnifiedManagement() {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [sortBy, setSortBy] = useState('rooms'); // 'rooms', 'subjects', 'schedules'
  const [selectedAcademicYear, setSelectedAcademicYear] = useState('');
  const [selectedSemester, setSelectedSemester] = useState('');
  const [searchTerm, setSearchTerm] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [modalData, setModalData] = useState(null);
  const [modalType, setModalType] = useState(''); // 'room', 'subject', 'schedule'
  const [expandedCards, setExpandedCards] = useState(new Set());
  
  // Bulk operations states
  const [selectedItems, setSelectedItems] = useState(new Set());
  const [selectAll, setSelectAll] = useState(false);
  const [showBulkDeleteModal, setShowBulkDeleteModal] = useState(false);
  const [bulkDeleteType, setBulkDeleteType] = useState('');
  const [isBulkDeleting, setIsBulkDeleting] = useState(false);
  
  // PDF Import states
  const [showImportSection, setShowImportSection] = useState(false);
  const [pdfFile, setPdfFile] = useState(null);
  const [dragActive, setDragActive] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [isImporting, setIsImporting] = useState(false);
  const [parsedData, setParsedData] = useState(null);
  const [importResults, setImportResults] = useState(null);
  const [showPreview, setShowPreview] = useState(false);
  const [showDataModal, setShowDataModal] = useState(false);
  const [activeTab, setActiveTab] = useState('subjects');
  const [importOptions, setImportOptions] = useState({
    updateExisting: false,
    createMissingRooms: true,
    skipDuplicates: true
  });
  
  const fileInputRef = useRef(null);
  
  // Delete functionality states
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleteItem, setDeleteItem] = useState(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [modalAnimation, setModalAnimation] = useState('hidden');
  
  // TEMPORARY: Schedule editing states
  const [showEditModal, setShowEditModal] = useState(false);
  const [editingSchedule, setEditingSchedule] = useState(null);
  const [editFormData, setEditFormData] = useState({
    subject_id: '',
    room_id: '',
    day_of_week: '',
    start_time: '',
    end_time: '',
    academic_year: '',
    semester: ''
  });

  useEffect(() => {
    fetchData();
  }, [selectedAcademicYear, selectedSemester]);

  const fetchData = async () => {
    try {
      setLoading(true);
      const token = localStorage.getItem('token');
      
      if (!token) {
        toast.error('Please login to access data');
        return;
      }

      const headers = { Authorization: `Bearer ${token}` };
      const params = {};
      
      if (selectedAcademicYear) params.academic_year = selectedAcademicYear;
      if (selectedSemester) params.semester = selectedSemester;

      const response = await axios.get('/api/unified/data', { 
        headers,
        params
      });
      
      setData(response.data);
      
      // Set default academic year if not selected
      if (!selectedAcademicYear && response.data.filters.academic_years.length > 0) {
        setSelectedAcademicYear(response.data.filters.academic_years[0]);
      }

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

  const fetchDetailedData = async (id, type) => {
    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };
      
      let endpoint;
      switch (type) {
        case 'room':
          endpoint = `/api/unified/room/${id}`;
          break;
        case 'subject':
          endpoint = `/api/unified/subject/${id}`;
          break;
        case 'schedule':
          endpoint = `/api/unified/schedule/${id}`;
          break;
        default:
          throw new Error('Invalid type');
      }

      const params = {};
      if (selectedAcademicYear) params.academic_year = selectedAcademicYear;
      if (selectedSemester) params.semester = selectedSemester;

      const response = await axios.get(endpoint, { headers, params });
      
      setModalData(response.data);
      setModalType(type);
      setShowModal(true);
    } catch (error) {
      console.error('Error fetching detailed data:', error);
      toast.error('Failed to load detailed information');
    }
  };

  const toggleCardExpansion = (id) => {
    setExpandedCards(prev => {
      const newSet = new Set(prev);
      if (newSet.has(id)) {
        newSet.delete(id);
      } else {
        newSet.add(id);
      }
      return newSet;
    });
  };

  // Delete functionality
  const handleDelete = (item, type) => {
    let title, message, confirmText;
    
    switch (type) {
      case 'room':
        title = 'Delete Room';
        message = `Are you sure you want to delete "${item.ROOMNUMBER} - ${item.ROOMNAME}"? This action cannot be undone.`;
        confirmText = 'Delete Room';
        break;
      case 'subject':
        title = 'Delete Subject';
        message = `Are you sure you want to delete "${item.SUBJECTCODE} - ${item.SUBJECTNAME}"? This will also remove all related schedules and enrollments. This action cannot be undone.`;
        confirmText = 'Delete Subject';
        break;
      case 'schedule':
        title = 'Delete Schedule';
        message = `Are you sure you want to delete this schedule? This action cannot be undone.`;
        confirmText = 'Delete Schedule';
        break;
      default:
        title = 'Delete Item';
        message = 'Are you sure you want to delete this item? This action cannot be undone.';
        confirmText = 'Delete';
    }

    setDeleteItem({
      ...item,
      type,
      title,
      message,
      confirmText
    });
    setShowDeleteModal(true);
    setTimeout(() => setModalAnimation('visible'), 10);
  };

  const confirmDelete = async () => {
    if (!deleteItem) return;

    setIsDeleting(true);
    
    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };
      
      let endpoint;
      let successMessage;
      
      switch (deleteItem.type) {
        case 'room':
          endpoint = `/api/rooms/${deleteItem.ROOMID}`;
          successMessage = 'Room deleted successfully!';
          break;
        case 'subject':
          endpoint = `/api/subjects/${deleteItem.SUBJECTID}`;
          successMessage = 'Subject deleted successfully!';
          break;
        case 'schedule':
          endpoint = `/api/schedules/${deleteItem.SCHEDULEID}`;
          successMessage = 'Schedule deleted successfully!';
          break;
        default:
          throw new Error('Invalid delete type');
      }

      const response = await axios.delete(endpoint, { headers });
      
      // Show enhanced success message
      const message = response.data.message || successMessage;
      toast.success(message, {
        duration: 4000,
        style: {
          background: '#f0fdf4',
          border: '1px solid #bbf7d0',
          color: '#166534',
        },
      });
      
      // Refresh data
      await fetchData();
      
    } catch (error) {
      console.error('Error deleting item:', error);
      const errorMessage = error.response?.data?.message || 'Failed to delete item';
      const errorDetails = error.response?.data?.details;
      
      // Show detailed error message
      if (errorDetails) {
        toast.error(`${errorMessage}: ${errorDetails}`, {
          duration: 8000,
          style: {
            background: '#fef2f2',
            border: '1px solid #fecaca',
            color: '#dc2626',
          },
        });
      } else {
        toast.error(errorMessage);
      }
    } finally {
      setIsDeleting(false);
      setModalAnimation('hidden');
      setTimeout(() => {
        setShowDeleteModal(false);
        setDeleteItem(null);
      }, 300);
    }
  };

  // Bulk Delete Functions
  const handleSelectItem = (itemId) => {
    const newSelectedItems = new Set(selectedItems);
    if (newSelectedItems.has(itemId)) {
      newSelectedItems.delete(itemId);
    } else {
      newSelectedItems.add(itemId);
    }
    setSelectedItems(newSelectedItems);
    
    // Update select all state
    const currentItems = getCurrentItems();
    setSelectAll(newSelectedItems.size === currentItems.length && Array.isArray(currentItems) && currentItems.length > 0);
  };

  const handleSelectAll = () => {
    const currentItems = getCurrentItems();
    
    if (!Array.isArray(currentItems) || currentItems.length === 0) {
      return;
    }
    
    if (selectAll) {
      setSelectedItems(new Set());
    } else {
      try {
        const itemIds = currentItems.map(item => getItemId(item));
        setSelectedItems(new Set(itemIds));
      } catch (error) {
        console.error('Error mapping items:', error);
        return;
      }
    }
    setSelectAll(!selectAll);
  };

  const getCurrentItems = () => {
    if (!data) return [];
    
    switch (sortBy) {
      case 'rooms':
        return Array.isArray(data.rooms?.data) ? data.rooms.data : [];
      case 'subjects':
        return Array.isArray(data.subjects?.data) ? data.subjects.data : [];
      case 'schedules':
        return Array.isArray(data.schedules?.data) ? data.schedules.data : [];
      default:
        return [];
    }
  };

  const getItemId = (item) => {
    switch (sortBy) {
      case 'rooms':
        return item.ROOMID;
      case 'subjects':
        return item.SUBJECTID;
      case 'schedules':
        return item.SCHEDULEID;
      default:
        return item.id;
    }
  };

  const handleBulkDelete = () => {
    if (selectedItems.size === 0) return;
    
    setBulkDeleteType(sortBy);
    setShowBulkDeleteModal(true);
    setTimeout(() => setModalAnimation('visible'), 10);
  };

  const confirmBulkDelete = async () => {
    if (selectedItems.size === 0) return;

    setIsBulkDeleting(true);
    
    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };
      
      let endpoint;
      let successMessage;
      
      switch (bulkDeleteType) {
        case 'rooms':
          endpoint = '/api/rooms/bulk-delete';
          successMessage = `${selectedItems.size} rooms deleted successfully!`;
          break;
        case 'subjects':
          endpoint = '/api/subjects/bulk-delete';
          successMessage = `${selectedItems.size} subjects deleted successfully!`;
          break;
        case 'schedules':
          endpoint = '/api/schedules/bulk-delete';
          successMessage = `${selectedItems.size} schedules deleted successfully!`;
          break;
        default:
          throw new Error('Invalid bulk delete type');
      }

      const response = await axios.delete(endpoint, {
        headers,
        data: { ids: Array.from(selectedItems) }
      });
      
      const message = response.data.message || successMessage;
      toast.success(message, {
        duration: 4000,
        style: {
          background: '#f0fdf4',
          border: '1px solid #bbf7d0',
          color: '#166534',
        },
      });
      
      // Clear selections and refresh data
      setSelectedItems(new Set());
      setSelectAll(false);
      await fetchData();
      
    } catch (error) {
      console.error('Error bulk deleting items:', error);
      const errorMessage = error.response?.data?.message || 'Failed to delete items';
      const errorDetails = error.response?.data?.details;
      
      if (errorDetails) {
        toast.error(`${errorMessage}: ${errorDetails}`, {
          duration: 8000,
          style: {
            background: '#fef2f2',
            border: '1px solid #fecaca',
            color: '#dc2626',
          },
        });
      } else {
        toast.error(errorMessage);
      }
    } finally {
      setIsBulkDeleting(false);
      setModalAnimation('hidden');
      setTimeout(() => {
        setShowBulkDeleteModal(false);
        setBulkDeleteType('');
      }, 300);
    }
  };

  // PDF Import Functions
  const handleDrag = (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.type === 'dragenter' || e.type === 'dragover') {
      setDragActive(true);
    } else if (e.type === 'dragleave') {
      setDragActive(false);
    }
  };

  const handleDrop = (e) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);
    
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      const droppedFile = e.dataTransfer.files[0];
      if (droppedFile.type === 'application/pdf') {
        setPdfFile(droppedFile);
      } else {
        toast.error('Please upload a PDF file');
      }
    }
  };

  const handleFileSelect = (e) => {
    if (e.target.files && e.target.files[0]) {
      setPdfFile(e.target.files[0]);
    }
  };

  const handleUploadAndPreview = async () => {
    if (!pdfFile) {
      toast.error('Please select a PDF file');
      return;
    }

    setIsUploading(true);
    setParsedData(null);
    setImportResults(null);

    try {
      const formData = new FormData();
      formData.append('pdf', pdfFile);

      const response = await axios.post('/api/import/preview', formData, {
        headers: {
          'Content-Type': 'multipart/form-data',
          Authorization: `Bearer ${localStorage.getItem('token')}`
        }
      });

      setParsedData(response.data.data);
      setShowPreview(true);
      toast.success('PDF parsed successfully! Review the data before importing.');

    } catch (error) {
      console.error('Upload error:', error);
      if (error.response?.data?.message) {
        toast.error(error.response.data.message);
      } else {
        toast.error('Failed to parse PDF');
      }
    } finally {
      setIsUploading(false);
    }
  };

  const handleImport = async () => {
    if (!parsedData) {
      toast.error('No data to import');
      return;
    }

    setIsImporting(true);

    try {
      const response = await axios.post('/api/import/execute', {
        parsedData,
        options: importOptions
      }, {
        headers: {
          Authorization: `Bearer ${localStorage.getItem('token')}`
        }
      });

      setImportResults(response.data.results);
      toast.success('Data imported successfully! Refreshing data...');
      
      // Refresh the main data after import
      await fetchData();
      
      // Reset import form
      resetImportForm();

    } catch (error) {
      console.error('Import error:', error);
      if (error.response?.data?.message) {
        toast.error(error.response.data.message);
      } else {
        toast.error('Failed to import data');
      }
    } finally {
      setIsImporting(false);
    }
  };

  const resetImportForm = () => {
    setPdfFile(null);
    setParsedData(null);
    setImportResults(null);
    setShowPreview(false);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  // Delete functionality
  const handleDeleteClick = (item, type) => {
    setDeleteItem({ ...item, type });
    setShowDeleteModal(true);
  };

  const handleDeleteConfirm = async () => {
    if (!deleteItem) return;

    setIsDeleting(true);
    try {
      let endpoint = '';
      let idField = '';

      switch (deleteItem.type) {
        case 'room':
          endpoint = '/api/rooms';
          idField = 'roomId';
          break;
        case 'subject':
          endpoint = '/api/subjects';
          idField = 'subjectId';
          break;
        case 'schedule':
          endpoint = '/api/schedules';
          idField = 'scheduleId';
          break;
        default:
          throw new Error('Invalid delete type');
      }

      await axios.delete(`${endpoint}/${deleteItem[idField]}`, {
        headers: {
          Authorization: `Bearer ${localStorage.getItem('token')}`
        }
      });

      toast.success(`${deleteItem.type.charAt(0).toUpperCase() + deleteItem.type.slice(1)} deleted successfully!`);
      
      // Refresh data
      await fetchData();
      
      // Close modal
      setShowDeleteModal(false);
      setDeleteItem(null);

    } catch (error) {
      console.error('Delete error:', error);
      if (error.response?.data?.message) {
        toast.error(error.response.data.message);
      } else {
        toast.error(`Failed to delete ${deleteItem.type}`);
      }
    } finally {
      setIsDeleting(false);
    }
  };

  const handleDeleteCancel = () => {
    setShowDeleteModal(false);
    setDeleteItem(null);
  };

  // TEMPORARY: Schedule editing functions
  const handleEditSchedule = (schedule) => {
    setEditingSchedule(schedule);
    setEditFormData({
      subject_id: schedule.SUBJECTID || '',
      room_id: schedule.ROOMID || '',
      day_of_week: schedule.DAYOFWEEK || '',
      start_time: schedule.STARTTIME ? schedule.STARTTIME.substring(0, 5) : '',
      end_time: schedule.ENDTIME ? schedule.ENDTIME.substring(0, 5) : '',
      academic_year: schedule.ACADEMICYEAR || '',
      semester: schedule.SEMESTER || ''
    });
    setShowEditModal(true);
  };

  const handleEditSubmit = async (e) => {
    e.preventDefault();
    
    if (!editingSchedule) return;

    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      const updateData = {
        subject_id: editFormData.subject_id,
        room_id: editFormData.room_id,
        day_of_week: editFormData.day_of_week,
        start_time: editFormData.start_time,
        end_time: editFormData.end_time,
        academic_year: editFormData.academic_year,
        semester: editFormData.semester
      };

      await axios.put(`/api/schedules/${editingSchedule.SCHEDULEID}`, updateData, { headers });
      toast.success('Schedule updated successfully!');
      
      // Refresh data
      await fetchData();
      
      // Close modal
      setShowEditModal(false);
      setEditingSchedule(null);
      setEditFormData({
        subject_id: '',
        room_id: '',
        day_of_week: '',
        start_time: '',
        end_time: '',
        academic_year: '',
        semester: ''
      });
    } catch (error) {
      console.error('Error updating schedule:', error);
      if (error.response?.data?.message) {
        toast.error(error.response.data.message);
      } else {
        toast.error('Failed to update schedule');
      }
    }
  };

  const formatTime = (time) => {
    if (!time) return '';
    const [hours, minutes] = time.split(':');
    const hour = parseInt(hours);
    const ampm = hour >= 12 ? 'PM' : 'AM';
    const displayHour = hour % 12 || 12;
    return `${displayHour}:${minutes} ${ampm}`;
  };

  const filterData = (items, searchTerm) => {
    if (!Array.isArray(items)) return [];
    if (!searchTerm) return items;
    
    return items.filter(item => {
      const searchLower = searchTerm.toLowerCase();
      
      switch (sortBy) {
        case 'rooms':
          return (
            item.ROOMNUMBER?.toLowerCase().includes(searchLower) ||
            item.ROOMNAME?.toLowerCase().includes(searchLower) ||
            item.BUILDING?.toLowerCase().includes(searchLower)
          );
        case 'subjects':
          return (
            item.SUBJECTCODE?.toLowerCase().includes(searchLower) ||
            item.SUBJECTNAME?.toLowerCase().includes(searchLower) ||
            item.instructor_name?.toLowerCase().includes(searchLower)
          );
        case 'schedules':
          return (
            item.SUBJECTCODE?.toLowerCase().includes(searchLower) ||
            item.SUBJECTNAME?.toLowerCase().includes(searchLower) ||
            item.instructor_name?.toLowerCase().includes(searchLower) ||
            item.ROOMNUMBER?.toLowerCase().includes(searchLower) ||
            item.DAYOFWEEK?.toLowerCase().includes(searchLower)
          );
        default:
          return true;
      }
    });
  };

  const getCurrentData = () => {
    if (!data) return [];
    
    switch (sortBy) {
      case 'rooms':
        return data.rooms.data || [];
      case 'subjects':
        return data.subjects.data || [];
      case 'schedules':
        return data.schedules.data || [];
      default:
        return [];
    }
  };

  const filteredData = filterData(getCurrentData(), searchTerm);

  const getSortIcon = (type) => {
    switch (type) {
      case 'rooms':
        return BuildingOfficeIcon;
      case 'subjects':
        return BookOpenIcon;
      case 'schedules':
        return CalendarDaysIcon;
      default:
        return BuildingOfficeIcon;
    }
  };

  const renderRoomCard = (room) => {
    const isExpanded = expandedCards.has(room.ROOMID);
    const schedules = data?.rooms?.schedules?.[room.ROOMID] || [];

    return (
      <div key={room.ROOMID} className="bg-white rounded-xl shadow border-2 border-gray-300 p-6 hover:shadow-md hover:border-gray-400 transition">
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-3 mb-4">
          <div className="flex items-start space-x-3">
            <input
              type="checkbox"
              checked={selectedItems.has(room.ROOMID)}
              onChange={() => handleSelectItem(room.ROOMID)}
              className="mt-1 h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <div className="text-2xl">🏫</div>
            <div>
              <h3 className="text-lg font-semibold text-gray-900">{room.ROOMNUMBER}</h3>
              <p className="text-sm text-gray-600">{room.ROOMNAME}</p>
              <p className="text-xs text-gray-500">{room.BUILDING}</p>
            </div>
          </div>
          <div className="flex space-x-2">
            <button
              onClick={() => toggleCardExpansion(room.ROOMID)}
              className="text-gray-400 hover:text-blue-600 p-1 rounded"
              title={isExpanded ? "Collapse" : "Expand"}
            >
              {isExpanded ? <ChevronUpIcon className="h-4 w-4" /> : <ChevronDownIcon className="h-4 w-4" />}
            </button>
            <button
              onClick={() => fetchDetailedData(room.ROOMID, 'room')}
              className="text-gray-400 hover:text-green-600 p-1 rounded"
              title="View Details"
            >
              <EyeIcon className="h-4 w-4" />
            </button>
            <button
              onClick={() => handleDelete(room, 'room')}
              className="text-gray-400 hover:text-red-600 p-1 rounded"
              title="Delete Room"
            >
              <TrashIcon className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="space-y-3">
          <div className="flex items-center justify-between text-sm">
            <div className="flex items-center text-gray-600">
              <MapPinIcon className="h-4 w-4 mr-2" />
              {room.CAPACITY ? `Capacity: ${room.CAPACITY}` : 'No capacity set'}
            </div>
            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
              room.STATUS === 'Available' ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800'
            }`}>
              {room.STATUS}
            </span>
          </div>

          <div className="flex items-center justify-between text-sm text-gray-600">
            <span>{room.schedule_count || 0} schedules</span>
            <span>{room.subject_count || 0} subjects</span>
          </div>

          {isExpanded && schedules.length > 0 && (
            <div className="mt-4 pt-4 border-t border-gray-200">
              <h4 className="text-sm font-medium text-gray-900 mb-2">Current Schedules</h4>
              <div className="space-y-2 max-h-40 overflow-y-auto">
                {schedules.map((schedule) => (
                  <div key={schedule.SCHEDULEID} className="text-xs bg-gray-50 rounded p-2">
                    <div className="font-medium">{schedule.SUBJECTCODE} - {schedule.SUBJECTNAME}</div>
                    <div className="text-gray-600">
                      {schedule.DAYOFWEEK} • {formatTime(schedule.STARTTIME)} - {formatTime(schedule.ENDTIME)}
                    </div>
                    <div className="text-gray-500">{schedule.instructor_name}</div>
                    <div className="text-blue-600">{schedule.enrolled_students || 0} students enrolled</div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    );
  };

  const renderSubjectCard = (subject) => {
    const isExpanded = expandedCards.has(subject.SUBJECTID);
    const schedules = data?.subjects?.schedules?.[subject.SUBJECTID] || [];

    return (
      <div key={subject.SUBJECTID} className="bg-white rounded-xl shadow border-2 border-gray-300 p-6 hover:shadow-md hover:border-gray-400 transition">
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-3 mb-4">
          <div className="flex items-start space-x-3">
            <input
              type="checkbox"
              checked={selectedItems.has(subject.SUBJECTID)}
              onChange={() => handleSelectItem(subject.SUBJECTID)}
              className="mt-1 h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <div className="text-2xl">📚</div>
            <div>
              <h3 className="text-lg font-semibold text-gray-900">{subject.SUBJECTCODE}</h3>
              <p className="text-sm text-gray-600">{subject.SUBJECTNAME}</p>
              <p className="text-xs text-gray-500">{subject.instructor_name || 'No instructor assigned'}</p>
            </div>
          </div>
          <div className="flex space-x-2">
            <button
              onClick={() => toggleCardExpansion(subject.SUBJECTID)}
              className="text-gray-400 hover:text-blue-600 p-1 rounded"
              title={isExpanded ? "Collapse" : "Expand"}
            >
              {isExpanded ? <ChevronUpIcon className="h-4 w-4" /> : <ChevronDownIcon className="h-4 w-4" />}
            </button>
            <button
              onClick={() => fetchDetailedData(subject.SUBJECTID, 'subject')}
              className="text-gray-400 hover:text-green-600 p-1 rounded"
              title="View Details"
            >
              <EyeIcon className="h-4 w-4" />
            </button>
            <button
              onClick={() => handleDelete(subject, 'subject')}
              className="text-gray-400 hover:text-red-600 p-1 rounded"
              title="Delete Subject"
            >
              <TrashIcon className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="space-y-3">
          <div className="text-xs text-gray-600">
            <div>{subject.SEMESTER} • {subject.ACADEMICYEAR}</div>
            {subject.DESCRIPTION && (
              <div className="mt-1 line-clamp-2">{subject.DESCRIPTION}</div>
            )}
          </div>

          <div className="flex items-center justify-between text-sm text-gray-600">
            <div className="flex items-center">
              <UsersIcon className="h-4 w-4 mr-1" />
              <span>{subject.enrolled_students || 0} students</span>
            </div>
            <div className="flex items-center space-x-4">
              <span>{subject.schedule_count || 0} schedules</span>
              <span>{subject.room_count || 0} rooms</span>
            </div>
          </div>

          {isExpanded && schedules.length > 0 && (
            <div className="mt-4 pt-4 border-t border-gray-200">
              <h4 className="text-sm font-medium text-gray-900 mb-2">Schedule Details</h4>
              <div className="space-y-2 max-h-40 overflow-y-auto">
                {schedules.map((schedule) => (
                  <div key={schedule.SCHEDULEID} className="text-xs bg-gray-50 rounded p-2">
                    <div className="font-medium">{schedule.DAYOFWEEK}</div>
                    <div className="text-gray-600">
                      {formatTime(schedule.STARTTIME)} - {formatTime(schedule.ENDTIME)}
                    </div>
                    <div className="text-gray-500">
                      {schedule.ROOMNUMBER} - {schedule.ROOMNAME} ({schedule.BUILDING})
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    );
  };

  const renderScheduleCard = (schedule) => {
    const isExpanded = expandedCards.has(schedule.SCHEDULEID);

    return (
      <div key={schedule.SCHEDULEID} className="bg-white rounded-xl shadow border-2 border-gray-300 p-6 hover:shadow-md hover:border-gray-400 transition">
        <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-3 mb-4">
          <div className="flex items-start space-x-3">
            <input
              type="checkbox"
              checked={selectedItems.has(schedule.SCHEDULEID)}
              onChange={() => handleSelectItem(schedule.SCHEDULEID)}
              className="mt-1 h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <div className="text-2xl">📅</div>
            <div>
              <h3 className="text-lg font-semibold text-gray-900">{schedule.SUBJECTCODE}</h3>
              <p className="text-sm text-gray-600">{schedule.SUBJECTNAME}</p>
              <p className="text-xs text-gray-500">{schedule.instructor_name}</p>
            </div>
          </div>
          <div className="flex space-x-2">
            <button
              onClick={() => toggleCardExpansion(schedule.SCHEDULEID)}
              className="text-gray-400 hover:text-blue-600 p-1 rounded"
              title={isExpanded ? "Collapse" : "Expand"}
            >
              {isExpanded ? <ChevronUpIcon className="h-4 w-4" /> : <ChevronDownIcon className="h-4 w-4" />}
            </button>
            {/* TEMPORARY: Edit button for schedules */}
            <button
              onClick={() => handleEditSchedule(schedule)}
              className="text-gray-400 hover:text-yellow-600 p-1 rounded"
              title="Edit Schedule (Temporary)"
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
              </svg>
            </button>
            <button
              onClick={() => fetchDetailedData(schedule.SCHEDULEID, 'schedule')}
              className="text-gray-400 hover:text-green-600 p-1 rounded"
              title="View Details"
            >
              <EyeIcon className="h-4 w-4" />
            </button>
            <button
              onClick={() => handleDelete(schedule, 'schedule')}
              className="text-gray-400 hover:text-red-600 p-1 rounded"
              title="Delete Schedule"
            >
              <TrashIcon className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="space-y-3">
          <div className="flex items-center text-sm text-gray-700">
            <ClockIcon className="h-4 w-4 mr-2 text-blue-500" />
            <span className="font-medium">
              {schedule.DAYOFWEEK} • {formatTime(schedule.STARTTIME)} - {formatTime(schedule.ENDTIME)}
            </span>
            {((schedule.ISLAB === 1) || (schedule.ISLAB === '1') || (schedule.ISLAB === true)) ? (
              <span className="ml-2 inline-flex items-center rounded-full bg-purple-100 text-purple-800 px-2 py-0.5 text-xs font-medium">LAB</span>
            ) : (
              <span className="ml-2 inline-flex items-center rounded-full bg-blue-100 text-blue-800 px-2 py-0.5 text-xs font-medium">LEC</span>
            )}
          </div>

          <div className="flex items-center text-sm text-gray-600">
            <MapPinIcon className="h-4 w-4 mr-2" />
            <span>{schedule.ROOMNUMBER} - {schedule.ROOMNAME} ({schedule.BUILDING})</span>
          </div>

          <div className="flex items-center justify-between text-sm text-gray-600">
            <div className="flex items-center">
              <UsersIcon className="h-4 w-4 mr-1" />
              <span>{schedule.enrolled_students || 0} students enrolled</span>
            </div>
            <div className="text-xs text-gray-500">
              {schedule.SEMESTER} • {schedule.ACADEMICYEAR}
            </div>
          </div>

          {isExpanded && (
            <div className="mt-4 pt-4 border-t border-gray-200">
              <div className="grid grid-cols-2 gap-4 text-xs text-gray-600">
                <div>
                  <span className="font-medium">Academic Year:</span> {schedule.ACADEMICYEAR}
                </div>
                <div>
                  <span className="font-medium">Semester:</span> {schedule.SEMESTER}
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    );
  };

  const renderModalContent = () => {
    if (!modalData || !modalType) return null;

    switch (modalType) {
      case 'room':
        return (
          <div className="space-y-6">
            <div>
              <h2 className="text-2xl font-bold text-gray-900">
                {modalData.room.ROOMNUMBER} - {modalData.room.ROOMNAME}
              </h2>
              <p className="text-gray-600">{modalData.room.BUILDING}</p>
              {modalData.room.CAPACITY && (
                <p className="text-sm text-gray-500">Capacity: {modalData.room.CAPACITY} people</p>
              )}
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">
                  Subjects ({modalData.subjects?.length || 0})
                </h3>
                <div className="space-y-3 max-h-64 overflow-y-auto">
                  {modalData.subjects?.map((subject) => (
                    <div key={subject.SUBJECTID} className="bg-gray-50 rounded p-3">
                      <div className="font-medium text-gray-900">{subject.SUBJECTCODE}</div>
                      <div className="text-sm text-gray-600">{subject.SUBJECTNAME}</div>
                      <div className="text-xs text-gray-500">{subject.instructor_name}</div>
                      <div className="text-xs text-blue-600 mt-1">
                        {subject.enrolled_students || 0} students • {subject.schedule_count || 0} schedules
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">
                  Schedules ({modalData.schedules?.length || 0})
                </h3>
                <div className="space-y-3 max-h-64 overflow-y-auto">
                  {modalData.schedules?.map((schedule) => (
                    <div key={schedule.SCHEDULEID} className="bg-gray-50 rounded p-3">
                      <div className="font-medium text-gray-900">{schedule.SUBJECTCODE}</div>
                      <div className="text-sm text-gray-600">
                        {schedule.DAYOFWEEK} • {formatTime(schedule.STARTTIME)} - {formatTime(schedule.ENDTIME)}
                      </div>
                      <div className="text-xs text-gray-500">{schedule.instructor_name}</div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        );

      case 'subject':
        return (
          <div className="space-y-6">
            <div>
              <h2 className="text-2xl font-bold text-gray-900">
                {modalData.subject.SUBJECTCODE} - {modalData.subject.SUBJECTNAME}
              </h2>
              <p className="text-gray-600">{modalData.subject.instructor_name}</p>
              <p className="text-sm text-gray-500">
                {modalData.subject.SEMESTER} • {modalData.subject.ACADEMICYEAR}
              </p>
              {modalData.subject.DESCRIPTION && (
                <p className="text-sm text-gray-600 mt-2">{modalData.subject.DESCRIPTION}</p>
              )}
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">
                  Rooms ({modalData.rooms?.length || 0})
                </h3>
                <div className="space-y-3 max-h-64 overflow-y-auto">
                  {modalData.rooms?.map((room) => (
                    <div key={room.ROOMID} className="bg-gray-50 rounded p-3">
                      <div className="font-medium text-gray-900">{room.ROOMNUMBER}</div>
                      <div className="text-sm text-gray-600">{room.ROOMNAME}</div>
                      <div className="text-xs text-gray-500">{room.BUILDING}</div>
                      <div className="text-xs text-blue-600 mt-1">
                        {room.schedule_count || 0} schedules
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">
                  Students ({modalData.students?.length || 0})
                </h3>
                <div className="space-y-3 max-h-64 overflow-y-auto">
                  {modalData.students?.map((student) => (
                    <div key={student.USERID} className="bg-gray-50 rounded p-3">
                      <div className="font-medium text-gray-900">
                        {student.FIRSTNAME} {student.LASTNAME}
                      </div>
                      <div className="text-sm text-gray-600">{student.STUDENTID}</div>
                      <div className="text-xs text-gray-500">
                        Year {student.YEARLEVEL} • {student.DEPARTMENT}
                      </div>
                      <div className="text-xs text-gray-500">{student.EMAIL}</div>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            {modalData.schedules?.length > 0 && (
              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">
                  Schedule Details ({modalData.schedules.length})
                </h3>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                  {modalData.schedules.map((schedule) => (
                    <div key={schedule.SCHEDULEID} className="bg-gray-50 rounded p-3">
                      <div className="font-medium text-gray-900">{schedule.DAYOFWEEK}</div>
                      <div className="text-sm text-gray-600">
                        {formatTime(schedule.STARTTIME)} - {formatTime(schedule.ENDTIME)}
                      </div>
                      <div className="text-xs text-gray-500">
                        {schedule.ROOMNUMBER} - {schedule.ROOMNAME}
                      </div>
                      <div className="text-xs text-gray-500">{schedule.BUILDING}</div>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        );

      case 'schedule':
        return (
          <div className="space-y-6">
            <div>
              <h2 className="text-2xl font-bold text-gray-900">
                {modalData.schedule.SUBJECTCODE} - {modalData.schedule.SUBJECTNAME}
              </h2>
              <p className="text-gray-600">{modalData.schedule.instructor_name}</p>
              <div className="flex items-center space-x-4 text-sm text-gray-600 mt-2">
                <span>{modalData.schedule.DAYOFWEEK}</span>
                <span>{formatTime(modalData.schedule.STARTTIME)} - {formatTime(modalData.schedule.ENDTIME)}</span>
                <span>{modalData.schedule.ROOMNUMBER} - {modalData.schedule.ROOMNAME}</span>
              </div>
              <p className="text-sm text-gray-500">
                {modalData.schedule.SEMESTER} • {modalData.schedule.ACADEMICYEAR}
              </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">Room Details</h3>
                <div className="bg-gray-50 rounded p-4">
                  <div className="font-medium text-gray-900">{modalData.schedule.ROOMNUMBER}</div>
                  <div className="text-sm text-gray-600">{modalData.schedule.ROOMNAME}</div>
                  <div className="text-xs text-gray-500">{modalData.schedule.BUILDING}</div>
                  {modalData.schedule.CAPACITY && (
                    <div className="text-xs text-gray-500 mt-1">
                      Capacity: {modalData.schedule.CAPACITY} people
                    </div>
                  )}
                </div>
              </div>

              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">
                  Enrolled Students ({modalData.students?.length || 0})
                </h3>
                <div className="space-y-2 max-h-64 overflow-y-auto">
                  {modalData.students?.map((student) => (
                    <div key={student.USERID} className="bg-gray-50 rounded p-3">
                      <div className="font-medium text-gray-900">
                        {student.FIRSTNAME} {student.LASTNAME}
                      </div>
                      <div className="text-sm text-gray-600">{student.STUDENTID}</div>
                      <div className="text-xs text-gray-500">
                        Year {student.YEARLEVEL} • {student.DEPARTMENT}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            {modalData.schedule.subject_description && (
              <div>
                <h3 className="text-lg font-semibold text-gray-900 mb-3">Subject Description</h3>
                <p className="text-sm text-gray-600 bg-gray-50 rounded p-4">
                  {modalData.schedule.subject_description}
                </p>
              </div>
            )}
          </div>
        );

      default:
        return null;
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  return (
    <div className="space-y-8">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Academic Management</h1>
        <p className="text-gray-600">Unified view of rooms, subjects, and schedules</p>
      </div>

      {/* PDF Import Section */}
      <div className="bg-gradient-to-r from-blue-50 to-indigo-50 border-2 border-blue-200 rounded-xl p-5">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="flex items-center">
            <DocumentArrowUpIcon className="h-6 w-6 text-blue-600 mr-3" />
            <div>
              <h3 className="text-lg font-semibold text-blue-900">PDF Import</h3>
              <p className="text-sm text-blue-700">Import class lists from PDF documents</p>
            </div>
          </div>
          <button
            onClick={() => setShowImportSection(!showImportSection)}
            className="inline-flex items-center px-4 py-2 border border-blue-300 rounded-md shadow-sm text-sm font-medium text-blue-700 bg-white hover:bg-blue-50 transition-colors"
          >
            {showImportSection ? 'Hide Import' : 'Show Import'}
          </button>
        </div>

        {showImportSection && (
          <div className="mt-6 space-y-6">
            {/* Upload Section */}
            <div className="bg-white rounded-xl border-2 border-gray-200 p-6">
              <h4 className="text-md font-semibold text-gray-900 mb-4">Upload Class List PDF</h4>
              
              {!pdfFile && (
                <div
                  className={`relative border-2 border-dashed rounded-lg p-6 text-center transition-colors ${
                    dragActive 
                      ? 'border-blue-400 bg-blue-50' 
                      : 'border-gray-300 hover:border-gray-400'
                  }`}
                  onDragEnter={handleDrag}
                  onDragLeave={handleDrag}
                  onDragOver={handleDrag}
                  onDrop={handleDrop}
                >
                  <CloudArrowUpIcon className="mx-auto h-10 w-10 text-gray-400" />
                  <div className="mt-3">
                    <label htmlFor="pdf-upload" className="cursor-pointer">
                      <span className="text-sm font-medium text-blue-600 hover:text-blue-500">
                        Click to upload
                      </span>
                      <span className="text-sm text-gray-600"> or drag and drop</span>
                      <input
                        id="pdf-upload"
                        ref={fileInputRef}
                        type="file"
                        accept=".pdf"
                        onChange={handleFileSelect}
                        className="sr-only"
                      />
                    </label>
                  </div>
                  <p className="text-xs text-gray-500">PDF files only, up to 10MB</p>
                </div>
              )}

              {pdfFile && (
                <div className="bg-gray-50 rounded-lg border-2 border-gray-200 p-4 mb-4">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center">
                      <DocumentIcon className="h-6 w-6 text-red-600 mr-3" />
                      <div>
                        <div className="font-medium text-gray-900">{pdfFile.name}</div>
                        <div className="text-sm text-gray-500">
                          {(pdfFile.size / 1024 / 1024).toFixed(2)} MB
                        </div>
                      </div>
                    </div>
                    <button
                      onClick={resetImportForm}
                      className="text-gray-400 hover:text-gray-600"
                    >
                      <XMarkIcon className="h-5 w-5" />
                    </button>
                  </div>
                </div>
              )}

              {pdfFile && !parsedData && (
                <div className="flex justify-end">
                  <button
                    onClick={handleUploadAndPreview}
                    disabled={isUploading}
                    className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-50"
                  >
                    {isUploading ? (
                      <>
                        <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                        Parsing PDF...
                      </>
                    ) : (
                      <>
                        <EyeIcon className="h-4 w-4 mr-2" />
                        Parse & Preview
                      </>
                    )}
                  </button>
                </div>
              )}
            </div>

            {/* Import Options */}
            {parsedData && !importResults && (
              <div className="bg-white rounded-xl border-2 border-gray-200 p-6">
                <h4 className="text-md font-semibold text-gray-900 mb-4">Import Options</h4>
                <div className="space-y-3">
                  <label className="flex items-center">
                    <input
                      type="checkbox"
                      checked={importOptions.updateExisting}
                      onChange={(e) => setImportOptions({...importOptions, updateExisting: e.target.checked})}
                      className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                    />
                    <span className="ml-2 text-sm text-gray-700">
                      Update existing records (subjects, students, instructors, rooms)
                    </span>
                  </label>
                  <label className="flex items-center">
                    <input
                      type="checkbox"
                      checked={importOptions.createMissingRooms}
                      onChange={(e) => setImportOptions({...importOptions, createMissingRooms: e.target.checked})}
                      className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                    />
                    <span className="ml-2 text-sm text-gray-700">
                      Create missing rooms automatically
                    </span>
                  </label>
                  <label className="flex items-center">
                    <input
                      type="checkbox"
                      checked={importOptions.skipDuplicates}
                      onChange={(e) => setImportOptions({...importOptions, skipDuplicates: e.target.checked})}
                      className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                    />
                    <span className="ml-2 text-sm text-gray-700">
                      Skip duplicate enrollments and schedules
                    </span>
                  </label>
                </div>
              </div>
            )}

            {/* Preview Actions */}
            {parsedData && !importResults && (
              <div className="bg-white rounded-xl border-2 border-gray-200 p-6">
                <div className="flex justify-between items-center mb-4">
                  <div>
                    <h4 className="text-md font-semibold text-gray-900">PDF Parsed Successfully</h4>
                    <p className="text-sm text-gray-600">
                      Found {parsedData.statistics?.total_subjects || 0} subjects, {parsedData.statistics?.total_students || 0} students, 
                      and {parsedData.statistics?.total_instructors || 0} instructors
                    </p>
                  </div>
                  <div className="flex space-x-3">
                    <button
                      onClick={() => {
                        setActiveTab('subjects');
                        setShowDataModal(true);
                      }}
                      className="inline-flex items-center px-4 py-2 border border-blue-300 rounded-md text-sm font-medium text-blue-700 bg-blue-50 hover:bg-blue-100"
                    >
                      <EyeIcon className="h-4 w-4 mr-2" />
                      View Uploaded Data
                    </button>
                    <button
                      onClick={handleImport}
                      disabled={isImporting}
                      className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700 disabled:opacity-50"
                    >
                      {isImporting ? (
                        <>
                          <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                          Importing...
                        </>
                      ) : (
                        <>
                          <ArrowDownTrayIcon className="h-4 w-4 mr-2" />
                          Import Data
                        </>
                      )}
                    </button>
                  </div>
                </div>
              </div>
            )}

            {/* Import Results */}
            {importResults && (
              <div className="bg-green-50 border-2 border-green-200 rounded-xl p-6">
                <div className="flex items-center mb-4">
                  <CheckCircleIcon className="h-6 w-6 text-green-600 mr-2" />
                  <h4 className="text-md font-semibold text-green-900">Import Completed Successfully</h4>
                </div>

                <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                  {Object.entries(importResults).map(([key, result]) => (
                    <div key={key} className="bg-white rounded p-3 text-sm">
                      <div className="font-medium text-gray-900 capitalize">{key}</div>
                      <div className="text-green-600">Created: {result.created}</div>
                      <div className="text-blue-600">Updated: {result.updated}</div>
                      {result.errors?.length > 0 && (
                        <div className="text-red-600">Errors: {result.errors.length}</div>
                      )}
                    </div>
                  ))}
                </div>

                <div className="mt-4 flex justify-end">
                  <button
                    onClick={() => {
                      setImportResults(null);
                      setShowImportSection(false);
                    }}
                    className="inline-flex items-center px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                  >
                    Close
                  </button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Filters and Controls */}
      <div className="bg-white p-6 rounded-xl shadow border-2 border-gray-200">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4">
          {/* Sort By */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Sort by</label>
            <div className="flex flex-wrap gap-2">
              {[
                { key: 'rooms', label: 'Rooms', icon: BuildingOfficeIcon },
                { key: 'subjects', label: 'Subjects', icon: BookOpenIcon },
                { key: 'schedules', label: 'Schedules', icon: CalendarDaysIcon }
              ].map(({ key, label, icon: Icon }) => (
                <button
                  key={key}
                  onClick={() => {
                    setSortBy(key);
                    setSearchTerm('');
                    setExpandedCards(new Set());
                  }}
                  className={`shrink-0 flex items-center px-3 py-2 text-sm font-medium rounded-md border transition-colors ${
                    sortBy === key
                      ? 'bg-blue-600 text-white border-blue-600'
                      : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50'
                  }`}
                >
                  <Icon className="h-4 w-4 mr-2" />
                  {label}
                </button>
              ))}
            </div>
          </div>

          {/* Academic Year Filter */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Academic Year</label>
            <select
              value={selectedAcademicYear}
              onChange={(e) => setSelectedAcademicYear(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
            >
              <option value="">All Academic Years</option>
              {data?.filters?.academic_years?.map(year => (
                <option key={year} value={year}>{year}</option>
              ))}
            </select>
          </div>

          {/* Semester Filter */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Semester</label>
            <select
              value={selectedSemester}
              onChange={(e) => setSelectedSemester(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm"
            >
              <option value="">All Semesters</option>
              {data?.filters?.semesters?.map(semester => (
                <option key={semester} value={semester}>{semester}</option>
              ))}
            </select>
          </div>

          {/* Search */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Search</label>
            <div className="relative">
              <MagnifyingGlassIcon className="h-5 w-5 absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder={`Search ${sortBy}...`}
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="w-full pl-10 pr-3 py-2 border-2 border-gray-300 rounded-md text-sm"
              />
            </div>
          </div>
        </div>

        {/* Bulk Operations Toolbar */}
        {selectedItems.size > 0 && (
          <div className="bg-blue-50 border-2 border-blue-200 rounded-xl p-4 mb-4">
            <div className="flex items-center justify-between">
              <div className="flex items-center space-x-4">
                <div className="flex items-center space-x-2">
                  <div className="w-8 h-8 bg-blue-100 rounded-full flex items-center justify-center">
                    <span className="text-sm font-medium text-blue-600">{selectedItems.size}</span>
                  </div>
                  <span className="text-sm font-medium text-blue-900">
                    {selectedItems.size} {sortBy} selected
                  </span>
                </div>
                <button
                  onClick={() => {
                    setSelectedItems(new Set());
                    setSelectAll(false);
                  }}
                  className="text-sm text-blue-600 hover:text-blue-800 underline"
                >
                  Clear selection
                </button>
              </div>
              <div className="flex items-center space-x-2">
                <button
                  onClick={handleBulkDelete}
                  disabled={selectedItems.size === 0}
                  className="inline-flex items-center px-3 py-2 border border-red-300 shadow-sm text-sm font-medium rounded-md text-red-700 bg-white hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <TrashIcon className="h-4 w-4 mr-2" />
                  Delete Selected
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Statistics */}
        {data?.statistics && (
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 pt-4 border-t border-gray-200">
            <div className="text-center bg-white rounded-lg border-2 border-gray-200 p-4">
              <div className="text-2xl font-bold text-blue-600">{data.statistics.total_rooms}</div>
              <div className="text-sm text-gray-600">Total Rooms</div>
            </div>
            <div className="text-center bg-white rounded-lg border-2 border-gray-200 p-4">
              <div className="text-2xl font-bold text-green-600">{data.statistics.total_subjects}</div>
              <div className="text-sm text-gray-600">Total Subjects</div>
            </div>
            <div className="text-center bg-white rounded-lg border-2 border-gray-200 p-4">
              <div className="text-2xl font-bold text-purple-600">{data.statistics.total_schedules}</div>
              <div className="text-sm text-gray-600">Total Schedules</div>
            </div>
            <div className="text-center bg-white rounded-lg border-2 border-gray-200 p-4">
              <div className="text-2xl font-bold text-orange-600">{data.statistics.total_enrolled_students}</div>
              <div className="text-sm text-gray-600">Total Enrollments</div>
            </div>
          </div>
        )}
      </div>

      {/* Data Grid */}
      {filteredData.length > 0 && (
        <div className="flex flex-wrap items-center justify-between gap-2 mb-4">
          <div className="flex items-center space-x-3">
            <input
              type="checkbox"
              checked={selectAll}
              onChange={handleSelectAll}
              className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <span className="text-sm font-medium text-gray-700">
              Select All ({filteredData.length} {sortBy})
            </span>
          </div>
          <div className="text-sm text-gray-500">
            {selectedItems.size} selected
          </div>
        </div>
      )}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {Array.isArray(filteredData) ? filteredData.map((item) => {
          switch (sortBy) {
            case 'rooms':
              return renderRoomCard(item);
            case 'subjects':
              return renderSubjectCard(item);
            case 'schedules':
              return renderScheduleCard(item);
            default:
              return null;
          }
        }) : []}
      </div>

      {filteredData.length === 0 && !loading && (
        <div className="text-center py-12">
          {React.createElement(getSortIcon(sortBy), { className: "mx-auto h-12 w-12 text-gray-400" })}
          <h3 className="mt-2 text-sm font-medium text-gray-900">No {sortBy} found</h3>
          <p className="mt-1 text-sm text-gray-500">
            {searchTerm || selectedAcademicYear || selectedSemester 
              ? 'Try adjusting your search filters.' 
              : `No ${sortBy} available for the current filters.`}
          </p>
        </div>
      )}

      {/* Detail Modal */}
      {showModal && modalData && (
        <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
          <div className="relative top-10 mx-auto p-6 border max-w-6xl shadow-lg rounded-md bg-white my-8">
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-xl font-semibold text-gray-900">
                {modalType.charAt(0).toUpperCase() + modalType.slice(1)} Details
              </h2>
              <button
                onClick={() => {
                  setShowModal(false);
                  setModalData(null);
                  setModalType('');
                }}
                className="text-gray-400 hover:text-gray-600"
              >
                <XMarkIcon className="h-6 w-6" />
              </button>
            </div>

            <div className="max-h-[70vh] overflow-y-auto">
              {renderModalContent()}
            </div>

            <div className="mt-6 flex justify-end">
              <button
                onClick={() => {
                  setShowModal(false);
                  setModalData(null);
                  setModalType('');
                }}
                className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Data Preview Modal */}
      {showDataModal && parsedData && (
        <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
          <div className="relative top-20 mx-auto p-5 border w-11/12 max-w-6xl shadow-lg rounded-md bg-white">
            <div className="mt-3">
              {/* Modal Header */}
              <div className="flex justify-between items-center mb-6">
                <h3 className="text-lg font-semibold text-gray-900">PDF Data Preview</h3>
                <button
                  onClick={() => setShowDataModal(false)}
                  className="text-gray-400 hover:text-gray-600"
                >
                  <XMarkIcon className="h-6 w-6" />
                </button>
              </div>

              {/* Statistics */}
              {parsedData?.statistics && (
                <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4 mb-6">
                  {[
                    { label: 'Subjects', value: parsedData.statistics.total_subjects, color: 'bg-blue-100 text-blue-800' },
                    { label: 'Students', value: parsedData.statistics.total_students, color: 'bg-green-100 text-green-800' },
                    { label: 'Instructors', value: parsedData.statistics.total_instructors, color: 'bg-purple-100 text-purple-800' },
                    { label: 'Rooms', value: parsedData.statistics.total_rooms, color: 'bg-orange-100 text-orange-800' },
                    { label: 'Schedules', value: parsedData.statistics.total_schedules, color: 'bg-indigo-100 text-indigo-800' },
                    { label: 'Enrollments', value: parsedData.statistics.total_enrollments, color: 'bg-pink-100 text-pink-800' }
                  ].map((stat, index) => (
                    <div key={index} className="bg-gray-50 rounded-lg p-4 text-center">
                      <div className={`text-2xl font-bold ${stat.color}`}>{stat.value}</div>
                      <div className="text-sm text-gray-600 mt-1">{stat.label}</div>
                    </div>
                  ))}
                </div>
              )}

              {/* Detailed Data Tabs */}
              <div className="border-b border-gray-200 mb-6">
                <nav className="-mb-px flex space-x-8">
                  {[
                    { key: 'subjects', label: 'Subjects', count: parsedData.subjects?.length || 0 },
                    { key: 'students', label: 'Students', count: parsedData.students?.length || 0 },
                    { key: 'instructors', label: 'Instructors', count: parsedData.instructors?.length || 0 },
                    { key: 'rooms', label: 'Rooms', count: parsedData.rooms?.length || 0 },
                    { key: 'schedules', label: 'Schedules', count: parsedData.schedules?.length || 0 }
                  ].map((tab) => (
                    <button
                      key={tab.key}
                      onClick={() => setActiveTab(tab.key)}
                      className={`py-2 px-1 border-b-2 font-medium text-sm transition-colors ${
                        activeTab === tab.key
                          ? 'border-blue-500 text-blue-600'
                          : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                      }`}
                    >
                      {tab.label} ({tab.count})
                    </button>
                  ))}
                </nav>
              </div>

              {/* Data Content */}
              <div className="max-h-96 overflow-y-auto">
                {/* Subjects Tab */}
                {activeTab === 'subjects' && (
                  <div className="mb-6">
                    <h4 className="text-lg font-semibold text-gray-900 mb-3">Subjects ({parsedData.subjects?.length || 0})</h4>
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                      {parsedData.subjects?.map((subject, index) => (
                        <div key={index} className="bg-gray-50 rounded-lg p-3">
                          <div className="font-medium text-gray-900">{subject.code}</div>
                          <div className="text-sm text-gray-600">{subject.name}</div>
                          <div className="text-xs text-gray-500 mt-1">
                            {subject.section} • {subject.semester} • {subject.academic_year}
                          </div>
                          {subject.instructors?.length > 0 && (
                            <div className="text-xs text-blue-600 mt-1">
                              {subject.instructors.map(inst => inst.name).join(', ')}
                            </div>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Students Tab */}
                {activeTab === 'students' && (
                  <div className="mb-6">
                    <h4 className="text-lg font-semibold text-gray-900 mb-3">Students ({parsedData.students?.length || 0})</h4>
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                      {parsedData.students?.map((student, index) => (
                        <div key={index} className="bg-gray-50 rounded-lg p-3">
                          <div className="font-medium text-gray-900">{student.full_name}</div>
                          <div className="text-sm text-gray-600">{student.student_id}</div>
                          <div className="text-xs text-gray-500">
                            {student.course} • Year {student.year_level} • {student.gender}
                          </div>
                          <div className="text-xs text-gray-500">{student.email}</div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Instructors Tab */}
                {activeTab === 'instructors' && (
                  <div className="mb-6">
                    <h4 className="text-lg font-semibold text-gray-900 mb-3">Instructors ({parsedData.instructors?.length || 0})</h4>
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                      {parsedData.instructors?.map((instructor, index) => (
                        <div key={index} className="bg-gray-50 rounded-lg p-3">
                          <div className="font-medium text-gray-900">{instructor.name}</div>
                          <div className="text-sm text-gray-600">{instructor.email}</div>
                          <div className="text-xs text-gray-500">
                            {instructor.department} • {instructor.position}
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Rooms Tab */}
                {activeTab === 'rooms' && (
                  <div className="mb-6">
                    <h4 className="text-lg font-semibold text-gray-900 mb-3">Rooms ({parsedData.rooms?.length || 0})</h4>
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                      {parsedData.rooms?.map((room, index) => {
                        const roomNumber = room.number || room.room_number || room.ROOMNUMBER || room.code || room.id || '';
                        const roomName = room.name || room.ROOMNAME || '';
                        const building = room.building || room.BUILDING || '';
                        const capacity = room.capacity || room.CAPACITY || '';
                        const schedules = Array.isArray(parsedData.schedules) ? parsedData.schedules : [];
                        const scheduleCount = schedules.filter(s => {
                          const sRoom = s.room_number || s.ROOMNUMBER || s.room || '';
                          return String(sRoom).trim() === String(roomNumber).trim();
                        }).length;
                        return (
                          <div key={index} className="bg-gray-50 rounded-lg p-3">
                            <div className="font-medium text-gray-900">{roomNumber || 'Unknown Room'}</div>
                            {roomName && (
                              <div className="text-sm text-gray-600">{roomName}</div>
                            )}
                            <div className="text-xs text-gray-500">
                              {building || 'N/A'}{capacity ? ` • Capacity: ${capacity}` : ''}
                            </div>
                            <div className="text-xs text-blue-600">
                              {scheduleCount} {scheduleCount === 1 ? 'schedule' : 'schedules'}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                )}

                {/* Schedules Tab */}
                {activeTab === 'schedules' && (
                  <div className="mb-6">
                    <h4 className="text-lg font-semibold text-gray-900 mb-3">Schedules ({parsedData.schedules?.length || 0})</h4>
                    <div className="space-y-2">
                      {parsedData.schedules?.map((schedule, index) => (
                        <div key={index} className="bg-gray-50 rounded-lg p-3">
                          <div className="flex justify-between items-start">
                            <div>
                              <div className="font-medium text-gray-900">{schedule.subject_code}</div>
                              <div className="text-sm text-gray-600">
                                {schedule.day} • {schedule.start_time} - {schedule.end_time}
                              </div>
                              <div className="text-xs text-gray-500">
                                {schedule.room_number} • {schedule.instructor_name}
                              </div>
                            </div>
                            <div className="text-xs text-gray-500">
                              {schedule.semester} • {schedule.academic_year}
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>

              {/* Modal Footer */}
              <div className="flex justify-end space-x-3 mt-6 pt-4 border-t border-gray-200">
                <button
                  onClick={() => setShowDataModal(false)}
                  className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                >
                  Close
                </button>
                <button
                  onClick={() => {
                    setShowDataModal(false);
                    handleImport();
                  }}
                  disabled={isImporting}
                  className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700 disabled:opacity-50"
                >
                  {isImporting ? (
                    <>
                      <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2 inline-block"></div>
                      Importing...
                    </>
                  ) : (
                    <>
                      <ArrowDownTrayIcon className="h-4 w-4 mr-2 inline-block" />
                      Import All Data
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Modern Delete Confirmation Modal */}
      {showDeleteModal && deleteItem && (
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
                    {deleteItem.title}
                  </h3>
                  <p className="text-sm text-gray-600 leading-relaxed">
                    {deleteItem.message}
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
                        setDeleteItem(null);
                      }, 300);
                    }}
                    disabled={isDeleting}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-gray-300 shadow-sm text-sm font-medium rounded-xl text-gray-700 bg-white hover:bg-gray-50 hover:border-gray-400 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-all duration-200 ease-in-out transform hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={confirmDelete}
                    disabled={isDeleting}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {isDeleting ? (
                      <>
                        <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                        Deleting...
                      </>
                    ) : (
                      <>
                        <TrashIcon className="h-4 w-4 mr-2" />
                        {deleteItem.confirmText}
                      </>
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Bulk Delete Modal */}
      {showBulkDeleteModal && (
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
                    Bulk Delete {bulkDeleteType}
                  </h3>
                  <p className="text-sm text-gray-600 leading-relaxed">
                    Are you sure you want to delete <strong>{selectedItems.size} {bulkDeleteType}</strong>? This action cannot be undone and will also remove all related data (schedules, enrollments, etc.).
                  </p>
                </div>
                
                {/* Action buttons */}
                <div className="bg-gray-50 px-6 py-4 flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-3 space-y-3 space-y-reverse sm:space-y-0">
                  <button
                    type="button"
                    onClick={() => {
                      setModalAnimation('hidden');
                      setTimeout(() => {
                        setShowBulkDeleteModal(false);
                        setBulkDeleteType('');
                      }, 300);
                    }}
                    disabled={isBulkDeleting}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-gray-300 shadow-sm text-sm font-medium rounded-xl text-gray-700 bg-white hover:bg-gray-50 hover:border-gray-400 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-all duration-200 ease-in-out transform hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={confirmBulkDelete}
                    disabled={isBulkDeleting}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {isBulkDeleting ? (
                      <>
                        <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                        Deleting...
                      </>
                    ) : (
                      <>
                        <TrashIcon className="h-4 w-4 mr-2" />
                        Delete {selectedItems.size} {bulkDeleteType}
                      </>
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* TEMPORARY: Schedule Edit Modal */}
      {showEditModal && editingSchedule && (
        <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
          <div className="relative top-20 mx-auto p-5 border w-96 shadow-lg rounded-md bg-white">
            <div className="mt-3">
              <div className="flex justify-between items-center mb-4">
                <h3 className="text-lg font-medium text-gray-900">
                  Edit Schedule (TEMPORARY)
                </h3>
                <button
                  onClick={() => {
                    setShowEditModal(false);
                    setEditingSchedule(null);
                    setEditFormData({
                      subject_id: '',
                      room_id: '',
                      day_of_week: '',
                      start_time: '',
                      end_time: '',
                      academic_year: '',
                      semester: ''
                    });
                  }}
                  className="text-gray-400 hover:text-gray-600"
                >
                  <XMarkIcon className="h-6 w-6" />
                </button>
              </div>
              
              <div className="bg-yellow-50 border border-yellow-200 rounded-md p-3 mb-4">
                <div className="flex">
                  <ExclamationTriangleIcon className="h-5 w-5 text-yellow-400" />
                  <div className="ml-3">
                    <p className="text-sm text-yellow-800">
                      <strong>Note:</strong> This is a temporary edit feature for quick schedule modifications.
                    </p>
                  </div>
                </div>
              </div>

              <form onSubmit={handleEditSubmit} className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Subject</label>
                  <input
                    type="text"
                    value={editingSchedule.SUBJECTCODE}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2 bg-gray-50"
                    disabled
                  />
                  <p className="text-xs text-gray-500 mt-1">Subject cannot be changed</p>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Room</label>
                  <select
                    value={editFormData.room_id}
                    onChange={(e) => setEditFormData({ ...editFormData, room_id: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    required
                  >
                    <option value="">Select Room</option>
                    {data?.rooms?.data?.map(room => (
                      <option key={room.ROOMID} value={room.ROOMID}>
                        {room.ROOMNUMBER} - {room.ROOMNAME}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Day of Week</label>
                  <select
                    value={editFormData.day_of_week}
                    onChange={(e) => setEditFormData({ ...editFormData, day_of_week: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    required
                  >
                    <option value="">Select Day</option>
                    <option value="Monday">Monday</option>
                    <option value="Tuesday">Tuesday</option>
                    <option value="Wednesday">Wednesday</option>
                    <option value="Thursday">Thursday</option>
                    <option value="Friday">Friday</option>
                    <option value="Saturday">Saturday</option>
                    <option value="Sunday">Sunday</option>
                  </select>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700">Start Time</label>
                    <input
                      type="time"
                      value={editFormData.start_time}
                      onChange={(e) => setEditFormData({ ...editFormData, start_time: e.target.value })}
                      className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                      required
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700">End Time</label>
                    <input
                      type="time"
                      value={editFormData.end_time}
                      onChange={(e) => setEditFormData({ ...editFormData, end_time: e.target.value })}
                      className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                      required
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Academic Year</label>
                  <input
                    type="text"
                    value={editFormData.academic_year}
                    onChange={(e) => setEditFormData({ ...editFormData, academic_year: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    placeholder="2024-2025"
                    required
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Semester</label>
                  <select
                    value={editFormData.semester}
                    onChange={(e) => setEditFormData({ ...editFormData, semester: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    required
                  >
                    <option value="">Select Semester</option>
                    <option value="First Semester">First Semester</option>
                    <option value="Second Semester">Second Semester</option>
                    <option value="Summer">Summer</option>
                  </select>
                </div>

                <div className="flex justify-end space-x-3 pt-4">
                  <button
                    type="button"
                    onClick={() => {
                      setShowEditModal(false);
                      setEditingSchedule(null);
                      setEditFormData({
                        subject_id: '',
                        room_id: '',
                        day_of_week: '',
                        start_time: '',
                        end_time: '',
                        academic_year: '',
                        semester: ''
                      });
                    }}
                    className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-yellow-600 hover:bg-yellow-700"
                  >
                    Update Schedule
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default UnifiedManagement;
