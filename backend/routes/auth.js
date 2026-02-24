const express = require('express');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const { body, validationResult } = require('express-validator');
const rateLimit = require('express-rate-limit');
const { v4: uuidv4 } = require('uuid');
const { getSingleResult, executeQuery } = require('../config/database');
const { authenticateToken } = require('../middleware/auth');

const supabaseAuthService = require('../services/supabaseAuthService');
const { getSupabaseAdmin } = require('../services/supabaseAdmin');
const { logSecurityEvent } = require('../services/auditService');

const router = express.Router();
const passwordComplexityRegex = /^(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,}$/;

const supabaseExchangeLimiter = rateLimit({
    windowMs: 60 * 1000,
    max: 20,
    standardHeaders: true,
    legacyHeaders: false,
    message: 'Too many login attempts. Please wait and try again.'
});

const loginLimiter = rateLimit({
    windowMs: 60 * 1000, // 1 minute
    max: 10, // 10 attempts per minute
    standardHeaders: true,
    legacyHeaders: false,
    message: 'Too many login attempts. Please wait and try again.',
    skipSuccessfulRequests: true // Don't count successful logins
});

const extractNameParts = (supabaseUser) => {
    const metadata = supabaseUser?.user_metadata || {};
    let firstName = metadata.first_name || metadata.given_name || '';
    let lastName = metadata.last_name || metadata.family_name || '';

    if ((!firstName || !lastName) && metadata.full_name) {
        const parts = metadata.full_name.trim().split(/\s+/);
        if (!firstName && parts.length > 0) {
            firstName = parts[0];
        }
        if (!lastName && parts.length > 1) {
            lastName = parts.slice(1).join(' ');
        }
    }

    if ((!firstName || !lastName) && Array.isArray(supabaseUser?.identities)) {
        for (const identity of supabaseUser.identities) {
            const ident = identity?.identity_data || {};
            if (!firstName && ident.first_name) {
                firstName = ident.first_name;
            }
            if (!lastName && ident.last_name) {
                lastName = ident.last_name;
            }
            if ((!firstName || !lastName) && ident.full_name) {
                const parts = ident.full_name.trim().split(/\s+/);
                if (!firstName && parts.length > 0) {
                    firstName = parts[0];
                }
                if (!lastName && parts.length > 1) {
                    lastName = parts.slice(1).join(' ');
                }
            }
        }
    }

    if (!firstName) {
        firstName = 'New';
    }
    if (!lastName) {
        lastName = 'User';
    }

    return { firstName, lastName };
};

