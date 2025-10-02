const dgram = require('dgram');
const socket = dgram.createSocket('udp4');

const DISCOVERY_PORT = 8888;
const DISCOVERY_MESSAGE = 'IOT_ATTENDANCE_DISCOVERY';
const RESPONSE_MESSAGE = 'IOT_ATTENDANCE_SERVER';

socket.on('error', (err) => {
    console.log(`UDP Server error:\n${err.stack}`);
    socket.close();
});

socket.on('message', (msg, rinfo) => {
    const message = msg.toString();
    console.log(`📡 UDP Discovery request from ${rinfo.address}:${rinfo.port}`);
    console.log(`📝 Message: ${message}`);
    
    if (message === DISCOVERY_MESSAGE) {
        console.log(`✅ Responding to discovery request from ${rinfo.address}`);
        
        // Send response back to the Arduino
        const response = Buffer.from(RESPONSE_MESSAGE);
        socket.send(response, 0, response.length, rinfo.port, rinfo.address, (err) => {
            if (err) {
                console.error('❌ Error sending UDP response:', err);
            } else {
                console.log(`📤 Discovery response sent to ${rinfo.address}:${rinfo.port}`);
            }
        });
    }
});

socket.on('listening', () => {
    const address = socket.address();
    console.log(`📡 UDP Discovery Server listening on ${address.address}:${address.port}`);
    console.log('🔍 Ready to respond to IoT device discovery requests');
});

socket.bind(DISCOVERY_PORT);

// Keep the process alive
process.on('SIGINT', () => {
    console.log('\n🛑 UDP Discovery Server shutting down...');
    socket.close();
    process.exit(0);
});

module.exports = socket; 