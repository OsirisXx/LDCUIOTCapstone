import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { 
  PlusIcon, 
  PencilIcon, 
  TrashIcon, 
  MagnifyingGlassIcon,
  UserIcon,
  AcademicCapIcon,
  UserGroupIcon,
  XMarkIcon,
  DocumentArrowUpIcon,
  CheckIcon,
  ExclamationTriangleIcon,
  EllipsisVerticalIcon,
  ChevronDownIcon,
  Squares2X2Icon
} from '@heroicons/react/24/outline';
import axios from 'axios';
import toast from 'react-hot-toast';
import { useAuth } from '../contexts/AuthContext';

function Users() {
  const { user } = useAuth();
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [modalAnimation, setModalAnimation] = useState('hidden');
  const [editingUser, setEditingUser] = useState(null);
  const [searchTerm, setSearchTerm] = useState('');
  const [filterType, setFilterType] = useState('all');
  const [filterStatus, setFilterStatus] = useState('all');
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);

  const [showUploadModal, setShowUploadModal] = useState(false);
  const [uploadModalAnimation, setUploadModalAnimation] = useState('hidden');
  const [uploadFile, setUploadFile] = useState(null);
  const [uploading, setUploading] = useState(false);
  const [uploadResults, setUploadResults] = useState(null);
  const [previewData, setPreviewData] = useState(null);
  const [showPreview, setShowPreview] = useState(false);
  
  // Delete confirmation modal states
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleteItem, setDeleteItem] = useState(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteModalAnimation, setDeleteModalAnimation] = useState('hidden');
  
  // Bulk delete states
  const [selectedUsers, setSelectedUsers] = useState(new Set());
  const [selectAll, setSelectAll] = useState(false);
  const [showBulkDeleteModal, setShowBulkDeleteModal] = useState(false);
  const [isBulkDeleting, setIsBulkDeleting] = useState(false);
  
  // Delete all by type states
  const [showDeleteAllModal, setShowDeleteAllModal] = useState(false);
  const [deleteAllType, setDeleteAllType] = useState(''); // 'students' or 'instructors'
  const [isDeletingAll, setIsDeletingAll] = useState(false);
  const [showSecondConfirmation, setShowSecondConfirmation] = useState(false);
  const [showBulkActions, setShowBulkActions] = useState(false);
  const [showMultiSelect, setShowMultiSelect] = useState(false);
  
  // Database counts for each user type
  const [databaseCounts, setDatabaseCounts] = useState({
    student: 0,
    instructor: 0,
    admin: 0
  });
  
  const [formData, setFormData] = useState({
    first_name: '',
    last_name: '',
    user_type: 'student',
    student_id: '',
    faculty_id: '',
    year_level: '',
    department: '',
    status: 'active'
  });

  useEffect(() => {
    fetchUsers();
    fetchDatabaseCounts();
  }, [currentPage, filterType, filterStatus, searchTerm]);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (showBulkActions && !event.target.closest('.bulk-actions-dropdown')) {
        setShowBulkActions(false);
      }
    };

    if (showBulkActions) {
      document.addEventListener('mousedown', handleClickOutside);
    }

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [showBulkActions]);

  const fetchUsers = async () => {
    try {
      setLoading(true);
      const params = new URLSearchParams({
        page: currentPage,
        limit: 20
      });

      if (filterType !== 'all') {
        params.append('type', filterType);
      }
      if (filterStatus !== 'all') {
        params.append('status', filterStatus);
      }
      if (searchTerm) {
        params.append('search', searchTerm);
      }

      const response = await axios.get(`http://localhost:5000/api/users?${params}`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });

      setUsers(response.data.users);
      setTotalPages(response.data.pagination.pages);
    } catch (error) {
      console.error('Error fetching users:', error);
      toast.error('Failed to load users');
    } finally {
      setLoading(false);
    }
  };

  const fetchDatabaseCounts = async () => {
    try {
      const [studentsResponse, instructorsResponse] = await Promise.all([
        axios.get('http://localhost:5000/api/users/by-type/student', {
          headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
        }),
        axios.get('http://localhost:5000/api/users/by-type/instructor', {
          headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
        })
      ]);

      setDatabaseCounts({
        student: studentsResponse.data.count,
        instructor: instructorsResponse.data.count,
        admin: 0 // We don't need admin count for delete all functionality
      });
    } catch (error) {
      console.error('Error fetching database counts:', error);
      // Don't show error to user as this is background functionality
    }
  };



  const handleSubmit = async (e) => {
    e.preventDefault();
    
    try {
      if (editingUser) {
        await axios.put(`http://localhost:5000/api/users/${editingUser.USERID}`, formData, {
          headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
        });
        toast.success('User updated successfully');
      } else {
        await axios.post('http://localhost:5000/api/users', formData, {
          headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
        });
        toast.success('User created successfully');
      }
      
      setModalAnimation('hidden');
      setTimeout(() => {
        setShowModal(false);
        setEditingUser(null);
        resetForm();
      }, 300);
      fetchUsers();
    } catch (error) {
      console.error('Error saving user:', error);
      
      if (error.response?.status === 409) {
        // Handle conflict errors with more specific messages
        const errorMessage = error.response?.data?.message || 'User information conflicts with existing data';
        toast.error(errorMessage);
      } else if (error.response?.status === 400) {
        // Handle validation errors
        const errorMessage = error.response?.data?.message || 'Please check your input data';
        toast.error(errorMessage);
      } else {
        toast.error(error.response?.data?.message || 'Failed to save user');
      }
    }
  };

  const handleEdit = (user) => {
    setEditingUser(user);
    setFormData({
      first_name: user.FIRSTNAME || '',
      last_name: user.LASTNAME || '',
      user_type: user.USERTYPE || 'student',
      student_id: user.STUDENTID || '',
      faculty_id: user.FACULTYID || '',
      year_level: user.YEARLEVEL || '',
      department: user.DEPARTMENT || '',
      status: user.STATUS || 'active'
    });
    setShowModal(true);
    setTimeout(() => setModalAnimation('visible'), 10);
  };

  const handleDelete = (user) => {
    const title = 'Delete User';
    const message = `Are you sure you want to delete "${user.FIRSTNAME} ${user.LASTNAME}"? This action cannot be undone and will remove all associated data including attendance records, enrollments, and device assignments.`;
    const confirmText = 'Delete User';

    setDeleteItem({
      ...user,
      title,
      message,
      confirmText
    });
    setShowDeleteModal(true);
    setTimeout(() => setDeleteModalAnimation('visible'), 10);
  };

  const confirmDelete = async () => {
    if (!deleteItem) return;

    setIsDeleting(true);
    
    try {
      await axios.delete(`http://localhost:5000/api/users/${deleteItem.USERID}`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });
      
      // Show enhanced success message
      toast.success('User deleted successfully!', {
        duration: 4000,
        style: {
          background: '#f0fdf4',
          border: '1px solid #bbf7d0',
          color: '#166534',
        },
      });
      
      // Refresh data
      await fetchUsers();
      
    } catch (error) {
      console.error('Error deleting user:', error);
      const errorMessage = error.response?.data?.message || 'Failed to delete user';
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
      setDeleteModalAnimation('hidden');
      setTimeout(() => {
        setShowDeleteModal(false);
        setDeleteItem(null);
      }, 300);
    }
  };

  const resetForm = () => {
    setFormData({
      first_name: '',
      last_name: '',
      user_type: 'student',
      student_id: '',
      faculty_id: '',
      year_level: '',
      department: '',
      status: 'active'
    });
  };

  // Bulk delete functions
  const handleSelectUser = (userId) => {
    const newSelectedUsers = new Set(selectedUsers);
    if (newSelectedUsers.has(userId)) {
      newSelectedUsers.delete(userId);
    } else {
      newSelectedUsers.add(userId);
    }
    setSelectedUsers(newSelectedUsers);
    
    // Update select all state
    setSelectAll(newSelectedUsers.size === users.length && users.length > 0);
  };

  const handleSelectAll = () => {
    if (selectAll) {
      setSelectedUsers(new Set());
    } else {
      const userIds = users.map(user => user.USERID);
      setSelectedUsers(new Set(userIds));
    }
    setSelectAll(!selectAll);
  };

  const toggleMultiSelect = () => {
    if (showMultiSelect) {
      // Clear selections when turning off multi-select
      setSelectedUsers(new Set());
      setSelectAll(false);
    }
    setShowMultiSelect(!showMultiSelect);
  };

  const handleBulkDelete = () => {
    if (selectedUsers.size === 0) return;
    setShowBulkDeleteModal(true);
    setTimeout(() => setDeleteModalAnimation('visible'), 10);
  };

  const confirmBulkDelete = async () => {
    if (selectedUsers.size === 0) return;

    setIsBulkDeleting(true);
    
    try {
      const response = await axios.delete('http://localhost:5000/api/users/bulk-delete', {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` },
        data: { ids: Array.from(selectedUsers) }
      });
      
      // Show success message
      toast.success(`${selectedUsers.size} users deleted successfully!`, {
        duration: 4000,
        style: {
          background: '#f0fdf4',
          border: '1px solid #bbf7d0',
          color: '#166534',
        },
      });
      
      // Clear selections and refresh data
      setSelectedUsers(new Set());
      setSelectAll(false);
      await fetchUsers();
      
    } catch (error) {
      console.error('Error bulk deleting users:', error);
      const errorMessage = error.response?.data?.message || 'Failed to delete users';
      toast.error(errorMessage);
    } finally {
      setIsBulkDeleting(false);
      setDeleteModalAnimation('hidden');
      setTimeout(() => {
        setShowBulkDeleteModal(false);
      }, 300);
    }
  };

  // Delete all by type functions
  const handleDeleteAllByType = (type) => {
    setDeleteAllType(type);
    setShowSecondConfirmation(false);
    setShowDeleteAllModal(true);
    setTimeout(() => setDeleteModalAnimation('visible'), 10);
  };

  const confirmDeleteAllFirst = () => {
    setShowSecondConfirmation(true);
  };

  const confirmDeleteAllFinal = async () => {
    if (!deleteAllType) return;

    setIsDeletingAll(true);
    
    try {
      // First, fetch ALL users of the specified type from the database
      const response = await axios.get(`http://localhost:5000/api/users/by-type/${deleteAllType}`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });
      
      const allUsersOfType = response.data.users;
      const userIds = allUsersOfType.map(user => user.USERID);
      
      if (userIds.length === 0) {
        toast.error(`No ${deleteAllType}s found in the database to delete`);
        return;
      }

      // Now delete all users of this type
      const deleteResponse = await axios.delete('http://localhost:5000/api/users/bulk-delete', {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` },
        data: { ids: userIds }
      });
      
      // Show success message
      toast.success(`All ${userIds.length} ${deleteAllType}s deleted successfully from the database!`, {
        duration: 5000,
        style: {
          background: '#f0fdf4',
          border: '1px solid #bbf7d0',
          color: '#166534',
        },
      });
      
      // Clear selections and refresh data
      setSelectedUsers(new Set());
      setSelectAll(false);
      await fetchUsers();
      await fetchDatabaseCounts(); // Refresh database counts after deletion
      
    } catch (error) {
      console.error(`Error deleting all ${deleteAllType}s:`, error);
      const errorMessage = error.response?.data?.message || `Failed to delete all ${deleteAllType}s`;
      toast.error(errorMessage);
    } finally {
      setIsDeletingAll(false);
      setDeleteModalAnimation('hidden');
      setTimeout(() => {
        setShowDeleteAllModal(false);
        setShowSecondConfirmation(false);
        setDeleteAllType('');
      }, 300);
    }
  };

  const cancelDeleteAll = () => {
    setDeleteModalAnimation('hidden');
    setTimeout(() => {
      setShowDeleteAllModal(false);
      setShowSecondConfirmation(false);
      setDeleteAllType('');
    }, 300);
  };

  const handleFilePreview = async () => {
    if (!uploadFile) {
      toast.error('Please select a file to upload');
      return;
    }

    setUploading(true);
    const formData = new FormData();
    formData.append('excelFile', uploadFile);

    try {
      const response = await axios.post('http://localhost:5000/api/users/preview-excel', formData, {
        headers: {
          'Authorization': `Bearer ${localStorage.getItem('token')}`,
          'Content-Type': 'multipart/form-data'
        }
      });

      setPreviewData(response.data);
      setShowPreview(true);
      toast.success(`Preview generated for ${response.data.summary.previewRows} users`);
      
    } catch (error) {
      console.error('Preview error:', error);
      toast.error(error.response?.data?.message || 'Failed to preview Excel file');
      
      if (error.response?.data) {
        setPreviewData(error.response.data);
        setShowPreview(true);
      }
    } finally {
      setUploading(false);
    }
  };

  const handleConfirmUpload = async () => {
    if (!uploadFile) {
      toast.error('Please select a file to upload');
      return;
    }

    setUploading(true);
    const formData = new FormData();
    formData.append('excelFile', uploadFile);

    try {
      const response = await axios.post('http://localhost:5000/api/users/upload-excel', formData, {
        headers: {
          'Authorization': `Bearer ${localStorage.getItem('token')}`,
          'Content-Type': 'multipart/form-data'
        }
      });

      setUploadResults(response.data);
      setShowPreview(false);
      setPreviewData(null);
      toast.success(`Successfully processed ${response.data.summary.insertedUsers} users from Excel file`);
      
      // Refresh the users list
      fetchUsers();
      
      // Reset upload state
      setUploadFile(null);
      
    } catch (error) {
      console.error('Upload error:', error);
      toast.error(error.response?.data?.message || 'Failed to upload Excel file');
      
      if (error.response?.data?.errors) {
        setUploadResults(error.response.data);
      }
    } finally {
      setUploading(false);
    }
  };

  const handleFileSelect = (event) => {
    const file = event.target.files[0];
    if (file) {
      // Validate file type
      const allowedTypes = ['.xlsx', '.xls', '.csv'];
      const fileExtension = file.name.toLowerCase().substring(file.name.lastIndexOf('.'));
      
      if (!allowedTypes.includes(fileExtension)) {
        toast.error('Please select a valid Excel file (.xlsx, .xls) or CSV file');
        return;
      }
      
      // Validate file size (10MB limit)
      if (file.size > 10 * 1024 * 1024) {
        toast.error('File size must be less than 10MB');
        return;
      }
      
      setUploadFile(file);
      setUploadResults(null);
    }
  };



  const resetUploadModal = () => {
    setUploadModalAnimation('hidden');
    setTimeout(() => {
      setShowUploadModal(false);
      setUploadFile(null);
      setUploadResults(null);
      setPreviewData(null);
      setShowPreview(false);
      setUploading(false);
    }, 300);
  };

  const getUserTypeIcon = (type) => {
    switch (type) {
      case 'admin':
        return <UserIcon className="h-5 w-5 text-red-600" />;
      case 'instructor':
        return <AcademicCapIcon className="h-5 w-5 text-blue-600" />;
      case 'student':
        return <UserGroupIcon className="h-5 w-5 text-green-600" />;
      case 'custodian':
        return <UserIcon className="h-5 w-5 text-orange-600" />;
      case 'dean':
        return <AcademicCapIcon className="h-5 w-5 text-purple-600" />;
      default:
        return <UserIcon className="h-5 w-5 text-gray-600" />;
    }
  };

  const getUserTypeColor = (type) => {
    switch (type) {
      case 'admin':
        return 'bg-red-100 text-red-800';
      case 'instructor':
        return 'bg-blue-100 text-blue-800';
      case 'student':
        return 'bg-green-100 text-green-800';
      case 'custodian':
        return 'bg-orange-100 text-orange-800';
      case 'dean':
        return 'bg-purple-100 text-purple-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const getStatusColor = (status) => {
    return status === 'active' 
      ? 'bg-green-100 text-green-800' 
      : 'bg-red-100 text-red-800';
  };

  if (loading && users.length === 0) {
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
          <h1 className="text-2xl font-bold text-gray-900">User Management</h1>
          <p className="text-gray-600">Manage students, instructors, and administrators. Assign RFID tags and fingerprints.</p>
        </div>
        {user?.role === 'admin' && (
          <div className="flex space-x-3">
            <button
              onClick={() => {
                setShowUploadModal(true);
                setTimeout(() => setUploadModalAnimation('visible'), 10);
              }}
              className="btn btn-secondary flex items-center space-x-2"
            >
              <DocumentArrowUpIcon className="h-5 w-5" />
              <span>Upload CSV/Excel</span>
            </button>
            <button
              onClick={() => {
                setShowModal(true);
                setTimeout(() => setModalAnimation('visible'), 10);
              }}
              className="btn btn-primary flex items-center space-x-2"
            >
              <PlusIcon className="h-5 w-5" />
              <span>Add User</span>
            </button>
            
            {/* Discreet Bulk Actions Dropdown */}
            <div className="relative bulk-actions-dropdown">
              <button
                onClick={() => setShowBulkActions(!showBulkActions)}
                className="btn btn-secondary flex items-center space-x-2 text-sm"
              >
                <EllipsisVerticalIcon className="h-4 w-4" />
                <span>More</span>
                <ChevronDownIcon className={`h-4 w-4 transition-transform ${showBulkActions ? 'rotate-180' : ''}`} />
              </button>
              
              {/* Collapsible Bulk Actions Menu */}
              {showBulkActions && (
                <div className="absolute right-0 mt-2 w-64 bg-white rounded-lg shadow-lg border border-gray-200 z-10">
                  <div className="p-3">
                    <div className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-3">Bulk Actions</div>
                    <div className="space-y-2">
                      <button
                        onClick={() => {
                          handleDeleteAllByType('student');
                          setShowBulkActions(false);
                        }}
                        className="w-full flex items-center space-x-2 px-3 py-2 text-sm text-red-600 hover:bg-red-50 rounded-md transition-colors"
                        disabled={databaseCounts.student === 0}
                      >
                        <TrashIcon className="h-4 w-4" />
                        <span>Delete All Students ({databaseCounts.student})</span>
                      </button>
                      <button
                        onClick={() => {
                          handleDeleteAllByType('instructor');
                          setShowBulkActions(false);
                        }}
                        className="w-full flex items-center space-x-2 px-3 py-2 text-sm text-red-600 hover:bg-red-50 rounded-md transition-colors"
                        disabled={databaseCounts.instructor === 0}
                      >
                        <TrashIcon className="h-4 w-4" />
                        <span>Delete All Instructors ({databaseCounts.instructor})</span>
                      </button>
                    </div>
                    <div className="mt-3 pt-3 border-t border-gray-100">
                      <div className="text-xs text-gray-400">
                        ⚠️ These actions delete ALL users of the selected type from the entire database
                      </div>
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Multi-Select Mode Indicator */}
      {showMultiSelect && (
        <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div className="flex items-center space-x-3">
            <div className="flex-shrink-0">
              <CheckIcon className="h-5 w-5 text-blue-600" />
            </div>
            <div>
              <h3 className="text-sm font-medium text-blue-800">Multi-Select Mode Active</h3>
              <p className="text-sm text-blue-600">
                Select users by clicking the checkboxes, then use the bulk actions toolbar to perform operations on selected users.
              </p>
            </div>
            <div className="flex-shrink-0">
              <button
                onClick={toggleMultiSelect}
                className="text-blue-600 hover:text-blue-800 text-sm font-medium"
              >
                Exit Multi-Select
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Filters */}
      <div className="bg-white rounded-lg shadow p-6">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Search</label>
            <div className="relative">
              <MagnifyingGlassIcon className="h-5 w-5 absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder="Search users..."
                value={searchTerm}
                onChange={(e) => {
                  setSearchTerm(e.target.value);
                  setCurrentPage(1); // Reset to first page when searching
                }}
                className="w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">User Type</label>
            <select
              value={filterType}
              onChange={(e) => {
                setFilterType(e.target.value);
                setCurrentPage(1); // Reset to first page when filtering
              }}
              className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="all">All Types</option>
              <option value="student">Students</option>
              <option value="instructor">Instructors</option>
              <option value="admin">Administrators</option>
              <option value="custodian">Custodians</option>
              <option value="dean">Deans</option>
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
            <select
              value={filterStatus}
              onChange={(e) => {
                setFilterStatus(e.target.value);
                setCurrentPage(1); // Reset to first page when filtering
              }}
              className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="all">All Status</option>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </select>
          </div>

          <div className="flex items-end">
            <button
              onClick={() => {
                setSearchTerm('');
                setFilterType('all');
                setFilterStatus('all');
                setCurrentPage(1);
              }}
              className="w-full px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
            >
              Clear Filters
            </button>
          </div>
        </div>
      </div>

      {/* Bulk Actions Toolbar */}
      {showMultiSelect && selectedUsers.size > 0 && (
        <div className="bg-blue-50 border-2 border-blue-200 rounded-xl p-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-4">
              <div className="flex items-center space-x-2">
                <div className="w-8 h-8 bg-blue-100 rounded-full flex items-center justify-center">
                  <span className="text-sm font-medium text-blue-600">{selectedUsers.size}</span>
                </div>
                <span className="text-sm font-medium text-blue-900">
                  {selectedUsers.size} user{selectedUsers.size === 1 ? '' : 's'} selected
                </span>
              </div>
              <button
                onClick={() => {
                  setSelectedUsers(new Set());
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
                disabled={selectedUsers.size === 0}
                className="inline-flex items-center px-3 py-2 border border-red-300 shadow-sm text-sm font-medium rounded-md text-red-700 bg-white hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <TrashIcon className="h-4 w-4 mr-2" />
                Delete Selected
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Users List */}
      <div className="bg-white rounded-lg shadow">
        <div className="px-6 py-4 border-b border-gray-200">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold text-gray-900">
              Users ({users.length})
            </h2>
            {user?.role === 'admin' && (
              <button
                onClick={toggleMultiSelect}
                className={`p-2 rounded-lg transition-all duration-200 border ${
                  showMultiSelect 
                    ? 'bg-blue-500 text-white border-blue-500 hover:bg-blue-600 shadow-md' 
                    : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 border-gray-300 hover:border-gray-400'
                }`}
                title={showMultiSelect ? 'Exit Multi-Select Mode' : 'Enable Multi-Select Mode'}
              >
                <Squares2X2Icon className="h-5 w-5" />
              </button>
            )}
          </div>
        </div>

        {users.length === 0 ? (
          <div className="text-center py-12">
            <UserIcon className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">No users found</h3>
            <p className="mt-1 text-sm text-gray-500">
              {searchTerm || filterType !== 'all' || filterStatus !== 'all'
                ? 'Try adjusting your search or filter criteria.'
                : 'Get started by creating a new user.'
              }
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  {showMultiSelect && (
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      <input
                        type="checkbox"
                        checked={selectAll}
                        onChange={handleSelectAll}
                        className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                      />
                    </th>
                  )}
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    User
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Type
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    ID
                  </th>
                  {user?.role === 'admin' && (
                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Actions
                    </th>
                  )}
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {users.map((userData) => (
                  <tr key={userData.USERID} className="hover:bg-gray-50">
                    {showMultiSelect && (
                      <td className="px-6 py-4 whitespace-nowrap">
                        <input
                          type="checkbox"
                          checked={selectedUsers.has(userData.USERID)}
                          onChange={() => handleSelectUser(userData.USERID)}
                          className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                        />
                      </td>
                    )}
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <div className="flex-shrink-0 h-10 w-10">
                          <div className="h-10 w-10 rounded-full bg-gray-200 flex items-center justify-center">
                            {getUserTypeIcon(userData.USERTYPE)}
                          </div>
                        </div>
                        <div className="ml-4">
                          <div className="text-sm font-medium text-gray-900">
                            {userData.FIRSTNAME} {userData.LASTNAME}
                          </div>
                          <div className="text-sm text-gray-500">{userData.EMAIL}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getUserTypeColor(userData.USERTYPE)}`}>
                        {userData.USERTYPE}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      <div>
                        <div className="font-medium">
                          {userData.STUDENTID || userData.EMPLOYEEID || '-'}
                        </div>
                        {userData.YEARLEVEL && (
                          <div className="text-xs text-gray-500">Year {userData.YEARLEVEL}</div>
                        )}
                      </div>
                    </td>
                    {user?.role === 'admin' && (
                      <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                        <div className="flex items-center justify-end space-x-2">
                          <button
                            onClick={() => handleEdit(userData)}
                            className="text-blue-600 hover:text-blue-900 transition-colors duration-200"
                            title="Edit User"
                          >
                            <PencilIcon className="h-4 w-4" />
                          </button>
                          <button
                            onClick={() => handleDelete(userData)}
                            className="text-red-600 hover:text-red-900 transition-colors duration-200"
                            title="Delete User"
                          >
                            <TrashIcon className="h-4 w-4" />
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="px-6 py-4 border-t border-gray-200">
            <div className="flex items-center justify-between">
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
          </div>
        )}
      </div>

      {/* Add/Edit Modal */}
      {showModal && createPortal(
        <div 
          className={`fixed bg-gray-600 bg-opacity-50 overflow-y-auto z-[60] transition-opacity duration-300 ease-in-out ${
            modalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
          }`}
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
          <div className={`relative top-20 mx-auto p-5 border w-96 shadow-lg rounded-md bg-white transition-all duration-300 ease-out ${
            modalAnimation === 'visible' 
              ? 'scale-100 opacity-100 translate-y-0' 
              : 'scale-95 opacity-0 translate-y-4'
          }`}>
            <div className="flex justify-between items-center mb-4">
              <h3 className="text-lg font-semibold text-gray-900">
                {editingUser ? 'Edit User' : 'Add New User'}
              </h3>
              <button
                onClick={() => {
                  setModalAnimation('hidden');
                  setTimeout(() => {
                    setShowModal(false);
                    setEditingUser(null);
                    resetForm();
                  }, 300);
                }}
                className="text-gray-400 hover:text-gray-600"
              >
                <XMarkIcon className="h-6 w-6" />
              </button>
            </div>

            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    First Name *
                  </label>
                  <input
                    type="text"
                    required
                    value={formData.first_name}
                    onChange={(e) => setFormData({ ...formData, first_name: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Last Name *
                  </label>
                  <input
                    type="text"
                    required
                    value={formData.last_name}
                    onChange={(e) => setFormData({ ...formData, last_name: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    User Type *
                  </label>
                  <select
                    required
                    value={formData.user_type}
                    onChange={(e) => setFormData({ ...formData, user_type: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  >
                    <option value="student">Student</option>
                    <option value="instructor">Instructor</option>
                    <option value="admin">Administrator</option>
                    <option value="custodian">Custodian</option>
                    <option value="dean">Dean</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Status
                  </label>
                  <select
                    value={formData.status}
                    onChange={(e) => setFormData({ ...formData, status: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  >
                    <option value="active">Active</option>
                    <option value="inactive">Inactive</option>
                  </select>
                </div>
              </div>

              {formData.user_type === 'student' && (
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Student ID
                    </label>
                    <input
                      type="text"
                      value={formData.student_id}
                      onChange={(e) => setFormData({ ...formData, student_id: e.target.value })}
                      className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Year Level
                    </label>
                    <select
                      value={formData.year_level}
                      onChange={(e) => setFormData({ ...formData, year_level: e.target.value })}
                      className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                    >
                      <option value="">Select Year</option>
                      <option value="1">1st Year</option>
                      <option value="2">2nd Year</option>
                      <option value="3">3rd Year</option>
                      <option value="4">4th Year</option>
                    </select>
                  </div>
                </div>
              )}

              {(formData.user_type === 'instructor' || formData.user_type === 'admin') && (
                <div>
                                  <label className="block text-sm font-medium text-gray-700 mb-1">
                  Faculty ID
                </label>
                  <input
                    type="text"
                    value={formData.faculty_id}
                    onChange={(e) => setFormData({ ...formData, faculty_id: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  />
                </div>
              )}

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Department
                </label>
                <input
                  type="text"
                  value={formData.department}
                  onChange={(e) => setFormData({ ...formData, department: e.target.value })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
              </div>

              <div className="flex justify-end space-x-3 pt-4">
                <button
                  type="button"
                  onClick={() => {
                    setModalAnimation('hidden');
                    setTimeout(() => {
                      setShowModal(false);
                      setEditingUser(null);
                      resetForm();
                    }, 300);
                  }}
                  className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700"
                >
                  {editingUser ? 'Update' : 'Create'} User
                </button>
              </div>
            </form>
          </div>
        </div>,
        document.body
      )}

      {/* Upload Excel/CSV Modal */}
      {showUploadModal && createPortal(
        <div 
          className={`fixed bg-gray-600 bg-opacity-50 overflow-y-auto z-[60] transition-opacity duration-300 ease-in-out ${
            uploadModalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
          }`}
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
          <div className={`relative top-20 mx-auto p-5 border w-full max-w-2xl shadow-lg rounded-md bg-white transition-all duration-300 ease-out ${
            uploadModalAnimation === 'visible' 
              ? 'scale-100 opacity-100 translate-y-0' 
              : 'scale-95 opacity-0 translate-y-4'
          }`}>
            <div className="flex justify-between items-center mb-4">
              <h3 className="text-lg font-semibold text-gray-900">
                Upload Excel/CSV File
              </h3>
              <button
                onClick={resetUploadModal}
                className="text-gray-400 hover:text-gray-600"
              >
                <XMarkIcon className="h-6 w-6" />
              </button>
            </div>

            <div className="space-y-4">
              {/* Instructions */}
              <div className="bg-blue-50 border border-blue-200 rounded-md p-4">
                <h4 className="font-medium text-blue-800 mb-2">Instructions:</h4>
                <ul className="text-sm text-blue-700 space-y-1">
                  <li>• Upload an Excel file (.xlsx, .xls) or CSV file</li>
                  <li>• The file must contain a sheet named "SBO"</li>
                  <li>• Required columns: "SL#", "Student Name", "Subject/Major", "Year"</li>
                  <li>• Student names can be: "LASTNAME, FIRSTNAME" or "LASTNAME FIRSTNAME"</li>
                  <li>• System processes rows with valid SL# numbers only</li>
                  <li>• Processing stops when SL# is missing or invalid</li>
                  <li>• Maximum file size: 10MB</li>
                </ul>
              </div>

              {/* File Upload */}
              <div className="border-2 border-dashed border-gray-300 rounded-lg p-6">
                <div className="text-center">
                  <DocumentArrowUpIcon className="mx-auto h-12 w-12 text-gray-400" />
                  <div className="mt-4">
                    <label htmlFor="file-upload" className="cursor-pointer">
                      <span className="mt-2 block text-sm font-medium text-gray-900">
                        {uploadFile ? uploadFile.name : 'Select a file to upload'}
                      </span>
                      <input
                        id="file-upload"
                        name="file-upload"
                        type="file"
                        className="sr-only"
                        accept=".xlsx,.xls,.csv"
                        onChange={handleFileSelect}
                        disabled={uploading}
                      />
                      <span className="mt-1 block text-sm text-gray-500">
                        or drag and drop
                      </span>
                    </label>
                  </div>
                  <p className="mt-1 text-xs text-gray-500">
                    Excel (.xlsx, .xls) or CSV files up to 10MB
                  </p>
                </div>
              </div>

              {/* Preview Data */}
              {showPreview && previewData && (
                <div className="space-y-3">
                  <div className="bg-blue-50 border border-blue-200 rounded-md p-4">
                    <h4 className="font-medium text-blue-800 mb-2">Preview Summary:</h4>
                    <div className="text-sm text-blue-700 space-y-1">
                      <p>Sheet found: <strong>{previewData.sheetName}</strong></p>
                      <p>Total data rows available: {previewData.summary?.totalRows || 0}</p>
                      <p>Valid students found: {previewData.summary?.previewRows || 0}</p>
                      <p>Issues detected: {previewData.summary?.errors || 0}</p>
                    </div>
                    {previewData.headers && (
                      <div className="mt-2 text-xs text-blue-600">
                        <p>Columns detected: {previewData.headers.studentNameCol}, {previewData.headers.subjectMajorCol}, {previewData.headers.yearCol}</p>
                      </div>
                    )}
                  </div>

                  {previewData.errors && previewData.errors.length > 0 && (
                    <div className="bg-red-50 border border-red-200 rounded-md p-4 max-h-32 overflow-y-auto">
                      <h4 className="font-medium text-red-800 mb-2">Issues Found:</h4>
                      <ul className="text-sm text-red-700 space-y-1">
                        {previewData.errors.slice(0, 5).map((error, index) => (
                          <li key={index}>• {error}</li>
                        ))}
                        {previewData.errors.length > 5 && (
                          <li className="text-red-600 font-medium">
                            ... and {previewData.errors.length - 5} more issues
                          </li>
                        )}
                      </ul>
                    </div>
                  )}

                  {previewData.previewUsers && previewData.previewUsers.length > 0 && (
                    <div className="bg-gray-50 border border-gray-200 rounded-md p-4 max-h-64 overflow-y-auto">
                      <h4 className="font-medium text-gray-800 mb-2">Data Preview (All {previewData.previewUsers.length} students found):</h4>
                      <div className="overflow-x-auto">
                        <table className="min-w-full text-xs">
                          <thead className="bg-gray-100">
                            <tr>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">Row</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">SL#</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">Original Name</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">First Name</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">Last Name</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">Email</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">Subject/Major</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">Year</th>
                              <th className="px-2 py-1 text-left font-medium text-gray-600">Student ID</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-gray-200">
                            {previewData.previewUsers.map((user, index) => (
                              <tr key={index} className="hover:bg-gray-50">
                                <td className="px-2 py-1 text-gray-500">{user.rowNumber}</td>
                                <td className="px-2 py-1 text-blue-600 font-medium">{user.slNumber}</td>
                                <td className="px-2 py-1 text-gray-900">{user.originalName}</td>
                                <td className="px-2 py-1 text-gray-900">{user.firstName}</td>
                                <td className="px-2 py-1 text-gray-900">{user.lastName}</td>
                                <td className="px-2 py-1 text-gray-600">{user.email}</td>
                                <td className="px-2 py-1 text-gray-900">{user.subjectMajor}</td>
                                <td className="px-2 py-1 text-gray-900">{user.yearLevel}</td>
                                <td className="px-2 py-1 text-gray-600">{user.studentId}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* Upload Results */}
              {uploadResults && (
                <div className="space-y-3">
                  <div className="bg-green-50 border border-green-200 rounded-md p-4">
                    <h4 className="font-medium text-green-800 mb-2">Upload Complete!</h4>
                    <div className="text-sm text-green-700 space-y-1">
                      <p>Total rows processed: {uploadResults.summary?.totalRows || 0}</p>
                      <p>Users successfully imported: {uploadResults.summary?.insertedUsers || 0}</p>
                      <p>Errors encountered: {uploadResults.summary?.errors || 0}</p>
                    </div>
                  </div>

                  {uploadResults.errors && uploadResults.errors.length > 0 && (
                    <div className="bg-red-50 border border-red-200 rounded-md p-4 max-h-40 overflow-y-auto">
                      <h4 className="font-medium text-red-800 mb-2">Errors:</h4>
                      <ul className="text-sm text-red-700 space-y-1">
                        {uploadResults.errors.slice(0, 10).map((error, index) => (
                          <li key={index}>• {error}</li>
                        ))}
                        {uploadResults.errors.length > 10 && (
                          <li className="text-red-600 font-medium">
                            ... and {uploadResults.errors.length - 10} more errors
                          </li>
                        )}
                      </ul>
                    </div>
                  )}

                  {uploadResults.insertedUsers && uploadResults.insertedUsers.length > 0 && (
                    <div className="bg-gray-50 border border-gray-200 rounded-md p-4 max-h-40 overflow-y-auto">
                      <h4 className="font-medium text-gray-800 mb-2">Successfully Imported Users:</h4>
                      <div className="text-sm text-gray-700 space-y-1">
                        {uploadResults.insertedUsers.slice(0, 5).map((user, index) => (
                          <div key={index} className="flex justify-between">
                            <span>{user.name}</span>
                            <span className="text-gray-500">{user.subject} - Year {user.year}</span>
                          </div>
                        ))}
                        {uploadResults.insertedUsers.length > 5 && (
                          <p className="text-gray-600 font-medium">
                            ... and {uploadResults.insertedUsers.length - 5} more users
                          </p>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* Action Buttons */}
              <div className="flex justify-end space-x-3 pt-4">
                <button
                  type="button"
                  onClick={resetUploadModal}
                  className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                  disabled={uploading}
                >
                  {uploadResults ? 'Close' : 'Cancel'}
                </button>
                
                {!showPreview && !uploadResults && (
                  <button
                    onClick={handleFilePreview}
                    disabled={!uploadFile || uploading}
                    className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {uploading ? (
                      <div className="flex items-center">
                        <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                        Processing...
                      </div>
                    ) : (
                      'Preview Data'
                    )}
                  </button>
                )}

                {showPreview && !uploadResults && (
                  <>
                    <button
                      onClick={() => {
                        setShowPreview(false);
                        setPreviewData(null);
                      }}
                      className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                      disabled={uploading}
                    >
                      Back to File Selection
                    </button>
                    <button
                      onClick={handleConfirmUpload}
                      disabled={uploading}
                      className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      {uploading ? (
                        <div className="flex items-center">
                          <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                          Uploading...
                        </div>
                      ) : (
                        'Confirm Upload'
                      )}
                    </button>
                  </>
                )}
              </div>
            </div>
          </div>
        </div>,
        document.body
      )}


      {/* Modern Delete Confirmation Modal */}
      {showDeleteModal && deleteItem && (
        <div className="fixed inset-0 overflow-y-auto z-[60]">
          {/* Backdrop with blur and fade animation */}
          <div className={`fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ease-in-out ${
            deleteModalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
          }`}>
            <div className="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
              {/* Modal container with scale and fade animation */}
              <div className={`relative transform overflow-hidden rounded-2xl bg-white text-left shadow-2xl transition-all duration-300 ease-out sm:my-8 sm:w-full sm:max-w-lg ${
                deleteModalAnimation === 'visible' 
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
                      setDeleteModalAnimation('hidden');
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

      {/* Bulk Delete Confirmation Modal */}
      {showBulkDeleteModal && (
        <div className="fixed inset-0 overflow-y-auto z-[60]">
          {/* Backdrop with blur and fade animation */}
          <div className={`fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ease-in-out ${
            deleteModalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
          }`}>
            <div className="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
              {/* Modal container with scale and fade animation */}
              <div className={`relative transform overflow-hidden rounded-2xl bg-white text-left shadow-2xl transition-all duration-300 ease-out sm:my-8 sm:w-full sm:max-w-lg ${
                deleteModalAnimation === 'visible' 
                  ? 'scale-100 opacity-100 translate-y-0' 
                  : 'scale-95 opacity-0 translate-y-4'
              }`}>
                {/* Gradient header */}
                <div className="bg-gradient-to-r from-red-50 to-red-100 px-6 py-8">
                  <div className="mx-auto flex items-center justify-center h-16 w-16 rounded-full bg-red-100 shadow-lg mb-4 animate-pulse">
                    <ExclamationTriangleIcon className="h-8 w-8 text-red-600" />
                  </div>
                  <h3 className="text-xl font-semibold text-gray-900 mb-2">
                    Bulk Delete Users
                  </h3>
                  <p className="text-sm text-gray-600 leading-relaxed">
                    Are you sure you want to delete <strong>{selectedUsers.size} users</strong>? This action cannot be undone and will remove all associated data including attendance records, enrollments, and device assignments.
                  </p>
                </div>
                
                {/* Action buttons */}
                <div className="bg-gray-50 px-6 py-4 flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-3 space-y-3 space-y-reverse sm:space-y-0">
                  <button
                    type="button"
                    onClick={() => {
                      setDeleteModalAnimation('hidden');
                      setTimeout(() => {
                        setShowBulkDeleteModal(false);
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
                        Delete {selectedUsers.size} Users
                      </>
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Delete All by Type - Double Confirmation Modal */}
      {showDeleteAllModal && (
        <div className="fixed inset-0 overflow-y-auto z-[60]">
          {/* Backdrop with blur and fade animation */}
          <div className={`fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ease-in-out ${
            deleteModalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
          }`}>
            <div className="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
              {/* Modal container with scale and fade animation */}
              <div className={`relative transform overflow-hidden rounded-2xl bg-white text-left shadow-2xl transition-all duration-300 ease-out sm:my-8 sm:w-full sm:max-w-lg ${
                deleteModalAnimation === 'visible' 
                  ? 'scale-100 opacity-100 translate-y-0' 
                  : 'scale-95 opacity-0 translate-y-4'
              }`}>
                {/* Gradient header */}
                <div className="bg-gradient-to-r from-red-50 to-red-100 px-6 py-8">
                  <div className="mx-auto flex items-center justify-center h-16 w-16 rounded-full bg-red-100 shadow-lg mb-4 animate-pulse">
                    <ExclamationTriangleIcon className="h-8 w-8 text-red-600" />
                  </div>
                  
                  {!showSecondConfirmation ? (
                    <>
                      <h3 className="text-xl font-semibold text-gray-900 mb-2">
                        Delete All {deleteAllType === 'student' ? 'Students' : 'Instructors'}
                      </h3>
                      <p className="text-sm text-gray-600 leading-relaxed">
                        This will delete <strong>ALL {deleteAllType}s</strong> from the entire database, not just the current page. 
                        This action cannot be undone and will remove all associated data including attendance records, enrollments, and device assignments.
                      </p>
                      <div className="mt-4 p-3 bg-yellow-50 border border-yellow-200 rounded-lg">
                        <p className="text-sm text-yellow-800 font-medium">
                          ⚠️ This is a destructive action that will permanently delete ALL {deleteAllType}s from the database.
                        </p>
                      </div>
                    </>
                  ) : (
                    <>
                      <h3 className="text-xl font-semibold text-gray-900 mb-2">
                        Final Confirmation Required
                      </h3>
                      <p className="text-sm text-gray-600 leading-relaxed">
                        You are about to permanently delete <strong>ALL {deleteAllType}s</strong> from the entire database. 
                        This action cannot be undone.
                      </p>
                      <div className="mt-4 p-3 bg-red-50 border border-red-200 rounded-lg">
                        <p className="text-sm text-red-800 font-medium">
                          🚨 FINAL WARNING: This will delete ALL {deleteAllType}s from the database permanently!
                        </p>
                      </div>
                    </>
                  )}
                </div>
                
                {/* Action buttons */}
                <div className="bg-gray-50 px-6 py-4 flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-3 space-y-3 space-y-reverse sm:space-y-0">
                  <button
                    type="button"
                    onClick={cancelDeleteAll}
                    disabled={isDeletingAll}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-gray-300 shadow-sm text-sm font-medium rounded-xl text-gray-700 bg-white hover:bg-gray-50 hover:border-gray-400 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-all duration-200 ease-in-out transform hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  
                  {!showSecondConfirmation ? (
                    <button
                      type="button"
                      onClick={confirmDeleteAllFirst}
                      disabled={isDeletingAll}
                      className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r from-orange-600 to-orange-700 hover:from-orange-700 hover:to-orange-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-orange-500 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      Continue to Final Confirmation
                    </button>
                  ) : (
                    <button
                      type="button"
                      onClick={confirmDeleteAllFinal}
                      disabled={isDeletingAll}
                      className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      {isDeletingAll ? (
                        <>
                          <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2"></div>
                          Deleting All {deleteAllType === 'student' ? 'Students' : 'Instructors'}...
                        </>
                      ) : (
                        <>
                          <TrashIcon className="h-4 w-4 mr-2" />
                          DELETE ALL {deleteAllType === 'student' ? 'STUDENTS' : 'INSTRUCTORS'}
                        </>
                      )}
                    </button>
                  )}
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default Users;