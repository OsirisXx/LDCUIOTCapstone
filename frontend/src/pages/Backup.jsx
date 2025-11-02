import React, { useState, useEffect } from 'react';
import {
  ArrowDownTrayIcon,
  DocumentArrowDownIcon,
  ServerIcon,
  FolderIcon,
  ClockIcon,
  ExclamationTriangleIcon,
} from '@heroicons/react/24/outline';
import axios from 'axios';
import toast from 'react-hot-toast';

axios.defaults.baseURL = 'http://localhost:5000';

function Backup() {
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [stats, setStats] = useState(null);
  const [backups, setBackups] = useState([]);
  
  // Backup options
  const [includeDatabase, setIncludeDatabase] = useState(true);
  const [dbFormat, setDbFormat] = useState('both');
  const [includeFiles, setIncludeFiles] = useState(true);
  const [includeConfig, setIncludeConfig] = useState(true);

  useEffect(() => {
    fetchStats();
    fetchBackups();
  }, []);

  const fetchStats = async () => {
    try {
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/backup/stats', {
        headers: { Authorization: `Bearer ${token}` }
      });
      setStats(response.data);
    } catch (error) {
      console.error('Error fetching backup stats:', error);
      toast.error('Failed to load backup statistics');
    } finally {
      setLoading(false);
    }
  };

  const fetchBackups = async () => {
    try {
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/backup/list', {
        headers: { Authorization: `Bearer ${token}` }
      });
      setBackups(response.data.backups || []);
    } catch (error) {
      console.error('Error fetching backups:', error);
    }
  };

  const handleCreateBackup = async () => {
    try {
      setCreating(true);
      const token = localStorage.getItem('token');
      
      const response = await axios.post('/api/backup/create', {
        includeDatabase,
        dbFormat,
        includeFiles,
        includeConfig
      }, {
        headers: { Authorization: `Bearer ${token}` }
      });

      toast.success('Backup created successfully!');
      
      // Refresh backups list
      await fetchBackups();
      
      // Trigger download with auth header to avoid navigation without token
      const downloadUrl = `/api/backup/download/${response.data.filename}`;
      const downloadResponse = await axios.get(downloadUrl, {
        headers: { Authorization: `Bearer ${token}` },
        responseType: 'blob'
      });
      const blobUrl = window.URL.createObjectURL(new Blob([downloadResponse.data]));
      const link = document.createElement('a');
      link.href = blobUrl;
      link.download = response.data.filename;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(blobUrl);
    } catch (error) {
      console.error('Error creating backup:', error);
      toast.error(error.response?.data?.message || 'Failed to create backup');
    } finally {
      setCreating(false);
    }
  };

  const handleDownloadBackup = async (filename) => {
    try {
      const token = localStorage.getItem('token');
      const response = await axios.get(`/api/backup/download/${filename}`, {
        headers: { Authorization: `Bearer ${token}` },
        responseType: 'blob'
      });
      
      // Create blob URL and trigger download
      const url = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement('a');
      link.href = url;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
      
      toast.success('Backup downloaded successfully');
    } catch (error) {
      console.error('Error downloading backup:', error);
      toast.error('Failed to download backup');
    }
  };

  const formatDate = (dateString) => {
    if (!dateString) return 'N/A';
    return new Date(dateString).toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
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
      <div className="flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Backup & Export</h1>
          <p className="text-gray-600">Create comprehensive backups of your system data</p>
        </div>
      </div>

      {/* Statistics */}
      {stats && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <ServerIcon className="h-8 w-8 text-blue-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Database Size</p>
                <p className="text-2xl font-semibold text-gray-900">{stats.databaseSize} MB</p>
              </div>
            </div>
          </div>
          
          <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <FolderIcon className="h-8 w-8 text-green-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Uploaded Files</p>
                <p className="text-2xl font-semibold text-gray-900">{stats.uploadFileCount}</p>
                <p className="text-xs text-gray-500">{stats.uploadTotalSize} MB</p>
              </div>
            </div>
          </div>
          
          <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <DocumentArrowDownIcon className="h-8 w-8 text-purple-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Total Backups</p>
                <p className="text-2xl font-semibold text-gray-900">{backups.length}</p>
              </div>
            </div>
          </div>
          
          <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <ClockIcon className="h-8 w-8 text-indigo-500" />
              </div>
              <div className="ml-4">
                <p className="text-sm font-medium text-gray-500">Last Backup</p>
                <p className="text-sm font-semibold text-gray-900">
                  {stats.lastBackup ? formatDate(stats.lastBackup) : 'Never'}
                </p>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Backup Options */}
      <div className="bg-white rounded-lg shadow border border-gray-200">
        <div className="p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Backup Options</h2>
          
          <div className="space-y-4">
            {/* Database Backup */}
            <div className="border border-gray-200 rounded-lg p-4">
              <div className="flex items-start">
                <input
                  type="checkbox"
                  id="includeDatabase"
                  checked={includeDatabase}
                  onChange={(e) => setIncludeDatabase(e.target.checked)}
                  className="mt-1 h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                />
                <div className="ml-3 flex-1">
                  <label htmlFor="includeDatabase" className="text-sm font-medium text-gray-900 cursor-pointer">
                    Database Backup
                  </label>
                  <p className="text-sm text-gray-500 mt-1">
                    Include all tables and data from the MySQL database
                  </p>
                  
                  {includeDatabase && (
                    <div className="mt-3 ml-4">
                      <label className="text-sm text-gray-700 mb-2 block">Export Format:</label>
                      <div className="flex space-x-4">
                        <label className="flex items-center">
                          <input
                            type="radio"
                            name="dbFormat"
                            value="sql"
                            checked={dbFormat === 'sql'}
                            onChange={(e) => setDbFormat(e.target.value)}
                            className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300"
                          />
                          <span className="ml-2 text-sm text-gray-700">SQL Only</span>
                        </label>
                        <label className="flex items-center">
                          <input
                            type="radio"
                            name="dbFormat"
                            value="json"
                            checked={dbFormat === 'json'}
                            onChange={(e) => setDbFormat(e.target.value)}
                            className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300"
                          />
                          <span className="ml-2 text-sm text-gray-700">JSON Only</span>
                        </label>
                        <label className="flex items-center">
                          <input
                            type="radio"
                            name="dbFormat"
                            value="both"
                            checked={dbFormat === 'both'}
                            onChange={(e) => setDbFormat(e.target.value)}
                            className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300"
                          />
                          <span className="ml-2 text-sm text-gray-700">Both Formats</span>
                        </label>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* Uploaded Files */}
            <div className="border border-gray-200 rounded-lg p-4">
              <div className="flex items-start">
                <input
                  type="checkbox"
                  id="includeFiles"
                  checked={includeFiles}
                  onChange={(e) => setIncludeFiles(e.target.checked)}
                  className="mt-1 h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                />
                <div className="ml-3 flex-1">
                  <label htmlFor="includeFiles" className="text-sm font-medium text-gray-900 cursor-pointer">
                    Uploaded Files
                  </label>
                  <p className="text-sm text-gray-500 mt-1">
                    Include Excel files and other uploads from the system
                  </p>
                </div>
              </div>
            </div>

            {/* Configuration Files */}
            <div className="border border-gray-200 rounded-lg p-4">
              <div className="flex items-start">
                <input
                  type="checkbox"
                  id="includeConfig"
                  checked={includeConfig}
                  onChange={(e) => setIncludeConfig(e.target.checked)}
                  className="mt-1 h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
                />
                <div className="ml-3 flex-1">
                  <label htmlFor="includeConfig" className="text-sm font-medium text-gray-900 cursor-pointer">
                    Configuration Files
                  </label>
                  <p className="text-sm text-gray-500 mt-1">
                    Include package.json and sanitized environment files (passwords will be redacted)
                  </p>
                </div>
              </div>
            </div>
          </div>

          {/* Warning */}
          <div className="mt-6 bg-yellow-50 border border-yellow-200 rounded-md p-4">
            <div className="flex items-start">
              <ExclamationTriangleIcon className="h-5 w-5 text-yellow-600 mr-2 mt-0.5" />
              <div className="text-sm text-yellow-800">
                <p className="font-medium mb-1">Important:</p>
                <p>Ensure you have sufficient disk space. Large backups may take several minutes to complete.</p>
              </div>
            </div>
          </div>

          {/* Create Backup Button */}
          <div className="mt-6 flex justify-end">
            <button
              onClick={handleCreateBackup}
              disabled={creating || (!includeDatabase && !includeFiles && !includeConfig)}
              className="px-6 py-3 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center"
            >
              {creating ? (
                <>
                  <div className="animate-spin rounded-full h-5 w-5 border-b-2 border-white mr-2"></div>
                  Creating Backup...
                </>
              ) : (
                <>
                  <ArrowDownTrayIcon className="h-5 w-5 mr-2" />
                  Create Backup
                </>
              )}
            </button>
          </div>
        </div>
      </div>

      {/* Backup History */}
      <div className="bg-white rounded-lg shadow border border-gray-200">
        <div className="p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Backup History</h2>
          
          {backups.length === 0 ? (
            <div className="text-center py-12">
              <DocumentArrowDownIcon className="mx-auto h-12 w-12 text-gray-400" />
              <h3 className="mt-2 text-sm font-medium text-gray-900">No backups created yet</h3>
              <p className="mt-1 text-sm text-gray-500">Create your first backup to get started.</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Filename</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Size</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Created</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {backups.map((backup) => (
                    <tr key={backup.filename} className="hover:bg-gray-50">
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm font-medium text-gray-900">{backup.filename}</div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{backup.size} MB</td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(backup.date)}</td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm">
                        <button
                          onClick={() => handleDownloadBackup(backup.filename)}
                          className="text-blue-600 hover:text-blue-800 flex items-center"
                        >
                          <DocumentArrowDownIcon className="h-4 w-4 mr-1" />
                          Download
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default Backup;

