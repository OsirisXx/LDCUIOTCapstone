import React, { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LockClosedIcon, EnvelopeIcon, CheckCircleIcon } from '@heroicons/react/24/outline';

function Settings() {
  const { user, setPassword, changePassword } = useAuth();
  const [formData, setFormData] = useState({
    currentPassword: '',
    newPassword: '',
    confirmPassword: ''
  });
  const [loading, setLoading] = useState(false);
  const [validationError, setValidationError] = useState('');

  const hasPassword = user?.has_password;

  const handleChange = (e) => {
    setFormData({
      ...formData,
      [e.target.name]: e.target.value
    });
    // Clear validation error when user types
    setValidationError('');
  };

  const validatePassword = (password) => {
    if (password.length < 8) {
      return 'Password must be at least 8 characters long';
    }
    if (!/[A-Z]/.test(password)) {
      return 'Password must include at least one uppercase letter';
    }
    if (!/\d/.test(password)) {
      return 'Password must include at least one number';
    }
    if (!/[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]/.test(password)) {
      return 'Password must include at least one special character';
    }
    return null;
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);
    setValidationError('');

    try {
      // Validate new password
      const passwordError = validatePassword(formData.newPassword);
      if (passwordError) {
        setValidationError(passwordError);
        return;
      }

      // Check if passwords match
      if (formData.newPassword !== formData.confirmPassword) {
        setValidationError('Passwords do not match');
        return;
      }

      let result;
      if (hasPassword) {
        // Change existing password
        result = await changePassword(
          formData.currentPassword,
          formData.newPassword,
          formData.confirmPassword
        );
      } else {
        // Set initial password
        result = await setPassword(
          formData.newPassword,
          formData.confirmPassword
        );
      }

      if (result.success) {
        // Clear form
        setFormData({
          currentPassword: '',
          newPassword: '',
          confirmPassword: ''
        });
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Account Settings</h1>
        <p className="text-gray-600">Manage your account preferences and security settings</p>
      </div>

      {/* Account Information */}
      <div className="card">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Account Information</h2>
        <div className="space-y-4">
          <div>
            <label className="label">Email Address</label>
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <EnvelopeIcon className="h-5 w-5 text-gray-400" />
              </div>
              <input
                type="email"
                value={user?.email || ''}
                disabled
                className="input pl-10 bg-gray-50 cursor-not-allowed"
              />
            </div>
            <p className="mt-1 text-sm text-gray-500">Email address cannot be changed</p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="label">First Name</label>
              <input
                type="text"
                value={user?.first_name || ''}
                disabled
                className="input bg-gray-50 cursor-not-allowed"
              />
            </div>
            <div>
              <label className="label">Last Name</label>
              <input
                type="text"
                value={user?.last_name || ''}
                disabled
                className="input bg-gray-50 cursor-not-allowed"
              />
            </div>
          </div>

          <div>
            <label className="label">Role</label>
            <input
              type="text"
              value={user?.role ? user.role.charAt(0).toUpperCase() + user.role.slice(1) : ''}
              disabled
              className="input bg-gray-50 cursor-not-allowed capitalize"
            />
          </div>
        </div>
      </div>

      {/* Password Settings */}
      <div className="card">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">
              {hasPassword ? 'Change Password' : 'Set Password'}
            </h2>
            <p className="text-sm text-gray-600 mt-1">
              {hasPassword 
                ? 'Update your password to keep your account secure' 
                : 'Create a password to enable email/password login'}
            </p>
          </div>
          {hasPassword && (
            <div className="flex items-center text-sm text-success-600">
              <CheckCircleIcon className="h-5 w-5 mr-1" />
              Password Set
            </div>
          )}
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {validationError && (
            <div className="rounded-lg bg-danger-50 border border-danger-200 p-3">
              <p className="text-sm text-danger-700">{validationError}</p>
            </div>
          )}

          {hasPassword && (
            <div>
              <label htmlFor="currentPassword" className="label">
                Current Password
              </label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <LockClosedIcon className="h-5 w-5 text-gray-400" />
                </div>
                <input
                  id="currentPassword"
                  name="currentPassword"
                  type="password"
                  autoComplete="current-password"
                  required
                  value={formData.currentPassword}
                  onChange={handleChange}
                  className="input pl-10"
                  placeholder="Enter current password"
                  disabled={loading}
                />
              </div>
            </div>
          )}

          <div>
            <label htmlFor="newPassword" className="label">
              {hasPassword ? 'New Password' : 'Create Password'}
            </label>
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <LockClosedIcon className="h-5 w-5 text-gray-400" />
              </div>
              <input
                id="newPassword"
                name="newPassword"
                type="password"
                autoComplete="new-password"
                required
                value={formData.newPassword}
                onChange={handleChange}
                className="input pl-10"
                placeholder="Enter new password (min. 8 chars, uppercase, number, special)"
                disabled={loading}
                minLength={8}
              />
            </div>
          </div>

          <div>
            <label htmlFor="confirmPassword" className="label">
              Confirm Password
            </label>
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <LockClosedIcon className="h-5 w-5 text-gray-400" />
              </div>
              <input
                id="confirmPassword"
                name="confirmPassword"
                type="password"
                autoComplete="new-password"
                required
                value={formData.confirmPassword}
                onChange={handleChange}
                className="input pl-10"
                placeholder="Confirm new password"
                disabled={loading}
                minLength={8}
              />
            </div>
          </div>

          <div className="flex items-center justify-between pt-4">
            <div className="text-sm text-gray-600">
              <p>Password requirements:</p>
              <ul className="list-disc list-inside mt-1 space-y-1">
                <li>At least 8 characters long</li>
                <li>Include at least one uppercase letter (A-Z)</li>
                <li>Include at least one number (0-9)</li>
                <li>Include at least one special character (e.g., !@#$%)</li>
                <li>Passwords must match</li>
              </ul>
            </div>
            <button
              type="submit"
              disabled={loading}
              className="btn-primary"
            >
              {loading ? (
                <div className="flex items-center">
                  <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2" />
                  Saving...
                </div>
              ) : (
                hasPassword ? 'Update Password' : 'Set Password'
              )}
            </button>
          </div>
        </form>
      </div>

    </div>
  );
}

export default Settings;

