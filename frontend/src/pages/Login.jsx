import React, { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { EyeIcon, EyeSlashIcon } from '@heroicons/react/24/outline';

function Login() {
  const [formData, setFormData] = useState({
    email: '',
    password: ''
  });
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);
    
    try {
      await login(formData.email, formData.password);
    } catch (error) {
      console.error('Login error:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleChange = (e) => {
    setFormData({
      ...formData,
      [e.target.name]: e.target.value
    });
  };

  return (
    <div className="min-h-screen py-10 px-4 sm:px-6 lg:px-8 flex items-center justify-center" style={{ backgroundImage: 'url(/bg.png)', backgroundSize: 'cover', backgroundPosition: 'center' }}>
      <div className="relative w-full max-w-5xl">
        <div className="absolute inset-0 rounded-3xl bg-white/10 backdrop-blur-sm border-2 border-maroon-600/60" />
        <div className="relative grid grid-cols-1 md:grid-cols-2 overflow-hidden rounded-3xl shadow-2xl border-2 border-maroon-700 ring-2 ring-white/50">
          <div className="hidden md:block p-10 md:border-r-2 md:border-maroon-600/40" style={{ background: 'linear-gradient(135deg, rgba(128,0,32,0.85) 0%, rgba(38,10,22,0.85) 100%)' }}>
            <div className="h-full w-full rounded-2xl border-2 border-white/30 bg-white/5 backdrop-blur-md p-8 flex flex-col justify-center">
              <div className="mb-6 inline-flex items-center px-3 py-1 rounded-full text-xs font-medium text-white/90 bg-white/10 ring-1 ring-white/20 w-max">Secure Access</div>
              <h2 className="text-3xl font-extrabold text-white leading-tight">IoT Attendance System</h2>
              <p className="mt-2 text-white/80">Liceo de Cagayan University</p>
              <p className="mt-8 text-sm text-white/70">Sign in to your account to manage sessions, monitor attendance, and administer users.</p>
            </div>
          </div>

          <div className="p-6 sm:p-8 md:p-10 bg-white/85 backdrop-blur-xl md:border-l-2 md:border-maroon-600/40">
            <div className="mx-auto w-full max-w-sm">
              <div className="md:hidden mb-6">
                <h2 className="text-2xl font-extrabold text-gray-900">IoT Attendance System</h2>
                <p className="text-sm text-gray-600">Liceo de Cagayan University</p>
                <p className="mt-1 text-xs text-gray-500">Sign in to your account</p>
              </div>

              <form className="mt-6 space-y-6" onSubmit={handleSubmit}>
                <div className="space-y-5">
                  {/* Email Field */}
                  <div className="group relative">
                    <div className="relative">
                      <input
                        id="email"
                        name="email"
                        type="email"
                        autoComplete="email"
                        required
                        className="peer relative z-10 block w-full rounded-2xl border-0 bg-white/70 backdrop-blur-sm px-6 py-4 pl-6 text-gray-900 placeholder-transparent shadow-lg ring-1 ring-inset ring-gray-200/60 transition-all duration-300 focus:bg-white/90 focus:ring-2 focus:ring-maroon-500/50 focus:ring-offset-2 focus:ring-offset-transparent hover:bg-white/80 hover:ring-gray-300/60"
                        placeholder="Email address"
                        value={formData.email}
                        onChange={handleChange}
                      />
                      <label
                        htmlFor="email"
                        className="absolute left-6 -top-2.5 z-20 text-sm font-medium text-gray-600 bg-white/90 px-2 rounded-full transition-all duration-300 peer-placeholder-shown:text-base peer-placeholder-shown:text-gray-500 peer-placeholder-shown:top-4 peer-placeholder-shown:bg-transparent peer-placeholder-shown:px-0 peer-focus:-top-2.5 peer-focus:text-sm peer-focus:text-maroon-600 peer-focus:bg-white/90 peer-focus:px-2 peer-hover:text-maroon-500 pointer-events-none"
                      >
                        Email address
                      </label>
                      <div className="absolute inset-0 rounded-2xl bg-gradient-to-r from-maroon-500/10 to-primary-500/10 opacity-0 transition-opacity duration-300 peer-focus:opacity-100 pointer-events-none"></div>
                    </div>
                  </div>

                  {/* Password Field */}
                  <div className="group relative">
                    <div className="relative">
                      <input
                        id="password"
                        name="password"
                        type={showPassword ? 'text' : 'password'}
                        autoComplete="current-password"
                        required
                        className="peer relative z-10 block w-full rounded-2xl border-0 bg-white/70 backdrop-blur-sm px-6 py-4 pl-6 pr-14 text-gray-900 placeholder-transparent shadow-lg ring-1 ring-inset ring-gray-200/60 transition-all duration-300 focus:bg-white/90 focus:ring-2 focus:ring-maroon-500/50 focus:ring-offset-2 focus:ring-offset-transparent hover:bg-white/80 hover:ring-gray-300/60"
                        placeholder="Password"
                        value={formData.password}
                        onChange={handleChange}
                      />
                      <label
                        htmlFor="password"
                        className="absolute left-6 -top-2.5 z-20 text-sm font-medium text-gray-600 bg-white/90 px-2 rounded-full transition-all duration-300 peer-placeholder-shown:text-base peer-placeholder-shown:text-gray-500 peer-placeholder-shown:top-4 peer-placeholder-shown:bg-transparent peer-placeholder-shown:px-0 peer-focus:-top-2.5 peer-focus:text-sm peer-focus:text-maroon-600 peer-focus:bg-white/90 peer-focus:px-2 peer-hover:text-maroon-500 pointer-events-none"
                      >
                        Password
                      </label>
                      <button
                        type="button"
                        className="absolute inset-y-0 right-0 z-30 flex items-center pr-4 text-gray-400 transition-colors duration-200 hover:text-maroon-500 focus:text-maroon-500 focus:outline-none"
                        onClick={() => setShowPassword(!showPassword)}
                      >
                        <div className="rounded-full p-1.5 hover:bg-maroon-50 transition-colors duration-200">
                          {showPassword ? (
                            <EyeSlashIcon className="h-5 w-5" />
                          ) : (
                            <EyeIcon className="h-5 w-5" />
                          )}
                        </div>
                      </button>
                      <div className="absolute inset-0 rounded-2xl bg-gradient-to-r from-maroon-500/10 to-primary-500/10 opacity-0 transition-opacity duration-300 peer-focus:opacity-100 pointer-events-none"></div>
                    </div>
                  </div>
                </div>

                <button
                  type="submit"
                  disabled={loading}
                  className="group relative mt-4 inline-flex w-full items-center justify-center rounded-2xl bg-gradient-to-r from-maroon-600 to-maroon-700 py-4 px-6 text-sm font-semibold text-white shadow-xl ring-1 ring-inset ring-maroon-700/20 transition-all duration-300 hover:from-maroon-700 hover:to-maroon-800 hover:-translate-y-1 hover:shadow-2xl focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-maroon-500/50 active:translate-y-0 disabled:opacity-50 disabled:cursor-not-allowed disabled:transform-none"
                >
                  <div className="absolute inset-0 rounded-2xl bg-gradient-to-r from-white/20 to-transparent opacity-0 transition-opacity duration-300 group-hover:opacity-100"></div>
                  <div className="relative flex items-center">
                    {loading ? (
                      <>
                        <div className="animate-spin rounded-full h-5 w-5 border-b-2 border-white mr-3"></div>
                        Signing in...
                      </>
                    ) : (
                      <>
                        Sign in
                        <svg className="ml-2 h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7l5 5m0 0l-5 5m5-5H6" />
                        </svg>
                      </>
                    )}
                  </div>
                </button>
              </form>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default Login; 