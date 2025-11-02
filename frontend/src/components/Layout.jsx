import React, { useState, useEffect, useRef } from 'react';
import { Outlet, Link, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import {
  HomeIcon,
  ClipboardDocumentListIcon,
  ClockIcon,
  UsersIcon,
  Squares2X2Icon,
  Bars3Icon,
  XMarkIcon,
  ChevronDownIcon,
  UserCircleIcon,
  ArrowRightOnRectangleIcon,
  ChartBarIcon,
  ArchiveBoxIcon,
  ArrowDownTrayIcon,
} from '@heroicons/react/24/outline';

const navigation = [
  { name: 'Dashboard', href: '/dashboard', icon: HomeIcon },
  { name: 'Academic Management', href: '/management', icon: Squares2X2Icon },
  { name: 'Attendance Logs', href: '/attendance-logs', icon: ClipboardDocumentListIcon },
  { name: 'Reports', href: '/reports', icon: ChartBarIcon },
  { name: 'Sessions', href: '/sessions', icon: ClockIcon },
  { name: 'Users', href: '/users', icon: UsersIcon },
  { name: 'Archive', href: '/archive', icon: ArchiveBoxIcon, adminOnly: true },
  { name: 'Backup & Export', href: '/backup', icon: ArrowDownTrayIcon, adminOnly: true },
];

function Layout() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [desktopSidebarCollapsed, setDesktopSidebarCollapsed] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const { user, logout } = useAuth();
  const location = useLocation();
  const userMenuRef = useRef(null);

  const isActive = (href) => location.pathname === href;

  // Handle click outside to close user menu
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (userMenuRef.current && !userMenuRef.current.contains(event.target)) {
        setUserMenuOpen(false);
      }
    };

    if (userMenuOpen) {
      document.addEventListener('mousedown', handleClickOutside);
    }

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [userMenuOpen]);

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Mobile sidebar */}
      <div className={`fixed inset-0 z-40 lg:hidden ${sidebarOpen ? '' : 'pointer-events-none'}`}>
        <div className={`fixed inset-0 bg-maroon-900 bg-opacity-30 backdrop-blur-sm transition-opacity ${sidebarOpen ? 'opacity-100' : 'opacity-0'}`} 
             onClick={() => setSidebarOpen(false)} />
        
        <div className={`fixed inset-y-0 left-0 flex w-64 flex-col transform transition-transform ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}`}
             style={{
               background: 'linear-gradient(135deg, #1a0611 0%, #2d0a1a 25%, #4a1528 50%, #2d0a1a 75%, #1a0611 100%)'
             }}>
          <div className="absolute inset-0"
               style={{
                 background: 'rgba(128, 0, 32, 0.15)',
                 backdropFilter: 'blur(20px)',
                 WebkitBackdropFilter: 'blur(20px)',
                 border: '1px solid rgba(128, 0, 32, 0.2)',
                 boxShadow: '0 8px 32px 0 rgba(128, 0, 32, 0.2)'
               }}>
          </div>
          <div className="relative z-10 flex flex-col h-full">
            <div className="flex h-16 items-center justify-between px-4 border-b border-maroon-200/30">
              <img className="h-8 w-auto" src="/ldcu.png" alt="Liceo de Cagayan University" />
              <div className="ml-3">
                <h1 className="text-sm font-semibold text-white">IoT Attendance</h1>
                <p className="text-xs text-maroon-100">Liceo de Cagayan</p>
              </div>
              <button onClick={() => setSidebarOpen(false)} className="text-maroon-200 hover:text-white transition-colors">
                <XMarkIcon className="h-6 w-6" />
              </button>
            </div>
          <nav className="flex-1 space-y-1 px-2 py-4">
            {navigation.map((item) => {
              // Only show Archive for admins
              if (item.adminOnly && user?.role !== 'admin') return null;
              
              return (
                <Link
                  key={item.name}
                  to={item.href}
                  className={`group flex items-center px-3 py-2.5 text-sm font-medium rounded-lg transition-all duration-200 ${
                    isActive(item.href)
                      ? 'bg-white/20 text-white shadow-lg backdrop-blur-sm border border-white/10'
                      : 'text-maroon-100 hover:bg-white/10 hover:text-white hover:backdrop-blur-sm'
                  }`}
                  onClick={() => setSidebarOpen(false)}
                >
                  <item.icon className="mr-3 h-5 w-5 flex-shrink-0" />
                  {item.name}
                </Link>
              );
            })}
          </nav>
          </div>
        </div>
      </div>

      {/* Desktop sidebar */}
      <div className={`hidden lg:fixed lg:inset-y-0 lg:flex lg:flex-col transition-all duration-300 ${desktopSidebarCollapsed ? 'lg:w-16' : 'lg:w-64'}`}
           style={{
             background: 'linear-gradient(135deg, #1a0611 0%, #2d0a1a 25%, #4a1528 50%, #2d0a1a 75%, #1a0611 100%)'
           }}>
        <div className="flex flex-col flex-grow relative"
             style={{
               background: 'rgba(128, 0, 32, 0.15)',
               backdropFilter: 'blur(20px)',
               WebkitBackdropFilter: 'blur(20px)',
               border: '1px solid rgba(128, 0, 32, 0.2)',
               boxShadow: '0 8px 32px 0 rgba(128, 0, 32, 0.2)'
             }}>
          <div className="flex h-16 items-center px-4 border-b border-maroon-200/30">
            <button
              onClick={() => setDesktopSidebarCollapsed(!desktopSidebarCollapsed)}
              className="mr-3 text-maroon-200 hover:text-white transition-colors duration-200 p-1 rounded-md hover:bg-white/10"
              title="Toggle Sidebar"
            >
              <Bars3Icon className="h-5 w-5" />
            </button>
            {!desktopSidebarCollapsed && (
              <>
                <img className="h-8 w-auto" src="/ldcu.png" alt="Liceo de Cagayan University" />
                <div className="ml-3">
                  <h1 className="text-sm font-semibold text-white">IoT Attendance</h1>
                  <p className="text-xs text-maroon-100">Liceo de Cagayan</p>
                </div>
              </>
            )}
          </div>
          <nav className="flex-1 space-y-1 px-2 py-4">
            {navigation.map((item) => {
              // Only show Archive for admins
              if (item.adminOnly && user?.role !== 'admin') return null;
              
              return (
                <Link
                  key={item.name}
                  to={item.href}
                  className={`group flex items-center ${desktopSidebarCollapsed ? 'px-2 py-3 justify-center' : 'px-3 py-2.5'} text-sm font-medium rounded-lg transition-all duration-200 ${
                    isActive(item.href)
                      ? 'bg-white/20 text-white shadow-lg backdrop-blur-sm border border-white/10'
                      : 'text-maroon-100 hover:bg-white/10 hover:text-white hover:backdrop-blur-sm'
                  }`}
                  title={desktopSidebarCollapsed ? item.name : ''}
                >
                  <item.icon className={`${desktopSidebarCollapsed ? 'h-6 w-6' : 'mr-3 h-5 w-5'} flex-shrink-0`} />
                  {!desktopSidebarCollapsed && item.name}
                </Link>
              );
            })}
          </nav>
          
          {/* Logo at bottom when collapsed */}
          {desktopSidebarCollapsed && (
            <div className="p-3 border-t border-maroon-200/30">
              <div className="flex justify-center">
                <img 
                  className="h-8 w-8 rounded-md opacity-80 hover:opacity-100 transition-opacity duration-200" 
                  src="/ldcu.png" 
                  alt="Liceo de Cagayan University" 
                  title="Liceo de Cagayan University"
                />
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Main content */}
      <div className={`transition-all duration-300 ${desktopSidebarCollapsed ? 'lg:pl-16' : 'lg:pl-64'}`}>
        {/* Top bar */}
        <div className="sticky top-0 z-10 relative"
             style={{
               background: 'linear-gradient(135deg, #1a0611 0%, #2d0a1a 25%, #4a1528 50%, #2d0a1a 75%, #1a0611 100%)'
             }}>
          <div className="absolute inset-0"
               style={{
                 background: 'rgba(128, 0, 32, 0.15)',
                 backdropFilter: 'blur(20px)',
                 WebkitBackdropFilter: 'blur(20px)',
                 border: '1px solid rgba(128, 0, 32, 0.2)',
                 borderTop: 'none',
                 borderLeft: 'none',
                 borderRight: 'none'
               }}>
          </div>
          <div className="relative z-10 flex h-16 items-center justify-between px-4 sm:px-6 lg:px-8">
            {/* Left side - Mobile menu button */}
            <div className="flex items-center">
              <button
                onClick={() => setSidebarOpen(true)}
                className="lg:hidden text-maroon-200 hover:text-white transition-colors duration-200 p-1 rounded-md hover:bg-white/10"
              >
                <Bars3Icon className="h-6 w-6" />
              </button>
            </div>

            {/* Right side - System status and user menu */}
            <div className="flex items-center space-x-4">
              {/* System status indicators */}
              <div className="flex items-center space-x-2">
                <div className="flex items-center space-x-1">
                  <div className="h-2 w-2 bg-green-400 rounded-full animate-pulse"></div>
                  <span className="text-xs text-white/80">System Online</span>
                </div>
              </div>

              {/* User menu */}
              <div className="relative" ref={userMenuRef}>
                <button
                  onClick={() => setUserMenuOpen(!userMenuOpen)}
                  className="flex items-center space-x-2 text-sm text-white hover:text-maroon-100 transition-colors duration-200"
                >
                  <UserCircleIcon className="h-8 w-8 text-maroon-200" />
                  <span className="hidden sm:block">{user?.first_name} {user?.last_name}</span>
                  <ChevronDownIcon className="h-4 w-4" />
                </button>

                <div className={`absolute right-0 mt-2 w-48 rounded-lg shadow-lg ring-1 ring-white/10 transition-all duration-300 ease-in-out transform ${
                  userMenuOpen 
                    ? 'opacity-100 translate-y-0 scale-100' 
                    : 'opacity-0 -translate-y-2 scale-95 pointer-events-none'
                }`}
                     style={{
                       background: 'linear-gradient(135deg, #1a0611 0%, #2d0a1a 25%, #4a1528 50%, #2d0a1a 75%, #1a0611 100%)',
                     }}>
                  <div className="absolute inset-0 rounded-lg"
                       style={{
                         background: 'rgba(128, 0, 32, 0.2)',
                         backdropFilter: 'blur(20px)',
                         WebkitBackdropFilter: 'blur(20px)',
                         border: '1px solid rgba(128, 0, 32, 0.3)'
                       }}>
                  </div>
                  <div className="relative z-10 py-1">
                    <div className="px-4 py-2 text-sm border-b border-white/10">
                      <p className="font-medium text-white">{user?.first_name} {user?.last_name}</p>
                      <p className="text-maroon-100">{user?.email}</p>
                      <p className="text-xs text-maroon-200 capitalize">{user?.role}</p>
                    </div>
                    <button
                      onClick={() => {
                        setUserMenuOpen(false);
                        logout();
                      }}
                      className="flex items-center w-full px-4 py-2 text-sm text-maroon-100 hover:bg-white/10 hover:text-white transition-colors duration-200"
                    >
                      <ArrowRightOnRectangleIcon className="mr-3 h-4 w-4" />
                      Sign out
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Page content */}
        <main className="py-6">
          <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  );
}

export default Layout; 