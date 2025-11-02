import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { PlusIcon, PencilIcon, TrashIcon, BuildingOfficeIcon, UsersIcon, ExclamationTriangleIcon } from '@heroicons/react/24/outline';
import toast from 'react-hot-toast';
import axios from 'axios';

// Set up axios defaults
axios.defaults.baseURL = 'http://localhost:5000';

function Rooms() {
  const [rooms, setRooms] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [editingRoom, setEditingRoom] = useState(null);
  const [searchTerm, setSearchTerm] = useState('');
  const [filterBuilding, setFilterBuilding] = useState('');
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleteData, setDeleteData] = useState(null);
  const [modalAnimation, setModalAnimation] = useState('hidden');

  const [formData, setFormData] = useState({
    room_number: '',
    room_name: '',
    building: '',
    capacity: '',
    room_type: 'classroom'
  });

  const roomTypes = [
    { value: 'classroom', label: 'Classroom' },
    { value: 'laboratory', label: 'Laboratory' },
    { value: 'office', label: 'Office' }
  ];

  useEffect(() => {
    fetchRooms();
  }, []);

  const fetchRooms = async () => {
    try {
      setLoading(true);
      const token = localStorage.getItem('token');
      
      if (!token) {
        toast.error('Please login to access rooms');
        return;
      }

      const headers = { Authorization: `Bearer ${token}` };
      const response = await axios.get('/api/rooms', { headers });
      
      setRooms(response.data.rooms || []);
    } catch (error) {
      console.error('Error fetching rooms:', error);
      if (error.response?.status === 401) {
        toast.error('Session expired. Please login again.');
        localStorage.removeItem('token');
        window.location.href = '/login';
      } else {
        toast.error('Failed to load rooms');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };

      if (editingRoom) {
        // Update existing room
        await axios.put(`/api/rooms/${editingRoom.ROOMID}`, formData, { headers });
        toast.success('Room updated successfully');
      } else {
        // Create new room
        await axios.post('/api/rooms', formData, { headers });
        toast.success('Room created successfully');
      }

      await fetchRooms(); // Refresh data
      setShowModal(false);
      resetForm();
      setEditingRoom(null);
    } catch (error) {
      console.error('Error saving room:', error);
      if (error.response?.data?.message) {
        toast.error(error.response.data.message);
      } else {
        toast.error('Failed to save room');
      }
    }
  };

  const resetForm = () => {
    setFormData({
      room_number: '',
      room_name: '',
      building: '',
      capacity: '',
      room_type: 'classroom'
    });
  };

  const handleEdit = (room) => {
    setEditingRoom(room);
    setFormData({
      room_number: room.ROOMNUMBER || '',
      room_name: room.ROOMNAME || '',
      building: room.BUILDING || '',
      capacity: room.CAPACITY || '',
      room_type: room.room_type || 'classroom'
    });
    setShowModal(true);
  };

  const handleDelete = (roomId, roomNumber, roomName) => {
    setDeleteData({
      type: 'room',
      id: roomId,
      number: roomNumber,
      name: roomName,
      title: 'Delete Room',
      message: `Are you sure you want to delete "${roomNumber} - ${roomName}"? This action cannot be undone.`,
      confirmText: 'Delete Room'
    });
    setShowDeleteModal(true);
    setTimeout(() => setModalAnimation('visible'), 10);
  };

  const confirmDelete = async () => {
    if (!deleteData) return;

    try {
      const token = localStorage.getItem('token');
      const headers = { Authorization: `Bearer ${token}` };
      
      const response = await axios.delete(`/api/rooms/${deleteData.id}`, { headers });
      
      // Show enhanced success message
      const message = response.data.message || 'Room deleted successfully!';
      toast.success(message, {
        duration: 4000,
        style: {
          background: '#f0fdf4',
          border: '1px solid #bbf7d0',
          color: '#166534',
        },
      });
      
      await fetchRooms(); // Refresh data
    } catch (error) {
      console.error('Error deleting room:', error);
      const errorMessage = error.response?.data?.message || 'Failed to delete room';
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
      setModalAnimation('hidden');
      setTimeout(() => {
        setShowDeleteModal(false);
        setDeleteData(null);
      }, 300);
    }
  };

  // Get unique buildings for filter
  const buildings = [...new Set(rooms.map(room => room.BUILDING).filter(Boolean))];

  // Filter rooms based on search and building filter
  const filteredRooms = rooms.filter(room => {
    const matchesSearch = 
      room.ROOMNUMBER?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      room.ROOMNAME?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      room.BUILDING?.toLowerCase().includes(searchTerm.toLowerCase());
    
    const matchesBuilding = !filterBuilding || room.BUILDING === filterBuilding;
    
    return matchesSearch && matchesBuilding;
  });

  const getRoomStatusColor = (status) => {
    switch (status) {
      case 'Available':
        return 'bg-green-100 text-green-800';
      case 'Maintenance':
        return 'bg-yellow-100 text-yellow-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const getRoomTypeIcon = (type) => {
    switch (type) {
      case 'laboratory':
        return '🧪';
      case 'office':
        return '🏢';
      default:
        return '🏫';
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
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Room Management</h1>
          <p className="text-gray-600">Manage classrooms, laboratories, and office spaces</p>
        </div>
        <button
          onClick={() => setShowModal(true)}
          className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700"
        >
          <PlusIcon className="h-4 w-4 mr-2" />
          Add Room
        </button>
      </div>

      {/* Filters */}
      <div className="bg-white p-4 rounded-lg shadow border border-gray-200">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Search Rooms</label>
            <input
              type="text"
              placeholder="Search by room number, name, or building..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Filter by Building</label>
            <select
              value={filterBuilding}
              onChange={(e) => setFilterBuilding(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-3 py-2"
            >
              <option value="">All Buildings</option>
              {buildings.map(building => (
                <option key={building} value={building}>{building}</option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {/* Rooms Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {filteredRooms.map((room) => (
          <div key={room.ROOMID} className="bg-white rounded-lg shadow border border-gray-200 p-6">
            <div className="flex justify-between items-start mb-4">
              <div className="flex items-start space-x-3">
                <div className="text-2xl">{getRoomTypeIcon(room.room_type)}</div>
                <div>
                  <h3 className="text-lg font-semibold text-gray-900">{room.ROOMNUMBER}</h3>
                  <p className="text-sm text-gray-600">{room.ROOMNAME}</p>
                </div>
              </div>
              <div className="flex space-x-2">
                <button
                  onClick={() => handleEdit(room)}
                  className="text-gray-400 hover:text-blue-600"
                >
                  <PencilIcon className="h-4 w-4" />
                </button>
                <button
                  onClick={() => handleDelete(room.ROOMID, room.ROOMNUMBER, room.ROOMNAME)}
                  className="text-gray-400 hover:text-red-600"
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              </div>
            </div>

            <div className="space-y-3">
              <div className="flex items-center text-sm text-gray-600">
                <BuildingOfficeIcon className="h-4 w-4 mr-2" />
                {room.BUILDING}
              </div>
              
              {room.CAPACITY && (
                <div className="flex items-center text-sm text-gray-600">
                  <UsersIcon className="h-4 w-4 mr-2" />
                  Capacity: {room.CAPACITY} people
                </div>
              )}

              <div className="flex items-center justify-between">
                <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getRoomStatusColor(room.STATUS)}`}>
                  {room.STATUS}
                </span>
                {room.room_type && (
                  <span className="text-xs text-gray-500 capitalize">
                    {room.room_type}
                  </span>
                )}
              </div>

              {room.device_count !== undefined && (
                <div className="pt-2 border-t border-gray-200">
                  <div className="flex justify-between text-xs text-gray-500">
                    <span>Devices: {room.device_count || 0}</span>
                    <span>Online: {room.online_devices || 0}</span>
                  </div>
                </div>
              )}
            </div>
          </div>
        ))}
      </div>

      {filteredRooms.length === 0 && !loading && (
        <div className="text-center py-12">
          <BuildingOfficeIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">No rooms found</h3>
          <p className="mt-1 text-sm text-gray-500">
            {searchTerm || filterBuilding ? 'Try adjusting your search filters.' : 'Get started by adding a new room.'}
          </p>
          {!searchTerm && !filterBuilding && (
            <div className="mt-6">
              <button
                onClick={() => setShowModal(true)}
                className="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700"
              >
                <PlusIcon className="h-4 w-4 mr-2" />
                Add Room
              </button>
            </div>
          )}
        </div>
      )}

      {/* Add/Edit Room Modal */}
      {showModal && createPortal(
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
            <div className="mt-3">
              <h3 className="text-lg font-medium text-gray-900 mb-4">
                {editingRoom ? 'Edit Room' : 'Add New Room'}
              </h3>
              <form onSubmit={handleSubmit} className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Room Number</label>
                  <input
                    type="text"
                    value={formData.room_number}
                    onChange={(e) => setFormData({ ...formData, room_number: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    placeholder="e.g., CIT-601"
                    required
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Room Name</label>
                  <input
                    type="text"
                    value={formData.room_name}
                    onChange={(e) => setFormData({ ...formData, room_name: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    placeholder="e.g., Computer Laboratory 1"
                    required
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Building</label>
                  <input
                    type="text"
                    value={formData.building}
                    onChange={(e) => setFormData({ ...formData, building: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    placeholder="e.g., IT Building"
                    required
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Room Type</label>
                  <select
                    value={formData.room_type}
                    onChange={(e) => setFormData({ ...formData, room_type: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    required
                  >
                    {roomTypes.map(type => (
                      <option key={type.value} value={type.value}>{type.label}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Capacity</label>
                  <input
                    type="number"
                    value={formData.capacity}
                    onChange={(e) => setFormData({ ...formData, capacity: e.target.value })}
                    className="mt-1 block w-full border border-gray-300 rounded-md px-3 py-2"
                    placeholder="e.g., 40"
                    min="1"
                  />
                </div>

                <div className="flex justify-end space-x-3 pt-4">
                  <button
                    type="button"
                    onClick={() => {
                      setShowModal(false);
                      setEditingRoom(null);
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
                    {editingRoom ? 'Update' : 'Create'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>,
        document.body
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteModal && deleteData && (
        <div className="fixed inset-0 overflow-y-auto z-[60]">
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

export default Rooms; 