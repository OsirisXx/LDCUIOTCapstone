-- Register ESP32 Lock Controller
-- Run this SQL to register your ESP32 device

-- Step 1: Check your ESP32 IP address from the serial monitor
-- Step 2: Find your room number
SELECT ROOMID, ROOMNUMBER, ROOMNAME FROM ROOMS WHERE ROOMNUMBER = 'WAC-203';

-- Step 3: Register the ESP32 (CHANGE THE IP ADDRESS TO YOUR ESP32'S IP)
INSERT INTO DEVICES (
    DEVICEID, 
    DEVICETYPE, 
    IPADDRESS, 
    PORT, 
    LOCATION, 
    ROOMNUMBER, 
    STATUS, 
    LASTSEEN
) VALUES (
    'ESP32_LOCK_WAC203',           -- Unique device ID
    'ESP32_Lock_Controller',        -- Device type
    '192.168.1.100',               -- ⚠️ CHANGE THIS TO YOUR ESP32 IP ADDRESS
    80,                             -- Port (ESP32 web server)
    'outside',                      -- Location
    'WAC-203',                      -- ⚠️ MUST MATCH YOUR ROOM NUMBER
    'online',                       -- Status
    NOW()                           -- Last seen
);

-- Step 4: Verify registration
SELECT * FROM DEVICES WHERE DEVICETYPE = 'ESP32_Lock_Controller';

-- Step 5: Check if room number matches
SELECT 
    d.DEVICEID,
    d.IPADDRESS,
    d.ROOMNUMBER as DEVICE_ROOM,
    r.ROOMNUMBER as ACTUAL_ROOM,
    r.ROOMNAME
FROM DEVICES d
LEFT JOIN ROOMS r ON d.ROOMNUMBER = r.ROOMNUMBER
WHERE d.DEVICETYPE = 'ESP32_Lock_Controller';