// Login endpoint
router.post('/login',
    loginLimiter,
    [
        body('email').isEmail().normalizeEmail(),
        body('password').isLength({ min: 6 })
    ],
    async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { email, password } = req.body;

        // Get user from database - handle both email and student_id/faculty_id login
        let user = await getSingleResult(
            `SELECT USERID as id, EMAIL as email, PASSWORD_HASH as password_hash, 
                    USERTYPE as role, FIRSTNAME as first_name, LASTNAME as last_name, 
                    STATUS as status, DEPARTMENT as department, STUDENTID as student_id, FACULTYID as faculty_id
             FROM USERS WHERE EMAIL = ?`,
            [email]
        );

        // If no user found by email, try by student_id or faculty_id
        if (!user) {
            user = await getSingleResult(
                `SELECT USERID as id, EMAIL as email, PASSWORD_HASH as password_hash, 
                        USERTYPE as role, FIRSTNAME as first_name, LASTNAME as last_name, 
                        STATUS as status, DEPARTMENT as department, STUDENTID as student_id, FACULTYID as faculty_id
                 FROM USERS WHERE STUDENTID = ? OR FACULTYID = ?`,
                [email, email]
            );
        }

        if (!user) {
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        if (user.status !== 'Active') {
            return res.status(401).json({ message: 'Account is inactive' });
        }

        // Check if password is set
        if (!user.password_hash) {
            return res.status(401).json({ message: 'No password set for this account. Please sign in with Google first.' });
        }

        // Verify password
        const isValidPassword = await bcrypt.compare(password, user.password_hash);
        if (!isValidPassword) {
            return res.status(401).json({ message: 'Invalid credentials' });
        }

        const normalizedRole = (user.role || '').toLowerCase();
        user.role = normalizedRole;

        // Check admin/superadmin role requirement
        const allowedRoles = ['admin', 'superadmin'];
        if (!allowedRoles.includes(normalizedRole)) {
            return res.status(403).json({ message: 'This account does not have administrator privileges. Contact an administrator for access.' });
        }

        // Generate JWT token
        const token = jwt.sign(
            { 
                userId: user.id, 
                email: user.email, 
                role: normalizedRole 
            },
            process.env.JWT_SECRET,
            { expiresIn: process.env.JWT_EXPIRES_IN || '7d' }
        );

        // Remove password from response
        delete user.password_hash;

        // Add has_password flag
        user.has_password = true;

        res.json({
            message: 'Login successful',
            token,
            user
        });

    } catch (error) {
        console.error('Login error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

router.post('/supabase',
    supabaseExchangeLimiter,
    body('accessToken').isString().notEmpty().withMessage('accessToken is required'),
    body('refreshToken').optional().isString(),
    async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        if (!supabaseAuthService.isSupabaseConfigured()) {
            return res.status(503).json({ message: 'Supabase integration is not configured on the API.' });
        }

        const { accessToken } = req.body;
        const supabaseUser = await supabaseAuthService.verifySupabaseAccessToken(accessToken);

        const email = (supabaseUser.email || '').toLowerCase();
        if (!email) {
            return res.status(400).json({ message: 'Supabase account is missing a verified email address.' });
        }

        if (!supabaseAuthService.isEmailAllowlisted(email)) {
            return res.status(403).json({ message: 'This email is not authorized to access the system.' });
        }

        if (!supabaseUser.email_confirmed_at) {
            return res.status(403).json({ message: 'Google account has not been verified by Supabase.' });
        }

        let user = await getSingleResult(
            `SELECT USERID as id, EMAIL as email, USERTYPE as role,
                    FIRSTNAME as first_name, LASTNAME as last_name,
                    STATUS as status, DEPARTMENT as department,
                    STUDENTID as student_id, FACULTYID as faculty_id
             FROM USERS WHERE LOWER(EMAIL) = ?`,
            [email]
        );

        let autoProvisioned = false;

        if (!user) {
            autoProvisioned = true;
            const { firstName, lastName } = extractNameParts(supabaseUser);
            const newUserId = uuidv4();

            try {
                console.log('Auto-provisioning user:', { email, firstName, lastName, userId: newUserId });
                
                await executeQuery(
                    `INSERT INTO USERS (USERID, FIRSTNAME, LASTNAME, USERTYPE, EMAIL, STATUS, CREATED_AT, UPDATED_AT)
                     VALUES (?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)`,
                    [newUserId, firstName, lastName, 'student', email, 'Active']
                );
                
                console.log('User auto-provisioned successfully:', email);
            } catch (insertError) {
                console.error('Failed to auto-provision user from Supabase:', insertError);
                console.error('Insert error details:', {
                    message: insertError.message,
                    code: insertError.code,
                    sql: insertError.sql,
                    sqlMessage: insertError.sqlMessage
                });
                return res.status(500).json({ 
                    message: 'Unable to provision account. Please contact an administrator.',
                    error: process.env.NODE_ENV === 'development' ? insertError.message : undefined
                });
            }

            user = await getSingleResult(
                `SELECT USERID as id, EMAIL as email, USERTYPE as role,
                        FIRSTNAME as first_name, LASTNAME as last_name,
                        STATUS as status, DEPARTMENT as department,
                        STUDENTID as student_id, FACULTYID as faculty_id
                 FROM USERS WHERE LOWER(EMAIL) = ?`,
                [email]
            );

            if (!user) {
                console.error('User was inserted but could not be retrieved:', email);
                return res.status(500).json({ message: 'Account provisioning failed. Please contact an administrator.' });
            }

            const supabaseAdminClient = getSupabaseAdmin();
            if (supabaseAdminClient) {
                try {
                    await supabaseAdminClient.auth.admin.updateUserById(supabaseUser.id, {
                        app_metadata: {
                            ...(supabaseUser.app_metadata || {}),
                            role: 'student'
                        }
                    });
                } catch (metadataError) {
                    console.warn('Failed to set Supabase app_metadata role for auto-provisioned user:', metadataError.message);
                }
            }
        }

        if (user.status !== 'Active') {
            return res.status(403).json({ message: 'Account is inactive. Please contact an administrator.' });
        }

        const normalizedRole = (user.role || '').toLowerCase();
        const supabaseRole = (supabaseUser.app_metadata?.role || '').toLowerCase();

        const allowedRoles = ['admin', 'superadmin'];
        if (!allowedRoles.includes(normalizedRole)) {
            await logSecurityEvent({
                eventType: 'supabase_login_denied',
                actorUserId: user.id,
                targetUserId: user.id,
                metadata: {
                    email,
                    supabase_id: supabaseUser.id,
                    supabase_role: supabaseRole,
                    mysql_role: normalizedRole,
                    reason: 'insufficient_role',
                    autoProvisioned
                },
                ipAddress: req.ip
            });

            const message = autoProvisioned
                ? 'Account has been created but requires administrator approval before access is granted.'
                : 'This account does not have administrator privileges. Contact an administrator for access.';

            return res.status(403).json({ message });
        }

        if (supabaseRole && supabaseRole !== normalizedRole) {
            console.warn(`Supabase role mismatch for ${email}: Supabase=${supabaseRole}, MySQL=${normalizedRole}`);
        }

        const token = jwt.sign(
            {
                userId: user.id,
                email: user.email,
                role: normalizedRole
            },
            process.env.JWT_SECRET,
            { expiresIn: process.env.JWT_EXPIRES_IN || '7d' }
        );

        // Check if user has password set
        const userWithPassword = await getSingleResult(
            `SELECT PASSWORD_HASH FROM USERS WHERE USERID = ?`,
            [user.id]
        );

        const responseUser = {
            id: user.id,
            email: user.email,
            role: normalizedRole,
            first_name: user.first_name,
            last_name: user.last_name,
            status: user.status,
            department: user.department,
            student_id: user.student_id,
            faculty_id: user.faculty_id,
            supabase_id: supabaseUser.id,
            supabase_role: supabaseRole,
            has_password: !!(userWithPassword?.PASSWORD_HASH)
        };

        await logSecurityEvent({
            eventType: 'supabase_login',
            actorUserId: user.id,
            targetUserId: user.id,
            metadata: {
                email,
                supabase_id: supabaseUser.id,
                supabase_role: supabaseRole,
                mysql_role: normalizedRole,
                autoProvisioned
            },
            ipAddress: req.ip
        });

        res.json({
            message: 'Authentication successful',
            token,
            user: responseUser
        });
    } catch (error) {
        console.error('Supabase authentication error:', error);
        console.error('Error stack:', error.stack);

        if (error.statusCode) {
            return res.status(error.statusCode).json({ 
                message: error.message,
                ...(process.env.NODE_ENV === 'development' && { 
                    error: error.message,
                    stack: error.stack 
                })
            });
        }

        return res.status(500).json({ 
            message: 'Internal server error',
            ...(process.env.NODE_ENV === 'development' && { 
                error: error.message,
                stack: error.stack 
            })
        });
    }
});

// Verify token endpoint
router.get('/verify', authenticateToken, async (req, res) => {
    try {
        // Check if user has password set
        const userWithPassword = await getSingleResult(
            `SELECT PASSWORD_HASH FROM USERS WHERE USERID = ?`,
            [req.user.id]
        );
        
        const userResponse = {
            ...req.user,
            has_password: !!(userWithPassword?.PASSWORD_HASH)
        };

        res.json({
            message: 'Token is valid',
            user: userResponse
        });
    } catch (error) {
        console.error('Verify token error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Logout endpoint (client-side token removal)
router.post('/logout', authenticateToken, (req, res) => {
    res.json({ message: 'Logout successful' });
});

// Set initial password endpoint (for users who don't have one yet)
router.post('/set-password', [
    authenticateToken,
    body('newPassword')
        .isLength({ min: 8 }).withMessage('Password must be at least 8 characters long')
        .matches(passwordComplexityRegex)
        .withMessage('Password must include at least one uppercase letter, one number, and one special character.'),
    body('confirmPassword').custom((value, { req }) => {
        if (value !== req.body.newPassword) {
            throw new Error('Passwords do not match');
        }
        return true;
    })
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { newPassword } = req.body;
        const userId = req.user.id;

        // Get current password hash
        const user = await getSingleResult(
            'SELECT PASSWORD_HASH as password_hash FROM USERS WHERE USERID = ?',
            [userId]
        );

        // Check if user already has a password
        if (user.password_hash) {
            return res.status(400).json({ message: 'Password already set. Use change password instead.' });
        }

        // Hash new password
        const saltRounds = 10;
        const newPasswordHash = await bcrypt.hash(newPassword, saltRounds);

        // Update password
        await executeQuery(
            'UPDATE USERS SET PASSWORD_HASH = ?, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = ?',
            [newPasswordHash, userId]
        );

        res.json({ message: 'Password set successfully' });

    } catch (error) {
        console.error('Set password error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Change password endpoint
router.post('/change-password', [
    authenticateToken,
    body('currentPassword').isLength({ min: 6 }),
    body('newPassword')
        .isLength({ min: 8 }).withMessage('Password must be at least 8 characters long')
        .matches(passwordComplexityRegex)
        .withMessage('Password must include at least one uppercase letter, one number, and one special character.'),
    body('confirmPassword').custom((value, { req }) => {
        if (value !== req.body.newPassword) {
            throw new Error('Passwords do not match');
        }
        return true;
    })
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { currentPassword, newPassword } = req.body;
        const userId = req.user.id;

        // Get current password hash
        const user = await getSingleResult(
            'SELECT PASSWORD_HASH as password_hash FROM USERS WHERE USERID = ?',
            [userId]
        );

        // Check if user has a password
        if (!user.password_hash) {
            return res.status(400).json({ message: 'No password set. Use set password instead.' });
        }

        // Verify current password
        const isValidPassword = await bcrypt.compare(currentPassword, user.password_hash);
        if (!isValidPassword) {
            return res.status(400).json({ message: 'Current password is incorrect' });
        }

        // Hash new password
        const saltRounds = 10;
        const newPasswordHash = await bcrypt.hash(newPassword, saltRounds);

        // Update password
        await executeQuery(
            'UPDATE USERS SET PASSWORD_HASH = ?, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = ?',
            [newPasswordHash, userId]
        );

        res.json({ message: 'Password changed successfully' });

    } catch (error) {
        console.error('Change password error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

module.exports = router; 