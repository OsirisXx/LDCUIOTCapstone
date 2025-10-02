import React, { useState, useEffect, useRef } from 'react';
import axios from 'axios';
import { toast } from 'react-hot-toast';
import { useAuth } from '../contexts/AuthContext';
import { 
  CreditCardIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClockIcon,
  UserIcon
} from '@heroicons/react/24/outline';

function RFIDScanner() {
  const { user } = useAuth();
  const [isScanning, setIsScanning] = useState(false);
  const [lastScan, setLastScan] = useState(null);
  const [scanHistory, setScanHistory] = useState([]);
  const inputRef = useRef(null);
  const scanTimeoutRef = useRef(null);
  const rfidBufferRef = useRef('');

  useEffect(() => {
    // Focus the hidden input when component mounts
    if (inputRef.current) {
      inputRef.current.focus();
    }

    // Refocus input when window regains focus
    const handleFocus = () => {
      if (inputRef.current) {
        inputRef.current.focus();
      }
    };

    window.addEventListener('focus', handleFocus);
    document.addEventListener('click', handleFocus);

    return () => {
      window.removeEventListener('focus', handleFocus);
      document.removeEventListener('click', handleFocus);
      if (scanTimeoutRef.current) {
        clearTimeout(scanTimeoutRef.current);
      }
    };
  }, []);

  const handleKeyPress = (e) => {
    // RFID readers typically send data very quickly and end with Enter
    if (e.key === 'Enter') {
      if (rfidBufferRef.current.length > 4) {
        processRFIDScan(rfidBufferRef.current.trim());
      }
      rfidBufferRef.current = '';
      e.target.value = '';
      return;
    }

    // Add character to buffer
    rfidBufferRef.current += e.key;

    // Clear previous timeout and set new one
    if (scanTimeoutRef.current) {
      clearTimeout(scanTimeoutRef.current);
    }

    // If no more input for 200ms, process the scan
    scanTimeoutRef.current = setTimeout(() => {
      if (rfidBufferRef.current.length > 4) {
        processRFIDScan(rfidBufferRef.current.trim());
      }
      rfidBufferRef.current = '';
      if (inputRef.current) {
        inputRef.current.value = '';
      }
    }, 200);
  };

  const processRFIDScan = async (rfidData) => {
    try {
      // Check if user is authenticated
      if (!user) {
        toast.error('❌ Please login to use RFID scanner');
        return;
      }

      setIsScanning(true);
      console.log('RFID Card Detected:', rfidData);

      // Get authentication token
      const token = localStorage.getItem('token');
      if (!token) {
        throw new Error('No authentication token found');
      }

      const response = await axios.post('http://localhost:5000/api/logs/rfid-scan', {
        rfid_data: rfidData,
        location: 'inside',
        timestamp: new Date().toISOString()
      }, {
        headers: { Authorization: `Bearer ${token}` }
      });

      if (response.status === 201) {
        const attendance = response.data.attendance;
        const scanResult = {
          id: Date.now(),
          rfid: rfidData,
          user: attendance.user,
          status: attendance.status,
          time: new Date().toLocaleTimeString('en-US', { timeZone: 'Asia/Manila' }),
          success: true
        };

        setLastScan(scanResult);
        setScanHistory(prev => [scanResult, ...prev.slice(0, 9)]); // Keep last 10 scans
        
        // Show success message - lock control is now handled by backend
        if (attendance.user.type === 'instructor' || attendance.user.type === 'admin') {
          console.log('🔓 Instructor/Admin detected - Lock control handled by backend');
          toast.success(`✅ ${attendance.user.name} - ${attendance.status} - 🔓 Lock Opened!`);
          scanResult.lockTriggered = true;
        } else {
          toast.success(`✅ ${attendance.user.name} - ${attendance.status}`);
        }
      }

    } catch (error) {
      console.error('RFID scan error:', error);
      
      const scanResult = {
        id: Date.now(),
        rfid: rfidData,
        user: null,
        status: 'Error',
        time: new Date().toLocaleTimeString('en-US', { timeZone: 'Asia/Manila' }),
        success: false,
        error: error.response?.data?.message || 'Unknown error'
      };

      setLastScan(scanResult);
      setScanHistory(prev => [scanResult, ...prev.slice(0, 9)]);
      
      if (error.response?.status === 404) {
        toast.error('❌ RFID card not registered');
      } else {
        toast.error('❌ Failed to record attendance');
      }
    } finally {
      setIsScanning(false);
    }
  };

  const getStatusIcon = (success, status) => {
    if (!success) return <XCircleIcon className="h-5 w-5 text-red-500" />;
    
    switch (status?.toLowerCase()) {
      case 'present':
        return <CheckCircleIcon className="h-5 w-5 text-green-500" />;
      case 'late':
        return <ClockIcon className="h-5 w-5 text-yellow-500" />;
      default:
        return <CheckCircleIcon className="h-5 w-5 text-blue-500" />;
    }
  };

  const getStatusColor = (success, status) => {
    if (!success) return 'bg-red-100 text-red-800';
    
    switch (status?.toLowerCase()) {
      case 'present':
        return 'bg-green-100 text-green-800';
      case 'late':
        return 'bg-yellow-100 text-yellow-800';
      default:
        return 'bg-blue-100 text-blue-800';
    }
  };

  // Lock control is now handled directly by the backend RFID endpoint

  return (
    <div className="space-y-6">
      {/* Hidden input for RFID capture */}
      <input
        ref={inputRef}
        type="text"
        onKeyPress={handleKeyPress}
        style={{
          position: 'absolute',
          left: '-9999px',
          opacity: 0,
          pointerEvents: 'none'
        }}
        autoFocus
      />

      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900">RFID Scanner</h1>
        <p className="text-gray-600">Scan RFID cards for attendance tracking</p>
      </div>

      {/* Scanner Status */}
      <div className="card">
        <div className="text-center py-8">
          {!user ? (
            <div className="mx-auto w-24 h-24 rounded-full flex items-center justify-center mb-4 bg-red-100">
              <XCircleIcon className="h-12 w-12 text-red-600" />
            </div>
          ) : (
            <div className={`mx-auto w-24 h-24 rounded-full flex items-center justify-center mb-4 ${
              isScanning ? 'bg-blue-100 animate-pulse' : 'bg-gray-100'
            }`}>
              <CreditCardIcon className={`h-12 w-12 ${
                isScanning ? 'text-blue-600' : 'text-gray-400'
              }`} />
            </div>
          )}
          
          <h3 className="text-lg font-medium text-gray-900 mb-2">
            {!user ? 'Authentication Required' : (isScanning ? 'Processing...' : 'Ready to Scan')}
          </h3>
          
          <p className="text-gray-500 mb-4">
            {!user 
              ? 'Please login to use the RFID scanner'
              : (isScanning 
                ? 'Recording attendance...' 
                : 'Place RFID card near reader or scan with R10D device'
              )
            }
          </p>

          <div className="text-sm text-gray-400">
            {user && '💡 Tip: Keep this page focused for automatic RFID detection'}
          </div>
        </div>
      </div>

      {/* Last Scan Result */}
      {lastScan && (
        <div className="card">
          <h3 className="text-lg font-medium text-gray-900 mb-4">Last Scan Result</h3>
          
          <div className="flex items-center space-x-4 p-4 bg-gray-50 rounded-lg">
            {getStatusIcon(lastScan.success, lastScan.status)}
            
            <div className="flex-1">
              <div className="flex items-center space-x-2">
                <span className="font-medium text-gray-900">
                  {lastScan.user ? lastScan.user.name : 'Unknown User'}
                </span>
                <span className={`px-2 py-1 text-xs font-semibold rounded-full ${
                  getStatusColor(lastScan.success, lastScan.status)
                }`}>
                  {lastScan.success ? lastScan.status : 'Error'}
                </span>
              </div>
              
              <div className="text-sm text-gray-500">
                RFID: {lastScan.rfid} • {lastScan.time}
                {lastScan.user && (
                  <span> • ID: {lastScan.user.student_id}</span>
                )}
                {lastScan.lockTriggered && (
                  <span className="text-green-600 font-medium"> • 🔓 Lock Opened</span>
                )}
                {lastScan.lockError && (
                  <span className="text-red-600 font-medium"> • ⚠️ Lock Failed</span>
                )}
              </div>
              
              {!lastScan.success && lastScan.error && (
                <div className="text-sm text-red-600 mt-1">
                  {lastScan.error}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Scan History */}
      {scanHistory.length > 0 && (
        <div className="card">
          <h3 className="text-lg font-medium text-gray-900 mb-4">Recent Scans</h3>
          
          <div className="space-y-2">
            {scanHistory.map((scan) => (
              <div key={scan.id} className="flex items-center space-x-3 p-3 bg-gray-50 rounded-lg">
                {getStatusIcon(scan.success, scan.status)}
                
                <UserIcon className="h-5 w-5 text-gray-400" />
                
                <div className="flex-1">
                  <div className="flex items-center space-x-2">
                    <span className="text-sm font-medium text-gray-900">
                      {scan.user ? scan.user.name : 'Unknown'}
                    </span>
                    <span className={`px-2 py-1 text-xs font-semibold rounded-full ${
                      getStatusColor(scan.success, scan.status)
                    }`}>
                      {scan.success ? scan.status : 'Error'}
                    </span>
                  </div>
                  
                  <div className="text-xs text-gray-500">
                    {scan.rfid} • {scan.time}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Instructions */}
      <div className="card">
        <h3 className="text-lg font-medium text-gray-900 mb-4">Instructions</h3>
        
        <div className="space-y-3 text-sm text-gray-600">
          <div className="flex items-start space-x-2">
            <span className="font-semibold text-blue-600">1.</span>
            <span>Keep this browser tab focused and active</span>
          </div>
          
          <div className="flex items-start space-x-2">
            <span className="font-semibold text-blue-600">2.</span>
            <span>Scan RFID card with your R10D reader</span>
          </div>
          
          <div className="flex items-start space-x-2">
            <span className="font-semibold text-blue-600">3.</span>
            <span>Attendance will be recorded automatically</span>
          </div>
          
          <div className="flex items-start space-x-2">
            <span className="font-semibold text-blue-600">4.</span>
            <span>Check attendance logs page to view all records</span>
          </div>
        </div>
      </div>
    </div>
  );
}

export default RFIDScanner;
