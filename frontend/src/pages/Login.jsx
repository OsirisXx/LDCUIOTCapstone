import React from 'react';
import { useAuth } from '../contexts/AuthContext';
import { ArrowRightIcon } from '@heroicons/react/24/outline';

function Login() {
  const { login, loading } = useAuth();

  const handleGoogleSignIn = async () => {
    await login();
  };

  return (
    <div
      className="min-h-screen py-10 px-4 sm:px-6 lg:px-8 flex items-center justify-center"
      style={{ backgroundImage: 'url(/bg.png)', backgroundSize: 'cover', backgroundPosition: 'center' }}
    >
      <div className="relative w-full max-w-5xl">
        <div className="absolute inset-0 rounded-3xl bg-white/10 backdrop-blur-sm border-2 border-maroon-600/60" />
        <div className="relative grid grid-cols-1 md:grid-cols-2 overflow-hidden rounded-3xl shadow-2xl border-2 border-maroon-700 ring-2 ring-white/50">
          <div
            className="hidden md:block p-10 md:border-r-2 md:border-maroon-600/40"
            style={{ background: 'linear-gradient(135deg, rgba(128,0,32,0.85) 0%, rgba(38,10,22,0.85) 100%)' }}
          >
            <div className="h-full w-full rounded-2xl border-2 border-white/30 bg-white/5 backdrop-blur-md p-8 flex flex-col justify-center">
              <div className="mb-6 inline-flex items-center px-3 py-1 rounded-full text-xs font-medium text-white/90 bg-white/10 ring-1 ring-white/20 w-max">
                Secure Access
              </div>
              <h2 className="text-3xl font-extrabold text-white leading-tight">IoT Attendance System</h2>
              <p className="mt-2 text-white/80">Liceo de Cagayan University</p>
              <p className="mt-8 text-sm text-white/70">
                Sign in with your authorized Google workspace account to manage sessions, monitor attendance, and administer users.
              </p>
            </div>
          </div>

          <div className="p-6 sm:p-8 md:p-10 bg-white/85 backdrop-blur-xl md:border-l-2 md:border-maroon-600/40">
            <div className="mx-auto w-full max-w-sm">
              <div className="md:hidden mb-6">
                <h2 className="text-2xl font-extrabold text-gray-900">IoT Attendance System</h2>
                <p className="text-sm text-gray-600">Liceo de Cagayan University</p>
                <p className="mt-1 text-xs text-gray-500">Use your institutional Google account</p>
              </div>

              <div className="mt-6 space-y-6">
                <button
                  type="button"
                  onClick={handleGoogleSignIn}
                  disabled={loading}
                  className="group relative inline-flex w-full items-center justify-center rounded-2xl bg-white/95 py-4 px-6 text-sm font-semibold text-gray-900 shadow-xl ring-1 ring-inset ring-gray-200 transition-all duration-300 hover:-translate-y-1 hover:shadow-2xl hover:ring-gray-300 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-maroon-500/50 active:translate-y-0 disabled:opacity-60 disabled:cursor-not-allowed"
                >
                  <div className="absolute inset-0 rounded-2xl bg-gradient-to-r from-white/50 via-transparent to-white/30 opacity-0 transition-opacity duration-300 group-hover:opacity-100" />
                  <div className="relative flex items-center gap-3">
                    {loading ? (
                      <div className="animate-spin rounded-full h-5 w-5 border-b-2 border-maroon-600" />
                    ) : (
                      <svg className="h-5 w-5" viewBox="0 0 533.5 544.3" aria-hidden="true">
                        <path fill="#4285F4" d="M533.5 278.4c0-17.4-1.6-34.1-4.6-50.4H272v95.4h146.5c-6.3 33.7-25 62.3-53.2 81.3v67.7h85.8c50.2-46.3 82.4-114.6 82.4-194z" />
                        <path fill="#34A853" d="M272 544.3c72.2 0 132.8-23.9 177.1-64.9l-85.8-67.7c-23.9 16.1-54.5 25.8-91.3 25.8-70 0-129.3-47.2-150.5-110.5H32.2v69.4c44.3 87.8 135.2 147.9 239.8 147.9z" />
                        <path fill="#FBBC05" d="M121.5 327c-10.7-31.8-10.7-66.2 0-98l-69.3-69.4H32.2c-37 73.4-37 160.3 0 233.7l89.3-66.3z" />
                        <path fill="#EA4335" d="M272 107.7c38.9-.6 76.1 14.7 104.4 42.6l78.1-78.1C404 24.1 343.4 0 272 0 167.4 0 76.5 60.1 32.2 147.9l89.3 69.4C142.7 152.2 202 107.7 272 107.7z" />
                      </svg>
                    )}
                    <span>{loading ? 'Redirecting…' : 'Continue with Google'}</span>
                    <ArrowRightIcon className="h-4 w-4 text-maroon-600 transition-transform duration-300 group-hover:translate-x-1" />
                  </div>
                </button>

                <div className="rounded-2xl border border-gray-200/70 bg-white/60 p-4 text-xs text-gray-600">
                  <p className="font-semibold text-gray-700">Security notice</p>
                  <p className="mt-2">
                    Access is restricted to authorized institutional Google accounts. Sharing credentials or attempting access without authorization is prohibited and will be logged.
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default Login;