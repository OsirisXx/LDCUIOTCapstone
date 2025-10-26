const express = require('express');
const cors = require('cors');
const helmet = require('helmet');
const rateLimit = require('express-rate-limit');
require('dotenv').config();

const app = express();

// Security middleware
app.use(helmet());
app.use(cors({
    origin: process.env.FRONTEND_URL || 'http://localhost:3000',
    credentials: true
}));

// Rate limiting - More lenient for development
const limiter = rateLimit({
    windowMs: 15 * 60 * 1000, // 15 minutes
    max: 1000 // limit each IP to 1000 requests per windowMs (increased for development)
});
app.use('/api/', limiter);

// Body parsing middleware
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true }));

// Routes
app.use('/api/auth', require('./routes/auth'));
app.use('/api/scan', require('./routes/scan'));
app.use('/api/users', require('./routes/users'));
app.use('/api/logs', require('./routes/logs'));
app.use('/api/sessions', require('./routes/sessions'));
app.use('/api/rooms', require('./routes/rooms'));
app.use('/api/dashboard', require('./routes/dashboard'));
app.use('/api/subjects', require('./routes/subjects')); // Changed from courses to subjects
app.use('/api/schedules', require('./routes/schedules'));
app.use('/api/enrollment', require('./routes/enrollment'));
app.use('/api/devices', require('./routes/devices'));
app.use('/api/settings', require('./routes/settings'));
app.use('/api/unified', require('./routes/unified'));
app.use('/api/import', require('./routes/import'));
app.use('/api/lock-control', require('./routes/lockControl'));
app.use('/api/archive', require('./routes/archive'));
app.use('/api/backup', require('./routes/backup'));

// Health check endpoint
app.get('/api/health', (req, res) => {
    res.json({ 
        status: 'OK', 
        timestamp: new Date().toISOString(),
        uptime: process.uptime(),
        environment: process.env.NODE_ENV || 'development',
        system: 'IoT Attendance System'
    });
});

// Error handling middleware
app.use((err, req, res, next) => {
    console.error('Error:', err);
    
    if (err.type === 'entity.parse.failed') {
        return res.status(400).json({ message: 'Invalid JSON payload' });
    }
    
    if (err.code === 'LIMIT_FILE_SIZE') {
        return res.status(413).json({ message: 'File too large' });
    }
    
    res.status(err.status || 500).json({
        message: err.message || 'Internal server error',
        ...(process.env.NODE_ENV === 'development' && { stack: err.stack })
    });
});

// 404 handler
app.use('*', (req, res) => {
    res.status(404).json({ message: 'Endpoint not found' });
});

const PORT = process.env.PORT || 5000;

// Start UDP discovery server
const udpDiscoveryServer = require('./udpDiscoveryServer');

app.listen(PORT, '0.0.0.0', () => {
    console.log(`🚀 IoT Attendance System API Server running on port ${PORT}`);
    console.log(`📊 Environment: ${process.env.NODE_ENV || 'development'}`);
    console.log(`🔗 Health check: http://localhost:${PORT}/api/health`);
    console.log(`🌐 Network access: http://0.0.0.0:${PORT}/api/health`);
    console.log(`📱 Frontend URL: ${process.env.FRONTEND_URL || 'http://localhost:3000'}`);
    console.log(`📡 UDP Discovery Server running on port 8888`);
});

module.exports = app; 