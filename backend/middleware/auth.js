const jwt = require('jsonwebtoken');
const { getSingleResult } = require('../config/database');

const authenticateToken = async (req, res, next) => {
    const authHeader = req.headers['authorization'];
    const token = authHeader && authHeader.split(' ')[1]; // Bearer TOKEN

    if (!token) {
        return res.status(401).json({ message: 'Access token required' });
    }

    try {
        const decoded = jwt.verify(token, process.env.JWT_SECRET);
        
        // Get user from database to ensure they still exist and are active
        const user = await getSingleResult(
            'SELECT USERID as id, EMAIL as email, USERTYPE as role, STATUS as status, FIRSTNAME as first_name, LASTNAME as last_name FROM USERS WHERE USERID = ? AND STATUS = ?',
            [decoded.userId, 'Active']
        );

        if (!user) {
            return res.status(401).json({ message: 'Invalid token - user not found or inactive' });
        }

        req.user = user;
        next();
    } catch (error) {
        console.error('Token verification error:', error);
        return res.status(403).json({ message: 'Invalid or expired token' });
    }
};

const requireRole = (roles) => {
    return (req, res, next) => {
        if (!req.user) {
            return res.status(401).json({ message: 'Authentication required' });
        }

        if (!roles.includes(req.user.role)) {
            return res.status(403).json({ message: 'Insufficient permissions' });
        }

        next();
    };
};

const requireAdmin = requireRole(['admin']);
const requireInstructor = requireRole(['instructor', 'admin']);
const requireStudent = requireRole(['student', 'instructor', 'admin']);

module.exports = {
    authenticateToken,
    requireRole,
    requireAdmin,
    requireInstructor,
    requireStudent
}; 