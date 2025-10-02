const { GlobalKeyboardListener } = require('node-global-key-listener');
const axios = require('axios');

class RFIDService {
    constructor() {
        this.listener = new GlobalKeyboardListener();
        this.rfidBuffer = '';
        this.rfidTimeout = null;
        this.isCapturing = false;
        this.serverUrl = 'http://localhost:5000/api/logs/rfid-scan';
        
        console.log('🔖 RFID Service initialized');
        console.log('📡 Listening for RFID card scans...');
        console.log('🛑 Press Ctrl+C to stop');
    }

    start() {
        this.listener.addListener((e, down) => {
            if (e.state === 'DOWN') {
                this.handleKeyPress(e);
            }
        });
        
        console.log('✅ RFID Service started - Ready to capture card scans');
    }

    handleKeyPress(e) {
        // RFID readers typically send data very quickly (within 100ms)
        // and end with Enter key
        
        if (e.name === 'RETURN' || e.name === 'ENTER') {
            if (this.rfidBuffer.length > 0) {
                this.processRFIDScan(this.rfidBuffer.trim());
                this.rfidBuffer = '';
                this.isCapturing = false;
            }
            return;
        }

        // Check if this looks like RFID input (rapid typing)
        if (!this.isCapturing && this.isLikelyRFIDInput(e)) {
            this.isCapturing = true;
            this.rfidBuffer = '';
        }

        if (this.isCapturing) {
            // Add character to buffer
            if (e.name && e.name.length === 1) {
                this.rfidBuffer += e.name;
            } else if (e.name === 'SPACE') {
                this.rfidBuffer += ' ';
            } else if (e.name && e.name.startsWith('NUMPAD ')) {
                this.rfidBuffer += e.name.replace('NUMPAD ', '');
            } else if (e.name && /^[0-9]$/.test(e.name)) {
                this.rfidBuffer += e.name;
            }

            // Reset timeout for RFID completion
            clearTimeout(this.rfidTimeout);
            this.rfidTimeout = setTimeout(() => {
                if (this.rfidBuffer.length > 4) { // Minimum RFID length
                    this.processRFIDScan(this.rfidBuffer.trim());
                }
                this.rfidBuffer = '';
                this.isCapturing = false;
            }, 200); // 200ms timeout
        }
    }

    isLikelyRFIDInput(e) {
        // RFID input characteristics:
        // 1. Usually starts with numbers or letters
        // 2. Rapid succession of keystrokes
        // 3. No modifier keys (Ctrl, Alt, etc.)
        
        return (
            !e.ctrlKey && 
            !e.altKey && 
            !e.metaKey &&
            (
                (e.name && e.name.length === 1) ||
                (e.name && /^[0-9A-F]$/i.test(e.name)) ||
                (e.name && e.name.startsWith('NUMPAD '))
            )
        );
    }

    async processRFIDScan(rfidData) {
        try {
            console.log(`🔖 RFID Card Detected: ${rfidData}`);
            
            // Send to backend API
            const response = await axios.post(this.serverUrl, {
                rfid_data: rfidData,
                scan_type: 'rfid',
                location: 'inside',
                timestamp: new Date().toISOString()
            }, {
                headers: {
                    'Content-Type': 'application/json'
                },
                timeout: 5000
            });

            if (response.status === 201) {
                console.log('✅ Attendance recorded successfully!');
                console.log(`👤 User: ${response.data.attendance?.user?.name || 'Unknown'}`);
                console.log(`📍 Status: ${response.data.attendance?.status || 'Unknown'}`);
            } else {
                console.log('⚠️ Unexpected response:', response.status);
            }

        } catch (error) {
            if (error.response) {
                console.log(`❌ Server Error: ${error.response.status}`);
                console.log(`📝 Message: ${error.response.data?.message || 'Unknown error'}`);
            } else if (error.request) {
                console.log('❌ Network Error: Cannot reach server');
                console.log('🔧 Make sure backend is running: npm start');
            } else {
                console.log('❌ Error:', error.message);
            }
        }
    }

    stop() {
        this.listener.kill();
        console.log('🛑 RFID Service stopped');
    }
}

// Handle graceful shutdown
process.on('SIGINT', () => {
    console.log('\n🛑 Shutting down RFID Service...');
    process.exit(0);
});

process.on('SIGTERM', () => {
    console.log('\n🛑 RFID Service terminated');
    process.exit(0);
});

module.exports = RFIDService;

// If run directly, start the service
if (require.main === module) {
    const rfidService = new RFIDService();
    rfidService.start();
}
