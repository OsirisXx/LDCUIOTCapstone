import React, { useEffect, useMemo, useState } from 'react';
import axios from 'axios';
import toast from 'react-hot-toast';
import {
  ShieldCheckIcon,
  ArrowPathIcon,
  MagnifyingGlassIcon,
  UserGroupIcon,
  AdjustmentsHorizontalIcon,
  ExclamationTriangleIcon
} from '@heroicons/react/24/outline';
import { useAuth } from '../contexts/AuthContext';

const ROLE_OPTIONS = ['student', 'instructor', 'admin', 'custodian', 'dean', 'superadmin'];
const STATUS_FILTERS = ['all', 'active', 'inactive'];

const statusStyles = {
  Active: 'bg-emerald-100 text-emerald-700 border-emerald-200',
  Inactive: 'bg-amber-100 text-amber-700 border-amber-200'
};

function SuperAdmin() {
  const { user } = useAuth();
  const [records, setRecords] = useState([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const [filters, setFilters] = useState({ role: 'all', status: 'all', search: '' });
  const [showStatusModal, setShowStatusModal] = useState(false);
  const [statusModalAnimation, setStatusModalAnimation] = useState('hidden');
  const [selectedRecord, setSelectedRecord] = useState(null);
  const [nextStatus, setNextStatus] = useState('');
  const [isUpdatingStatus, setIsUpdatingStatus] = useState(false);

  const isSelf = (record) => record.USERID === user?.id;

  const formatRoleLabel = (role) =>
    role === 'superadmin' ? 'Super Admin' : role.charAt(0).toUpperCase() + role.slice(1);

  const filteredParams = useMemo(() => {
    const params = { limit: 100 };

    if (filters.role !== 'all') {
      params.type = filters.role;
    }

    if (filters.status !== 'all') {
      params.status = filters.status === 'active' ? 'Active' : 'Inactive';
    }

    if (filters.search) {
      params.search = filters.search;
    }

    return params;
  }, [filters]);

  const loadUsers = async (showSpinner = true) => {
    try {
      if (showSpinner) {
        setLoading(true);
      } else {
        setRefreshing(true);
      }

      const { data } = await axios.get('/api/users', { params: filteredParams });
      setRecords(data?.users ?? []);
    } catch (error) {
      console.error('Failed to load users:', error);
      const message = error.response?.data?.message || 'Unable to load users.';
      toast.error(message);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    loadUsers(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filteredParams]);

  const handleSearchSubmit = (event) => {
    event.preventDefault();
    setFilters((prev) => ({ ...prev, search: searchInput.trim() }));
  };

  const handleResetFilters = () => {
    setSearchInput('');
    setFilters({ role: 'all', status: 'all', search: '' });
  };

  const handleRoleChange = async (record, nextRole) => {
    if (record.USERTYPE === nextRole) {
      return;
    }

    if (isSelf(record) && nextRole !== 'superadmin') {
      toast.error('You cannot downgrade your own role.');
      return;
    }

    const confirmation = window.confirm(`Change ${record.FIRSTNAME} ${record.LASTNAME}'s role to ${nextRole}?`);
    if (!confirmation) {
      return;
    }

    try {
      const { data } = await axios.patch(`/api/users/${record.USERID}/role`, { role: nextRole });
      toast.success('Role updated successfully.');
      const updated = data?.user;
      setRecords((previous) =>
        previous.map((item) =>
          item.USERID === record.USERID
            ? { ...item, ...updated, USERTYPE: updated?.USERTYPE ?? nextRole }
            : item
        )
      );
    } catch (error) {
      console.error('Role update failed:', error);
      const message = error.response?.data?.message || 'Failed to update role.';
      toast.error(message);
    }
  };

  const handleStatusToggle = (record) => {
    const newStatus = record.STATUS === 'Active' ? 'inactive' : 'active';
    setSelectedRecord(record);
    setNextStatus(newStatus);
    setShowStatusModal(true);
    setTimeout(() => setStatusModalAnimation('visible'), 10);
  };

  const closeStatusModal = () => {
    setStatusModalAnimation('hidden');
    setTimeout(() => {
      setShowStatusModal(false);
      setSelectedRecord(null);
      setNextStatus('');
    }, 300);
  };

  const confirmStatusToggle = async () => {
    if (!selectedRecord) return;

    setIsUpdatingStatus(true);
    try {
      const payload = { status: nextStatus };
      const { data } = await axios.put(`/api/users/${selectedRecord.USERID}`, payload);
      toast.success('Status updated.');
      const updated = data?.user;
      setRecords((previous) =>
        previous.map((item) =>
          item.USERID === selectedRecord.USERID
            ? { ...item, ...updated, STATUS: updated?.STATUS ?? (nextStatus === 'active' ? 'Active' : 'Inactive') }
            : item
        )
      );
      closeStatusModal();
    } catch (error) {
      console.error('Status update failed:', error);
      const message = error.response?.data?.message || 'Failed to update status.';
      toast.error(message);
    } finally {
      setIsUpdatingStatus(false);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-b from-gray-50 via-white to-gray-100">
      <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:px-8">
        <div className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <div className="inline-flex items-center space-x-3 rounded-full bg-maroon-50 px-4 py-2 text-maroon-700 ring-1 ring-maroon-100">
              <ShieldCheckIcon className="h-5 w-5" />
              <span className="text-sm font-semibold tracking-wide uppercase">Super Administrator Console</span>
            </div>
            <h1 className="mt-4 text-3xl font-extrabold text-gray-900 tracking-tight">Account Governance</h1>
            <p className="mt-2 max-w-2xl text-sm text-gray-600">
              Manage privileged access, enforce role hygiene, and deactivate compromised accounts. All actions are audited.
            </p>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => loadUsers(false)}
              className="inline-flex items-center rounded-lg bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm ring-1 ring-gray-200 transition-colors hover:bg-gray-50"
              disabled={refreshing}
            >
              <ArrowPathIcon className={`mr-2 h-5 w-5 ${refreshing ? 'animate-spin text-maroon-600' : 'text-gray-400'}`} />
              {refreshing ? 'Refreshing…' : 'Refresh'}
            </button>
          </div>
        </div>

        <div className="rounded-3xl border border-gray-200/70 bg-white/90 shadow-xl backdrop-blur-sm">
          <div className="border-b border-gray-200/60 p-6">
            <form onSubmit={handleSearchSubmit} className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <div className="col-span-2">
                <label className="block text-xs font-semibold uppercase tracking-wide text-gray-600">Search</label>
                <div className="mt-2 relative">
                  <MagnifyingGlassIcon className="pointer-events-none absolute inset-y-0 left-3 my-auto h-5 w-5 text-gray-400" />
                  <input
                    type="text"
                    className="w-full rounded-xl border border-gray-200 bg-white py-2.5 pl-10 pr-4 text-sm text-gray-900 shadow-sm focus:border-maroon-500 focus:ring-2 focus:ring-maroon-500/40"
                    placeholder="Search by name, student ID, or faculty ID"
                    value={searchInput}
                    onChange={(event) => setSearchInput(event.target.value)}
                  />
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wide text-gray-600">Role</label>
                <div className="mt-2">
                  <select
                    className="w-full rounded-xl border border-gray-200 bg-white py-2.5 px-3 text-sm text-gray-900 shadow-sm focus:border-maroon-500 focus:ring-2 focus:ring-maroon-500/40"
                    value={filters.role}
                    onChange={(event) => setFilters((prev) => ({ ...prev, role: event.target.value }))}
                  >
                    <option value="all">All roles</option>
                    {ROLE_OPTIONS.map((option) => (
                      <option key={option} value={option}>
                        {formatRoleLabel(option)}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wide text-gray-600">Status</label>
                <div className="mt-2">
                  <select
                    className="w-full rounded-xl border border-gray-200 bg-white py-2.5 px-3 text-sm text-gray-900 shadow-sm focus:border-maroon-500 focus:ring-2 focus:ring-maroon-500/40"
                    value={filters.status}
                    onChange={(event) => setFilters((prev) => ({ ...prev, status: event.target.value }))}
                  >
                    {STATUS_FILTERS.map((status) => (
                      <option key={status} value={status}>
                        {status === 'all' ? 'All statuses' : status.charAt(0).toUpperCase() + status.slice(1)}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="col-span-2 flex items-center gap-3 sm:col-span-1">
                <button
                  type="submit"
                  className="inline-flex w-full items-center justify-center rounded-xl bg-maroon-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition-transform hover:-translate-y-0.5 hover:bg-maroon-700 hover:shadow-lg focus:outline-none focus:ring-2 focus:ring-maroon-500/60"
                >
                  Apply filters
                </button>
                <button
                  type="button"
                  onClick={handleResetFilters}
                  className="inline-flex items-center justify-center rounded-xl border border-gray-200 bg-white px-4 py-2.5 text-sm font-semibold text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-maroon-500/40"
                >
                  Reset
                </button>
              </div>
            </form>
          </div>

          <div className="p-6">
            {loading ? (
              <div className="flex min-h-[240px] items-center justify-center">
                <div className="space-y-2 text-center">
                  <AdjustmentsHorizontalIcon className="mx-auto h-10 w-10 animate-pulse text-maroon-500/70" />
                  <p className="text-sm text-gray-500">Loading user registry…</p>
                </div>
              </div>
            ) : (
              <div className="overflow-hidden rounded-2xl border border-gray-200">
                <table className="min-w-full divide-y divide-gray-200 bg-white">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">User</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">Identifiers</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">Role</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">Status</th>
                      <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wide text-gray-600">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {records.length === 0 ? (
                      <tr>
                        <td colSpan={5} className="px-4 py-12 text-center text-sm text-gray-500">
                          No users found for the selected filters.
                        </td>
                      </tr>
                    ) : (
                      records.map((record) => (
                        <tr key={record.USERID} className={isSelf(record) ? 'bg-maroon-50/40' : ''}>
                          <td className="px-4 py-4">
                            <div className="flex items-center gap-3">
                              <div className="flex h-10 w-10 items-center justify-center rounded-full bg-maroon-100 text-maroon-700">
                                <UserGroupIcon className="h-5 w-5" />
                              </div>
                              <div>
                                <p className="text-sm font-semibold text-gray-900">
                                  {record.FIRSTNAME} {record.LASTNAME}
                                </p>
                                <p className="text-xs text-gray-500">{record.EMAIL || '—'}</p>
                              </div>
                            </div>
                          </td>
                          <td className="px-4 py-4 text-sm text-gray-700">
                            <div className="space-y-1">
                              {record.STUDENTID && <p className="font-mono text-xs text-gray-500">Student ID: {record.STUDENTID}</p>}
                              {record.FACULTYID && <p className="font-mono text-xs text-gray-500">Faculty ID: {record.FACULTYID}</p>}
                              {!record.STUDENTID && !record.FACULTYID && <p className="text-xs text-gray-400">No identifiers on file</p>}
                            </div>
                          </td>
                          <td className="px-4 py-4">
                            <select
                              className="w-full rounded-lg border border-gray-200 bg-white py-2 px-3 text-sm capitalize text-gray-900 shadow-sm focus:border-maroon-500 focus:ring-2 focus:ring-maroon-500/40"
                              value={record.USERTYPE}
                              onChange={(event) => handleRoleChange(record, event.target.value)}
                              disabled={isSelf(record)}
                            >
                              {ROLE_OPTIONS.map((option) => (
                                <option key={option} value={option}>
                                  {formatRoleLabel(option)}
                                </option>
                              ))}
                            </select>
                          </td>
                          <td className="px-4 py-4">
                            <span
                              className={`inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold ${statusStyles[record.STATUS] || 'bg-gray-100 text-gray-600 border-gray-200'}`}
                            >
                              {record.STATUS}
                            </span>
                          </td>
                          <td className="px-4 py-4 text-right">
                            <div className="flex justify-end gap-2">
                              <button
                                type="button"
                                onClick={() => handleStatusToggle(record)}
                                className="inline-flex items-center rounded-lg border border-gray-200 bg-white px-3 py-2 text-xs font-semibold text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-maroon-500/40"
                              >
                                {record.STATUS === 'Active' ? 'Deactivate' : 'Activate'}
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>

        <div className="mt-6 rounded-2xl border border-maroon-100 bg-maroon-50/80 p-4 text-sm text-maroon-800 shadow-sm">
          <p className="font-semibold">Operational safeguards</p>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li>Role modifications require Google-authenticated super administrator access. All requests are recorded server-side.</li>
            <li>Accounts without verified Supabase identities will be rejected during promotion attempts.</li>
            <li>Deactivate compromised accounts instead of deletion to preserve audit history.</li>
          </ul>
        </div>
      </div>

      {/* Status Toggle Confirmation Modal */}
      {showStatusModal && selectedRecord && (
        <div className="fixed inset-0 overflow-y-auto z-[60]">
          {/* Backdrop with blur and fade animation */}
          <div 
            className={`fixed inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-300 ease-in-out ${
              statusModalAnimation === 'visible' ? 'opacity-100' : 'opacity-0'
            }`}
            onClick={closeStatusModal}
          >
            <div className="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
              {/* Modal container with scale and fade animation */}
              <div 
                className={`relative transform overflow-hidden rounded-2xl bg-white text-left shadow-2xl transition-all duration-300 ease-out sm:my-8 sm:w-full sm:max-w-lg ${
                  statusModalAnimation === 'visible' 
                    ? 'scale-100 opacity-100 translate-y-0' 
                    : 'scale-95 opacity-0 translate-y-4'
                }`}
                onClick={(e) => e.stopPropagation()}
              >
                {/* Gradient header */}
                <div className={`bg-gradient-to-r px-6 py-8 ${
                  nextStatus === 'inactive' 
                    ? 'from-amber-50 to-orange-100' 
                    : 'from-emerald-50 to-green-100'
                }`}>
                  <div className={`mx-auto flex items-center justify-center h-16 w-16 rounded-full shadow-lg mb-4 ${
                    nextStatus === 'inactive' 
                      ? 'bg-amber-100 animate-pulse' 
                      : 'bg-emerald-100 animate-pulse'
                  }`}>
                    <ExclamationTriangleIcon className={`h-8 w-8 ${
                      nextStatus === 'inactive' ? 'text-amber-600' : 'text-emerald-600'
                    }`} />
                  </div>
                  <h3 className="text-xl font-semibold text-gray-900 mb-2">
                    {nextStatus === 'inactive' ? 'Deactivate Account' : 'Activate Account'}
                  </h3>
                  <p className="text-sm text-gray-600 leading-relaxed">
                    Are you sure you want to {nextStatus === 'inactive' ? 'deactivate' : 'activate'} the account for{' '}
                    <strong className="font-semibold text-gray-900">
                      {selectedRecord.FIRSTNAME} {selectedRecord.LASTNAME}
                    </strong>?
                  </p>
                  {nextStatus === 'inactive' && (
                    <div className="mt-4 p-3 bg-amber-50 border border-amber-200 rounded-lg">
                      <p className="text-sm text-amber-800 font-medium">
                        ⚠️ Deactivated accounts will not be able to access the system until reactivated.
                      </p>
                    </div>
                  )}
                </div>
                
                {/* Action buttons */}
                <div className="bg-gray-50 px-6 py-4 flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-3 space-y-3 space-y-reverse sm:space-y-0">
                  <button
                    type="button"
                    onClick={closeStatusModal}
                    disabled={isUpdatingStatus}
                    className="w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-gray-300 shadow-sm text-sm font-medium rounded-xl text-gray-700 bg-white hover:bg-gray-50 hover:border-gray-400 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-all duration-200 ease-in-out transform hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={confirmStatusToggle}
                    disabled={isUpdatingStatus}
                    className={`w-full sm:w-auto inline-flex justify-center items-center px-6 py-3 border border-transparent shadow-sm text-sm font-medium rounded-xl text-white bg-gradient-to-r focus:outline-none focus:ring-2 focus:ring-offset-2 transition-all duration-200 ease-in-out transform hover:scale-105 hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed ${
                      nextStatus === 'inactive'
                        ? 'from-amber-600 to-orange-600 hover:from-amber-700 hover:to-orange-700 focus:ring-amber-500'
                        : 'from-emerald-600 to-green-600 hover:from-emerald-700 hover:to-green-700 focus:ring-emerald-500'
                    }`}
                  >
                    {isUpdatingStatus ? (
                      <>
                        <ArrowPathIcon className="mr-2 h-4 w-4 animate-spin" />
                        {nextStatus === 'inactive' ? 'Deactivating...' : 'Activating...'}
                      </>
                    ) : (
                      nextStatus === 'inactive' ? 'Deactivate Account' : 'Activate Account'
                    )}
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

export default SuperAdmin;

