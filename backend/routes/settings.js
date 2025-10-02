const express = require('express');
const { authenticateToken, requireAdmin } = require('../middleware/auth');

const router = express.Router();

// Get system settings
router.get('/', authenticateToken, requireAdmin, async (req, res) => {
    try {
        // Simple hardcoded settings since no SETTINGS table in the provided schema
        const settings = {
            current_academic_year: '2023-2024',
            current_semester: '1st',
            late_tolerance_minutes: 15,
            system_name: 'IoT Attendance System',
            university_name: 'Liceo de Cagayan University'
        };

        res.json(settings);

    } catch (error) {
        console.error('Get settings error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router; 