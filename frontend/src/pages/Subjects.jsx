import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { PlusIcon, PencilIcon, TrashIcon, BookOpenIcon, UserGroupIcon, UserPlusIcon, ExclamationTriangleIcon } from '@heroicons/react/24/outline';
import axios from 'axios';
import toast from 'react-hot-toast';

function Subjects() {
  const [subjects, setSubjects] = useState([]);
  const [instructors, setInstructors] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [showStudentModal, setShowStudentModal] = useState(false);
  const [editingSubject, setEditingSubject] = useState(null);
  const [selectedSubject, setSelectedSubject] = useState(null);
  const [enrolledStudents, setEnrolledStudents] = useState([]);
  const [availableStudents, setAvailableStudents] = useState([]);
  const [selectedStudents, setSelectedStudents] = useState([]);
  const [loadingStudents, setLoadingStudents] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleteData, setDeleteData] = useState(null);
  const [modalAnimation, setModalAnimation] = useState('hidden');

  const [formData, setFormData] = useState({
    SUBJECTCODE: '',
    SUBJECTNAME: '',
    DESCRIPTION: '',
    INSTRUCTORID: '',
    SEMESTER: 'First Semester',
    YEAR: new Date().getFullYear(),
    ACADEMICYEAR: '2024-2025'
  });

  const semesterOptions = ['First Semester', 'Second Semester', 'Summer'];

  useEffect(() => {
    fetchData();
  }, []);

  const fetchData = async () => {
    try {
      setLoading(true);
      
      // Fetch subjects and instructors in parallel
      const [subjectsResponse, instructorsResponse] = await Promise.all([
        axios.get('/api/subjects', {
          headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
        }),
        axios.get('/api/subjects/instructors/list', {
          headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
        })
      ]);

      setSubjects(subjectsResponse.data.subjects || []);
      setInstructors(instructorsResponse.data || []);
      
    } catch (error) {
      console.error('Error fetching data:', error);
      if (error.response?.status === 401) {
        toast.error('Session expired. Please login again.');
        // Redirect to login or refresh token
      } else {
        toast.error('Failed to load subjects');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    
    try {
      const url = editingSubject 
        ? `/api/subjects/${editingSubject.SUBJECTID}` 
        : '/api/subjects';
      
      const method = editingSubject ? 'put' : 'post';
      
      await axios[method](url, formData, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });

      toast.success(`Subject ${editingSubject ? 'updated' : 'created'} successfully!`);
      setShowModal(false);
      resetForm();
      fetchData();
    } catch (error) {
      console.error('Error saving subject:', error);
      if (error.response?.data?.errors) {
        error.response.data.errors.forEach(err => toast.error(err.msg));
      } else {
        toast.error(error.response?.data?.message || 'Failed to save subject');
      }
    }
  };

  const handleEdit = (subject) => {
    setEditingSubject(subject);
    setFormData({
      SUBJECTCODE: subject.SUBJECTCODE || '',
      SUBJECTNAME: subject.SUBJECTNAME || '',
      DESCRIPTION: subject.DESCRIPTION || '',
      INSTRUCTORID: subject.INSTRUCTORID || '',
      SEMESTER: subject.SEMESTER || 'First Semester',
      YEAR: subject.YEAR || new Date().getFullYear(),
      ACADEMICYEAR: subject.ACADEMICYEAR || '2024-2025'
    });
    setShowModal(true);
  };

  const handleDelete = (subjectId, subjectName) => {
    setDeleteData({
      type: 'subject',
      id: subjectId,
      name: subjectName,
      title: 'Delete Subject',
      message: `Are you sure you want to delete "${subjectName}"? This will also remove all related schedules and enrollments. This action cannot be undone.`,
      confirmText: 'Delete Subject'
    });
    setShowDeleteModal(true);
    setTimeout(() => setModalAnimation('visible'), 10);
  };

  const confirmDelete = async () => {
    if (!deleteData) return;

    try {
      const response = await axios.delete(`/api/subjects/${deleteData.id}`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });
      
      // Show enhanced success message with cascade deletion info
      const message = response.data.message || 'Subject deleted successfully!';
      toast.success(message, {
        duration: 4000,
        style: {
          background: '#f0fdf4',
          border: '1px solid #bbf7d0',
          color: '#166534',
        },
      });
      fetchData();
    } catch (error) {
      console.error('Error deleting subject:', error);
      const errorMessage = error.response?.data?.message || 'Failed to delete subject';
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

  const resetForm = () => {
    setFormData({
      SUBJECTCODE: '',
      SUBJECTNAME: '',
      DESCRIPTION: '',
      INSTRUCTORID: '',
      SEMESTER: 'First Semester',
      YEAR: new Date().getFullYear(),
      ACADEMICYEAR: '2024-2025'
    });
    setEditingSubject(null);
  };

  const closeModal = () => {
    setShowModal(false);
    resetForm();
  };

  const handleAssignStudents = async (subject) => {
    setSelectedSubject(subject);
    setShowStudentModal(true);
    setSelectedStudents([]);
    await fetchStudentsForSubject(subject.SUBJECTID);
  };

  const fetchStudentsForSubject = async (subjectId) => {
    try {
      setLoadingStudents(true);
      const response = await axios.get(`/api/subjects/${subjectId}/students`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });
      
      setEnrolledStudents(response.data.enrolled || []);
      setAvailableStudents(response.data.available || []);
    } catch (error) {
      console.error('Error fetching students:', error);
      toast.error('Failed to load students for this subject');
    } finally {
      setLoadingStudents(false);
    }
  };

  const handleEnrollStudents = async () => {
    if (selectedStudents.length === 0) {
      toast.error('Please select at least one student');
      return;
    }

    try {
      await axios.post(`/api/subjects/${selectedSubject.SUBJECTID}/enroll`, {
        studentIds: selectedStudents
      }, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });

      toast.success(`Successfully enrolled ${selectedStudents.length} student(s)`);
      setShowStudentModal(false);
      setSelectedStudents([]);
      fetchData(); // Refresh subjects list to update enrollment counts
    } catch (error) {
      console.error('Error enrolling students:', error);
      toast.error(error.response?.data?.message || 'Failed to enroll students');
    }
  };

  const handleRemoveStudent = async (studentId) => {
    if (!window.confirm('Are you sure you want to remove this student from the subject?')) {
      return;
    }

    try {
      await axios.delete(`/api/subjects/${selectedSubject.SUBJECTID}/students/${studentId}`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token')}` }
      });

      toast.success('Student removed successfully');
      await fetchStudentsForSubject(selectedSubject.SUBJECTID);
      fetchData(); // Refresh subjects list to update enrollment counts
    } catch (error) {
      console.error('Error removing student:', error);
      toast.error(error.response?.data?.message || 'Failed to remove student');
    }
  };

  const closeStudentModal = () => {
    setShowStudentModal(false);
    setSelectedSubject(null);
    setEnrolledStudents([]);
    setAvailableStudents([]);
    setSelectedStudents([]);
  };

  if (loading) {
    return (
      <div className="flex justify-center items-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  return (
    <div className="p-6">
      {/* Header */}
      <div className="mb-8">
        <div className="flex justify-between items-center">
          <div>
            <h1 className="text-3xl font-bold text-gray-900">Subjects</h1>
            <p className="mt-1 text-sm text-gray-600">
              Manage academic subjects and assign instructors
            </p>
          </div>
          <button
            onClick={() => setShowModal(true)}
            className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
          >
            <PlusIcon className="h-5 w-5 mr-2" />
            Add Subject
          </button>
        </div>
      </div>

      {/* Subjects Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {subjects.map((subject) => (
          <div key={subject.SUBJECTID} className="bg-white overflow-hidden shadow rounded-lg border border-gray-200 hover:shadow-md transition-shadow">
            <div className="p-6">
              <div className="flex items-center justify-between mb-4">
                <div className="flex items-center">
                  <BookOpenIcon className="h-8 w-8 text-blue-600" />
                  <div className="ml-3">
                    <h3 className="text-lg font-medium text-gray-900">{subject.SUBJECTCODE}</h3>
                    <p className="text-sm text-gray-500">{subject.SEMESTER} {subject.YEAR}</p>
                  </div>
                </div>
                <div className="flex space-x-2">
                  <button
                    onClick={() => handleAssignStudents(subject)}
                    className="p-2 text-gray-400 hover:text-green-600 transition-colors"
                    title="Assign Students"
                  >
                    <UserPlusIcon className="h-4 w-4" />
                  </button>
                  <button
                    onClick={() => handleEdit(subject)}
                    className="p-2 text-gray-400 hover:text-blue-600 transition-colors"
                    title="Edit Subject"
                  >
                    <PencilIcon className="h-4 w-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(subject.SUBJECTID, subject.SUBJECTNAME)}
                    className="p-2 text-gray-400 hover:text-red-600 transition-colors"
                    title="Delete Subject"
                  >
                    <TrashIcon className="h-4 w-4" />
                  </button>
                </div>
              </div>
              
              <h4 className="text-base font-semibold text-gray-900 mb-2">
                {subject.SUBJECTNAME}
              </h4>
              
              {subject.DESCRIPTION && (
                <p className="text-sm text-gray-600 mb-4 line-clamp-2">
                  {subject.DESCRIPTION}
                </p>
              )}
              
              <div className="flex items-center justify-between text-sm">
                <div className="flex items-center text-gray-500">
                  <UserGroupIcon className="h-4 w-4 mr-1" />
                  <span>{subject.enrolled_students || 0} students</span>
                </div>
                <span className="text-gray-500">
                  {subject.instructor_name || 'No instructor assigned'}
                </span>
              </div>
              
              <div className="mt-4 text-xs text-gray-400">
                Academic Year: {subject.ACADEMICYEAR}
              </div>
            </div>
          </div>
        ))}
      </div>

      {subjects.length === 0 && (
        <div className="text-center py-12">
          <BookOpenIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">No subjects</h3>
          <p className="mt-1 text-sm text-gray-500">Get started by creating a new subject.</p>
          <div className="mt-6">
            <button
              onClick={() => setShowModal(true)}
              className="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
            >
              <PlusIcon className="h-5 w-5 mr-2" />
              Add Subject
            </button>
          </div>
        </div>
      )}

      {/* Modal */}
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
          <div className="relative top-20 mx-auto p-5 border w-11/12 md:w-3/4 lg:w-1/2 shadow-lg rounded-md bg-white">
            <div className="flex justify-between items-center mb-4">
              <h3 className="text-lg font-medium text-gray-900">
                {editingSubject ? 'Edit Subject' : 'Add New Subject'}
              </h3>
              <button
                onClick={closeModal}
                className="text-gray-400 hover:text-gray-600"
              >
                <span className="sr-only">Close</span>
                <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>

            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Subject Code *</label>
                  <input
                    type="text"
                    required
                    value={formData.SUBJECTCODE}
                    onChange={(e) => setFormData({...formData, SUBJECTCODE: e.target.value})}
                    className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500"
                    placeholder="e.g., CS101"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Instructor *</label>
                  <select
                    required
                    value={formData.INSTRUCTORID}
                    onChange={(e) => setFormData({...formData, INSTRUCTORID: e.target.value})}
                    className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500"
                  >
                    <option value="">Select Instructor</option>
                    {instructors.map((instructor) => (
                      <option key={instructor.USERID} value={instructor.USERID}>
                        {instructor.FIRSTNAME} {instructor.LASTNAME} ({instructor.EMPLOYEEID || instructor.FACULTYID})
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700">Subject Name *</label>
                <input
                  type="text"
                  required
                  value={formData.SUBJECTNAME}
                  onChange={(e) => setFormData({...formData, SUBJECTNAME: e.target.value})}
                  className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500"
                  placeholder="e.g., Introduction to Computer Science"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700">Description</label>
                <textarea
                  value={formData.DESCRIPTION}
                  onChange={(e) => setFormData({...formData, DESCRIPTION: e.target.value})}
                  rows={3}
                  className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500"
                  placeholder="Subject description (optional)"
                />
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Semester *</label>
                  <select
                    required
                    value={formData.SEMESTER}
                    onChange={(e) => setFormData({...formData, SEMESTER: e.target.value})}
                    className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500"
                  >
                    {semesterOptions.map((semester) => (
                      <option key={semester} value={semester}>{semester}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Year *</label>
                  <input
                    type="number"
                    required
                    min="2020"
                    max="2030"
                    value={formData.YEAR}
                    onChange={(e) => setFormData({...formData, YEAR: parseInt(e.target.value)})}
                    className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700">Academic Year *</label>
                  <input
                    type="text"
                    required
                    pattern="\d{4}-\d{4}"
                    value={formData.ACADEMICYEAR}
                    onChange={(e) => setFormData({...formData, ACADEMICYEAR: e.target.value})}
                    className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500"
                    placeholder="2024-2025"
                  />
                </div>
              </div>

              <div className="flex justify-end space-x-3 pt-4">
                <button
                  type="button"
                  onClick={closeModal}
                  className="px-4 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                >
                  {editingSubject ? 'Update' : 'Create'} Subject
                </button>
              </div>
            </form>
          </div>
        </div>,
        document.body
      )}

      {/* Student Assignment Modal */}
      {showStudentModal && selectedSubject && createPortal(
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
          <div className="relative top-10 mx-auto p-5 border w-11/12 md:w-4/5 lg:w-3/4 shadow-lg rounded-md bg-white">
            <div className="flex justify-between items-center mb-4">
              <h3 className="text-lg font-medium text-gray-900">
                Assign Students to {selectedSubject.SUBJECTCODE} - {selectedSubject.SUBJECTNAME}
              </h3>
              <button
                onClick={closeStudentModal}
                className="text-gray-400 hover:text-gray-600"
              >
                <span className="sr-only">Close</span>
                <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>

            {loadingStudents ? (
              <div className="flex justify-center items-center h-32">
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
              </div>
            ) : (
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* Available Students */}
                <div>
                  <h4 className="text-md font-medium text-gray-900 mb-3">
                    Available Students ({availableStudents.length})
                  </h4>
                  <div className="border border-gray-200 rounded-lg max-h-96 overflow-y-auto">
                    {availableStudents.length === 0 ? (
                      <div className="p-4 text-center text-gray-500">
                        No available students to assign
                      </div>
                    ) : (
                      <div className="divide-y divide-gray-200">
                        {availableStudents.map((student) => (
                          <div key={student.USERID} className="p-3 hover:bg-gray-50">
                            <div className="flex items-center justify-between">
                              <div className="flex-1">
                                <div className="flex items-center">
                                  <input
                                    type="checkbox"
                                    checked={selectedStudents.includes(student.USERID)}
                                    onChange={(e) => {
                                      if (e.target.checked) {
                                        setSelectedStudents([...selectedStudents, student.USERID]);
                                      } else {
                                        setSelectedStudents(selectedStudents.filter(id => id !== student.USERID));
                                      }
                                    }}
                                    className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded mr-3"
                                  />
                                  <div>
                                    <p className="text-sm font-medium text-gray-900">
                                      {student.FIRSTNAME} {student.LASTNAME}
                                    </p>
                                    <p className="text-xs text-gray-500">
                                      {student.STUDENTID} • {student.YEARLEVEL} • {student.DEPARTMENT}
                                    </p>
                                  </div>
                                </div>
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>

                {/* Enrolled Students */}
                <div>
                  <h4 className="text-md font-medium text-gray-900 mb-3">
                    Enrolled Students ({enrolledStudents.length})
                  </h4>
                  <div className="border border-gray-200 rounded-lg max-h-96 overflow-y-auto">
                    {enrolledStudents.length === 0 ? (
                      <div className="p-4 text-center text-gray-500">
                        No students enrolled yet
                      </div>
                    ) : (
                      <div className="divide-y divide-gray-200">
                        {enrolledStudents.map((student) => (
                          <div key={student.USERID} className="p-3 hover:bg-gray-50">
                            <div className="flex items-center justify-between">
                              <div className="flex-1">
                                <div>
                                  <p className="text-sm font-medium text-gray-900">
                                    {student.FIRSTNAME} {student.LASTNAME}
                                  </p>
                                  <p className="text-xs text-gray-500">
                                    {student.STUDENTID} • {student.YEARLEVEL} • {student.DEPARTMENT}
                                  </p>
                                  <p className="text-xs text-gray-400">
                                    Enrolled: {new Date(student.ENROLLMENTDATE).toLocaleDateString()}
                                  </p>
                                </div>
                              </div>
                              <button
                                onClick={() => handleRemoveStudent(student.USERID)}
                                className="ml-2 p-1 text-red-400 hover:text-red-600 transition-colors"
                                title="Remove Student"
                              >
                                <TrashIcon className="h-4 w-4" />
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            )}

            <div className="flex justify-end space-x-3 pt-6 border-t border-gray-200">
              <button
                type="button"
                onClick={closeStudentModal}
                className="px-4 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
              >
                Close
              </button>
              {selectedStudents.length > 0 && (
                <button
                  onClick={handleEnrollStudents}
                  className="px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
                >
                  Enroll {selectedStudents.length} Student{selectedStudents.length !== 1 ? 's' : ''}
                </button>
              )}
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

export default Subjects;
