@echo off
echo ========================================
echo    IoT Attendance System Startup
echo ========================================
echo.
echo This will start the backend server with automatic discovery.
echo The Arduino will automatically find this server on the network.
echo.
echo Make sure:
echo 1. Arduino is connected to the same WiFi network
echo 2. Arduino code is uploaded and running
echo 3. No firewall is blocking ports 5000 and 8888
echo.
pause

echo.
echo Starting IoT Attendance System Backend...
echo.

cd backend
npm start

pause 