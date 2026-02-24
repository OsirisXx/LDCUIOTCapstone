const mysql = require('mysql2/promise');

async function registerESP32() {
    const conn = await mysql.createConnection({
        host: '172.72.100.126',
        user: 'root',
        password: '',
        database: 'iot_attendance'
    });

    try {
        // Check existing devices
        console.log('Checking existing Door_Controller devices...');
        const [devices] = await conn.execute(
            'SELECT DEVICEID, DEVICETYPE, DEVICENAME, IPADDRESS, ROOMID, STATUS FROM DEVICES WHERE DEVICETYPE = ?',
            ['Door_Controller']
        );
        
        console.log('Found devices:', JSON.stringify(devices, null, 2));

        if (devices.length === 0) {
            console.log('\nNo ESP32 registered. Registering now...');
            
            // Register ESP32
            await conn.execute(
                `INSERT INTO DEVICES (DEVICEID, DEVICETYPE, DEVICENAME, IPADDRESS, ROOMID, LOCATION, STATUS, LASTSEEN)
                 VALUES (?, ?, ?, ?, ?, ?, ?, NOW())`,
                [
                    'ESP32_WAC302_Door',
                    'Door_Controller',
                    'ESP32 Door Lock Controller',
                    '192.168.137.131',
                    '5d3225b6-2235-4434-bda3-fad3ef9fbfbc', // WAC-302 room ID
                    'WAC-302 Door Lock',
                    'Active'
                ]
            );
            
            console.log('✅ ESP32 registered successfully!');
        } else {
            console.log('\nESP32 already registered. Updating IP address...');
            
            await conn.execute(
                `UPDATE DEVICES 
                 SET IPADDRESS = ?, STATUS = ?, LASTSEEN = NOW()
                 WHERE ROOMID = ? AND DEVICETYPE = ?`,
                ['192.168.137.131', 'Active', '5d3225b6-2235-4434-bda3-fad3ef9fbfbc', 'Door_Controller']
            );
            
            console.log('✅ ESP32 IP address updated!');
        }

        // Verify registration
        const [updated] = await conn.execute(
            'SELECT * FROM DEVICES WHERE DEVICETYPE = ? AND ROOMID = ?',
            ['Door_Controller', '5d3225b6-2235-4434-bda3-fad3ef9fbfbc']
        );
        
        console.log('\nFinal device registration:');
        console.log(JSON.stringify(updated, null, 2));

    } catch (error) {
        console.error('Error:', error);
    } finally {
        await conn.end();
    }
}

registerESP32();
