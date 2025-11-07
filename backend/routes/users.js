const express = require('express');
const bcrypt = require('bcryptjs');
const { body, validationResult } = require('express-validator');
const { executeQuery, getSingleResult, getResults, transaction } = require('../config/database');
const { authenticateToken, requireAdmin, requireInstructor, requireSuperAdmin } = require('../middleware/auth');
const multer = require('multer');
const XLSX = require('xlsx');
const path = require('path');
const fs = require('fs');
const roleService = require('../services/roleService');

const router = express.Router();

// Configure multer for file uploads
const storage = multer.diskStorage({
    destination: function (req, file, cb) {
        const uploadDir = path.join(__dirname, '../uploads');
        if (!fs.existsSync(uploadDir)) {
            fs.mkdirSync(uploadDir, { recursive: true });
        }
        cb(null, uploadDir);
    },
    filename: function (req, file, cb) {
        const uniqueSuffix = Date.now() + '-' + Math.round(Math.random() * 1E9);
        cb(null, 'excel-' + uniqueSuffix + path.extname(file.originalname));
    }
});

const upload = multer({
    storage: storage,
    fileFilter: function (req, file, cb) {
        const allowedTypes = ['.xlsx', '.xls', '.csv'];
        const fileExtension = path.extname(file.originalname).toLowerCase();
        if (allowedTypes.includes(fileExtension)) {
            cb(null, true);
        } else {
            cb(new Error('Only Excel files (.xlsx, .xls) and CSV files are allowed'));
        }
    },
    limits: {
        fileSize: 10 * 1024 * 1024 // 10MB limit
    }
});

// Upload Excel/CSV endpoint
router.post('/upload-excel', authenticateToken, requireAdmin, upload.single('excelFile'), async (req, res) => {
    try {
        if (!req.file) {
            return res.status(400).json({ message: 'No file uploaded' });
        }

        const filePath = req.file.path;
        let workbook;
        let users = [];

        try {
            // Read the Excel file
            workbook = XLSX.readFile(filePath);
            
            // Look for the SBO sheet
            const sheetNames = workbook.SheetNames;
            let sboSheet = null;
            
            // Find SBO sheet (case insensitive)
            for (const sheetName of sheetNames) {
                if (sheetName.toLowerCase().includes('sbo')) {
                    sboSheet = workbook.Sheets[sheetName];
                    break;
                }
            }
            
            if (!sboSheet) {
                return res.status(400).json({ 
                    message: 'SBO sheet not found in the Excel file',
                    availableSheets: sheetNames 
                });
            }

            // Convert sheet to JSON - get all data including empty cells
            const jsonData = XLSX.utils.sheet_to_json(sboSheet, { 
                header: 1,
                defval: '',
                raw: false
            });
            
            if (jsonData.length < 6) {
                return res.status(400).json({ message: 'SBO sheet appears to be empty or has insufficient data rows' });
            }

            // Find the header row - look for row with "STUDENT NAME", "COURSE/Major", "YEAR"
            let headerRowIndex = -1;
            let headerRow = null;
            
            for (let i = 0; i < Math.min(jsonData.length, 10); i++) {
                const row = jsonData[i];
                if (row && Array.isArray(row)) {
                    const rowStr = row.join('|').toLowerCase();
                    if (rowStr.includes('student') && rowStr.includes('name') && 
                        (rowStr.includes('course') || rowStr.includes('major')) && 
                        rowStr.includes('year')) {
                        headerRowIndex = i;
                        headerRow = row;
                        break;
                    }
                }
            }
            
            if (headerRowIndex === -1) {
                return res.status(400).json({ 
                    message: 'Header row with required columns not found',
                    searchedRows: jsonData.slice(0, 10)
                });
            }

            // Find column indices
            let studentNameCol = -1;
            let courseMajorCol = -1;
            let yearCol = -1;

            // Look for column headers (case insensitive and flexible)
            for (let i = 0; i < headerRow.length; i++) {
                const header = String(headerRow[i] || '').toLowerCase().trim();
                
                if (header.includes('student') && header.includes('name')) {
                    studentNameCol = i;
                } else if (header.includes('course') || header.includes('major')) {
                    courseMajorCol = i;
                } else if (header.includes('year') && !header.includes('academic')) {
                    yearCol = i;
                }
            }

            if (studentNameCol === -1) {
                return res.status(400).json({ 
                    message: 'Student Name column not found in SBO sheet',
                    headers: headerRow 
                });
            }

            if (courseMajorCol === -1) {
                return res.status(400).json({ 
                    message: 'Course/Major column not found in SBO sheet',
                    headers: headerRow 
                });
            }

            if (yearCol === -1) {
                return res.status(400).json({ 
                    message: 'Year column not found in SBO sheet',
                    headers: headerRow 
                });
            }

            // Process data rows - start from row after header
            const processedUsers = [];
            const errors = [];
            const startDataRow = headerRowIndex + 1;

            for (let i = startDataRow; i < jsonData.length; i++) {
                const row = jsonData[i];
                
                if (!row || !Array.isArray(row) || row.length === 0) {
                    console.log(`Hit empty row at ${i}, stopping processing`);
                    break; // Stop processing when we hit empty rows
                }

                // Check if SL# column exists and has a valid number (indicates valid student record)
                const slNumber = String(row[0] || '').trim();
                if (!slNumber || isNaN(slNumber)) {
                    console.log(`No valid SL# at row ${i} (value: "${slNumber}"), stopping processing`);
                    break;
                }

                const studentName = String(row[studentNameCol] || '').trim();
                const courseMajor = String(row[courseMajorCol] || '').trim();
                const year = String(row[yearCol] || '').trim();

                if (!studentName) {
                    console.log(`Row ${i + 1}: Student name is empty, skipping`);
                    continue; // Skip this row but continue processing
                }

                // Parse student name - handle both "LASTNAME, FIRSTNAME" and "LASTNAME FIRSTNAME" formats
                let lastName, firstName;
                
                if (studentName.includes(',')) {
                    // Format: "LASTNAME, FIRSTNAME MIDDLENAME"
                    const nameParts = studentName.split(',');
                    if (nameParts.length < 2) {
                        errors.push(`Row ${i + 1}: Invalid name format for "${studentName}". Expected: "LASTNAME, FIRSTNAME"`);
                        continue;
                    }
                    lastName = nameParts[0].trim();
                    const firstNamePart = nameParts[1].trim();
                    const firstNameWords = firstNamePart.split(' ');
                    firstName = firstNameWords[0];
                } else {
                    // Format: "LASTNAME FIRSTNAME MIDDLENAME" (no comma)
                    const nameWords = studentName.split(' ');
                    if (nameWords.length < 2) {
                        errors.push(`Row ${i + 1}: Invalid name format for "${studentName}". Expected at least first and last name`);
                        continue;
                    }
                    lastName = nameWords[0].trim();
                    firstName = nameWords[1].trim();
                }

                // Don't generate fake emails - use null instead
                const email = null;

                // Generate student ID (you might want to customize this logic)
                const studentId = `STU${String(i - headerRowIndex).padStart(3, '0')}`;

                // Parse year level
                let yearLevel = '';
                if (year) {
                    const yearMatch = year.match(/(\d+)/);
                    if (yearMatch) {
                        yearLevel = yearMatch[1];
                    }
                }

                processedUsers.push({
                    firstName,
                    lastName,
                    email,
                    courseMajor,
                    yearLevel,
                    studentId,
                    rowNumber: i + 1
                });
            }

            // Insert users into database
            const { v4: uuidv4 } = require('uuid');
            const insertedUsers = [];
            const insertErrors = [];

            for (const userData of processedUsers) {
                try {
                    // Check if student ID already exists
                    const existingUser = await getSingleResult(
                        'SELECT USERID FROM USERS WHERE STUDENTID = ?',
                        [userData.studentId]
                    );

                    if (existingUser) {
                        insertErrors.push(`Row ${userData.rowNumber}: Email ${userData.email} already exists`);
                        continue;
                    }

                    // Generate default password
                    const defaultPassword = 'student123';
                    const saltRounds = 10;
                    const password_hash = await bcrypt.hash(defaultPassword, saltRounds);

                    const userId = uuidv4();

                    await executeQuery(
                        `INSERT INTO USERS (USERID, STUDENTID, FIRSTNAME, LASTNAME, PASSWORD_HASH, 
                                           USERTYPE, YEARLEVEL, DEPARTMENT, STATUS)
                         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                        [userId, userData.studentId, userData.firstName, userData.lastName, 
                         password_hash, 'student', userData.yearLevel, userData.courseMajor, 'Active']
                    );

                    insertedUsers.push({
                        name: `${userData.firstName} ${userData.lastName}`,
                        studentId: userData.studentId,
                        course: userData.courseMajor,
                        year: userData.yearLevel
                    });

                } catch (error) {
                    console.error('Error inserting user:', error);
                    insertErrors.push(`Row ${userData.rowNumber}: Database error - ${error.message}`);
                }
            }

            // Clean up uploaded file
            fs.unlinkSync(filePath);

            res.json({
                message: 'Excel file processed successfully',
                summary: {
                    totalRows: jsonData.length - startDataRow,
                    processedUsers: processedUsers.length,
                    insertedUsers: insertedUsers.length,
                    errors: errors.length + insertErrors.length
                },
                insertedUsers,
                errors: [...errors, ...insertErrors]
            });

        } catch (error) {
            // Clean up uploaded file in case of error
            if (fs.existsSync(filePath)) {
                fs.unlinkSync(filePath);
            }
            throw error;
        }

    } catch (error) {
        console.error('Upload Excel error:', error);
        res.status(500).json({ 
            message: 'Failed to process Excel file', 
            error: error.message 
        });
    }
});

// Preview Excel/CSV endpoint - shows parsed data without saving to database
router.post('/preview-excel', authenticateToken, requireAdmin, upload.single('excelFile'), async (req, res) => {
    try {
        if (!req.file) {
            return res.status(400).json({ message: 'No file uploaded' });
        }

        const filePath = req.file.path;
        let workbook;

        try {
            console.log('Processing file:', filePath);
            
            // Read the Excel file
            workbook = XLSX.readFile(filePath);
            console.log('Workbook sheets:', workbook.SheetNames);
            
            // Look for the SBO sheet
            const sheetNames = workbook.SheetNames;
            let sboSheet = null;
            let sboSheetName = '';
            
            // Find SBO sheet (case insensitive)
            for (const sheetName of sheetNames) {
                if (sheetName.toLowerCase().includes('sbo')) {
                    sboSheet = workbook.Sheets[sheetName];
                    sboSheetName = sheetName;
                    break;
                }
            }
            
            if (!sboSheet) {
                return res.status(400).json({ 
                    message: 'SBO sheet not found in the Excel file',
                    availableSheets: sheetNames 
                });
            }

            console.log('Found SBO sheet:', sboSheetName);

            // Convert sheet to JSON - get all data including empty cells
            const jsonData = XLSX.utils.sheet_to_json(sboSheet, { 
                header: 1,
                defval: '',
                raw: false
            });
            console.log('Raw data rows:', jsonData.length);
            console.log('First 10 rows:', jsonData.slice(0, 10));
            
            if (jsonData.length < 6) {
                return res.status(400).json({ message: 'SBO sheet appears to be empty or has insufficient data rows' });
            }

            // Find the header row - look for row with "STUDENT NAME", "COURSE/Major", "YEAR"
            let headerRowIndex = -1;
            let headerRow = null;
            
            for (let i = 0; i < Math.min(jsonData.length, 10); i++) {
                const row = jsonData[i];
                if (row && Array.isArray(row)) {
                    const rowStr = row.join('|').toLowerCase();
                    if (rowStr.includes('student') && rowStr.includes('name') && 
                        (rowStr.includes('course') || rowStr.includes('major')) && 
                        rowStr.includes('year')) {
                        headerRowIndex = i;
                        headerRow = row;
                        break;
                    }
                }
            }
            
            if (headerRowIndex === -1) {
                console.log('Header row not found. Looking at first 10 rows:');
                jsonData.slice(0, 10).forEach((row, i) => {
                    console.log(`Row ${i}:`, row);
                });
                return res.status(400).json({ 
                    message: 'Header row with required columns not found',
                    searchedRows: jsonData.slice(0, 10)
                });
            }

            console.log('Header row found at index:', headerRowIndex);
            console.log('Headers found:', headerRow);
            
            let studentNameCol = -1;
            let courseMajorCol = -1;
            let yearCol = -1;

            // Look for column headers (case insensitive and flexible)
            for (let i = 0; i < headerRow.length; i++) {
                const header = String(headerRow[i] || '').toLowerCase().trim();
                console.log(`Column ${i}: "${header}"`);
                
                if (header.includes('student') && header.includes('name')) {
                    studentNameCol = i;
                    console.log('Found student name column at:', i);
                } else if (header.includes('course') || header.includes('major')) {
                    courseMajorCol = i;
                    console.log('Found course/major column at:', i);
                } else if (header.includes('year') && !header.includes('academic')) {
                    yearCol = i;
                    console.log('Found year column at:', i);
                }
            }

            console.log('Column indices - Name:', studentNameCol, 'Course:', courseMajorCol, 'Year:', yearCol);

            if (studentNameCol === -1) {
                return res.status(400).json({ 
                    message: 'Student Name column not found in SBO sheet',
                    headers: headerRow,
                    headerRowIndex: headerRowIndex
                });
            }

            if (courseMajorCol === -1) {
                return res.status(400).json({ 
                    message: 'Course/Major column not found in SBO sheet',
                    headers: headerRow,
                    headerRowIndex: headerRowIndex
                });
            }

            if (yearCol === -1) {
                return res.status(400).json({ 
                    message: 'Year column not found in SBO sheet',
                    headers: headerRow,
                    headerRowIndex: headerRowIndex
                });
            }

            // Process data rows for preview - start from row after header
            const previewUsers = [];
            const errors = [];
            const startDataRow = headerRowIndex + 1;

            console.log('Starting to process data from row:', startDataRow);

            for (let i = startDataRow; i < jsonData.length; i++) { // Process all data rows
                const row = jsonData[i];
                
                if (!row || !Array.isArray(row) || row.length === 0) {
                    console.log(`Hit empty row at ${i}, stopping processing`);
                    break; // Stop processing when we hit empty rows
                }

                // Check if SL# column exists and has a valid number (indicates valid student record)
                const slNumber = String(row[0] || '').trim();
                if (!slNumber || isNaN(slNumber)) {
                    console.log(`No valid SL# at row ${i} (value: "${slNumber}"), stopping processing`);
                    break;
                }

                const studentName = String(row[studentNameCol] || '').trim();
                const courseMajor = String(row[courseMajorCol] || '').trim();
                const year = String(row[yearCol] || '').trim();

                console.log(`Row ${i}: Name="${studentName}", Course="${courseMajor}", Year="${year}"`);

                if (!studentName) {
                    console.log(`Row ${i + 1}: Student name is empty, skipping`);
                    continue; // Skip this row but continue processing
                }

                // Parse student name - handle both "LASTNAME, FIRSTNAME" and "LASTNAME FIRSTNAME" formats
                let lastName, firstName;
                
                if (studentName.includes(',')) {
                    // Format: "LASTNAME, FIRSTNAME MIDDLENAME"
                    const nameParts = studentName.split(',');
                    if (nameParts.length < 2) {
                        errors.push(`Row ${i + 1}: Invalid name format for "${studentName}". Expected: "LASTNAME, FIRSTNAME"`);
                        continue;
                    }
                    lastName = nameParts[0].trim();
                    const firstNamePart = nameParts[1].trim();
                    const firstNameWords = firstNamePart.split(' ');
                    firstName = firstNameWords[0];
                } else {
                    // Format: "LASTNAME FIRSTNAME MIDDLENAME" (no comma)
                    const nameWords = studentName.split(' ');
                    if (nameWords.length < 2) {
                        errors.push(`Row ${i + 1}: Invalid name format for "${studentName}". Expected at least first and last name`);
                        continue;
                    }
                    lastName = nameWords[0].trim();
                    firstName = nameWords[1].trim();
                }

                // Don't generate fake emails - use null instead
                const email = null;

                // Generate student ID
                const studentId = `STU${String(i - headerRowIndex).padStart(3, '0')}`;

                // Parse year level
                let yearLevel = '';
                if (year) {
                    const yearMatch = year.match(/(\d+)/);
                    if (yearMatch) {
                        yearLevel = yearMatch[1];
                    }
                }

                previewUsers.push({
                    rowNumber: i + 1,
                    slNumber: slNumber,
                    originalName: studentName,
                    firstName,
                    lastName,
                    email,
                    courseMajor,
                    yearLevel,
                    studentId
                });
            }

            console.log('Processed users:', previewUsers.length);
            console.log('Errors:', errors.length);

            // Clean up uploaded file
            fs.unlinkSync(filePath);

            res.json({
                message: 'Excel file preview generated successfully',
                sheetName: sboSheetName,
                availableSheets: sheetNames,
                headerRowIndex: headerRowIndex,
                headers: {
                    studentNameCol: headerRow[studentNameCol],
                    courseMajorCol: headerRow[courseMajorCol],
                    yearCol: headerRow[yearCol]
                },
                summary: {
                    totalRows: jsonData.length - startDataRow,
                    previewRows: previewUsers.length,
                    errors: errors.length
                },
                previewUsers,
                errors
            });

        } catch (error) {
            // Clean up uploaded file in case of error
            if (fs.existsSync(filePath)) {
                fs.unlinkSync(filePath);
            }
            throw error;
        }

    } catch (error) {
        console.error('Preview Excel error:', error);
        res.status(500).json({ 
            message: 'Failed to preview Excel file', 
            error: error.message 
        });
    }
});

// Get all users with filtering
router.get('/', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { type, status, search, page = 1, limit = 50 } = req.query;
        
        let query = `
            SELECT u.USERID, u.STUDENTID, u.FACULTYID, u.FIRSTNAME, u.LASTNAME, u.USERTYPE,
                   u.YEARLEVEL, u.DEPARTMENT, u.STATUS, u.RFIDTAG, u.CREATED_AT,
                   am.IDENTIFIER as FINGERPRINT_IDENTIFIER
            FROM USERS u
            LEFT JOIN AUTHENTICATIONMETHODS am ON u.USERID = am.USERID 
                AND am.METHODTYPE = 'Fingerprint'
            WHERE 1=1 AND u.ARCHIVED_AT IS NULL
        `;
        const params = [];

        if (type) {
            query += ' AND u.USERTYPE = ?';
            params.push(type);
        }

        if (status) {
            query += ' AND u.STATUS = ?';
            params.push(status);
        }

        if (search) {
            query += ' AND (u.FIRSTNAME LIKE ? OR u.LASTNAME LIKE ? OR u.STUDENTID LIKE ? OR u.FACULTYID LIKE ?)';
            const searchTerm = `%${search}%`;
            params.push(searchTerm, searchTerm, searchTerm, searchTerm);
        }

        query += ' ORDER BY u.CREATED_AT DESC, u.LASTNAME, u.FIRSTNAME';
        
        const offset = (page - 1) * limit;
        query += ` LIMIT ${parseInt(limit)} OFFSET ${offset}`;

        const rawUsers = await executeQuery(query, params);
        
        // Debug: Log raw data to see what we're getting
        console.log('Raw users data (first user):', rawUsers[0]);
        
        // Process users to extract fingerprint ID from identifier
        const users = rawUsers.map(user => {
            let fingerprintId = null;
            if (user.FINGERPRINT_IDENTIFIER) {
                const match = user.FINGERPRINT_IDENTIFIER.match(/FP_(\d+)/);
                if (match) {
                    fingerprintId = parseInt(match[1]);
                }
            }
            
            const processedUser = {
                ...user,
                FINGERPRINT_ID: fingerprintId,
                // Remove the raw identifier from response
                FINGERPRINT_IDENTIFIER: undefined
            };
            
            // Debug: Log processing for users with fingerprints
            if (user.FINGERPRINT_IDENTIFIER) {
                console.log(`User ${user.FIRSTNAME} ${user.LASTNAME}: Identifier=${user.FINGERPRINT_IDENTIFIER}, Processed ID=${fingerprintId}`);
            }
            
            return processedUser;
        });

        // Get total count for pagination
        let countQuery = 'SELECT COUNT(DISTINCT u.USERID) as total FROM USERS u LEFT JOIN AUTHENTICATIONMETHODS am ON u.USERID = am.USERID AND am.METHODTYPE = \'Fingerprint\' WHERE 1=1 AND u.ARCHIVED_AT IS NULL';
        const countParams = [];

        if (type) {
            countQuery += ' AND u.USERTYPE = ?';
            countParams.push(type);
        }
        if (status) {
            countQuery += ' AND u.STATUS = ?';
            countParams.push(status);
        }
        if (search) {
            countQuery += ' AND (u.FIRSTNAME LIKE ? OR u.LASTNAME LIKE ? OR u.STUDENTID LIKE ? OR u.FACULTYID LIKE ?)';
            const searchTerm = `%${search}%`;
            countParams.push(searchTerm, searchTerm, searchTerm, searchTerm);
        }

        const [{ total }] = await executeQuery(countQuery, countParams);

        res.json({
            users,
            pagination: {
                page: parseInt(page),
                limit: parseInt(limit),
                total,
                pages: Math.ceil(total / limit)
            }
        });

    } catch (error) {
        console.error('Get users error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get single user
router.get('/:id', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { id } = req.params;

        const user = await getSingleResult(
            `SELECT USERID, STUDENTID, EMPLOYEEID, FIRSTNAME, LASTNAME, USERTYPE, 
                    YEARLEVEL, DEPARTMENT, STATUS, CREATED_AT, UPDATED_AT
             FROM USERS WHERE USERID = ?`,
            [id]
        );

        if (!user) {
            return res.status(404).json({ message: 'User not found' });
        }

        // Get authentication methods
        const authMethods = await executeQuery(
            'SELECT AUTHID, METHODTYPE, ISACTIVE, CREATED_AT FROM AUTHENTICATIONMETHODS WHERE USERID = ?',
            [id]
        );

        res.json({
            ...user,
            auth_methods: authMethods
        });

    } catch (error) {
        console.error('Get user error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Create new user
router.post('/', [
    authenticateToken,
    requireAdmin,
    body('first_name').trim().isLength({ min: 1 }),
    body('last_name').trim().isLength({ min: 1 }),
    body('email').optional().isEmail().normalizeEmail(),
    body('password').optional().isLength({ min: 6 }),
    body('user_type').isIn(['student', 'instructor', 'admin', 'custodian', 'dean']),
    body('department').optional().trim()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const {
            student_id, faculty_id, first_name, last_name, email, password,
            user_type, year_level, department
        } = req.body;

        // Convert empty strings to null for optional fields
        const cleanStudentId = student_id && student_id.trim() !== '' ? student_id.trim() : null;
        const cleanFacultyId = faculty_id && faculty_id.trim() !== '' ? faculty_id.trim() : null;

        // Check if student ID or faculty ID already exists
        let existingUser = null;
        if (cleanStudentId) {
            existingUser = await getSingleResult(
                'SELECT USERID FROM USERS WHERE STUDENTID = ?',
                [cleanStudentId]
            );
        }
        if (!existingUser && cleanFacultyId) {
            existingUser = await getSingleResult(
                'SELECT USERID FROM USERS WHERE FACULTYID = ?',
                [cleanFacultyId]
            );
        }

        if (existingUser) {
            return res.status(409).json({ message: 'Student ID or Faculty ID already exists' });
        }

        // Hash password only if provided
        let password_hash = null;
        if (password && password.trim() !== '') {
            const saltRounds = 10;
            password_hash = await bcrypt.hash(password, saltRounds);
        }

        const { v4: uuidv4 } = require('uuid');
        const userId = uuidv4();

        await executeQuery(
            `INSERT INTO USERS (USERID, STUDENTID, FACULTYID, FIRSTNAME, LASTNAME, PASSWORD_HASH, 
                               USERTYPE, YEARLEVEL, DEPARTMENT)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
            [userId, cleanStudentId, cleanFacultyId, first_name, last_name, password_hash, 
             user_type, year_level, department]
        );

        const newUser = await getSingleResult(
            `SELECT USERID, STUDENTID, FACULTYID, FIRSTNAME, LASTNAME, USERTYPE, 
                    YEARLEVEL, DEPARTMENT, STATUS, CREATED_AT
             FROM USERS WHERE USERID = ?`,
            [userId]
        );

        res.status(201).json({
            message: 'User created successfully',
            user: newUser
        });

    } catch (error) {
        console.error('Create user error:', error);
        if (error.code === 'ER_DUP_ENTRY') {
            // Check which field caused the duplicate entry
            if (error.message.includes('STUDENTID')) {
                return res.status(409).json({ message: 'Student ID already exists' });
            } else if (error.message.includes('EMPLOYEEID')) {
                return res.status(409).json({ message: 'Employee ID already exists' });
            } else if (error.message.includes('RFIDTAG')) {
                return res.status(409).json({ message: 'RFID tag already assigned to another user' });
            } else {
                return res.status(409).json({ message: 'A user with this information already exists' });
            }
        }
        res.status(500).json({ message: 'Internal server error', error: error.message });
    }
});

// Dedicated RFID assignment endpoint (must be before general /:id route)
router.put('/:id/assign-rfid', [
    authenticateToken,
    requireAdmin,
    body('rfid_tag').custom((value) => {
        // Allow empty string or null for deletion
        if (!value || value === '' || value === null) {
            return true;
        }
        // If not empty, must be 4-50 characters
        if (typeof value !== 'string' || value.length < 4 || value.length > 50) {
            throw new Error('RFID tag must be between 4 and 50 characters');
        }
        return true;
    })
], async (req, res) => {
    try {
        console.log('RFID assignment request:', { id: req.params.id, rfid_tag: req.body.rfid_tag });

        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            console.log('RFID validation errors:', errors.array());
            return res.status(400).json({ errors: errors.array() });
        }

        const { id } = req.params;
        const { rfid_tag } = req.body;

        // Check if user exists
        const existingUser = await getSingleResult(
            'SELECT USERID, FIRSTNAME, LASTNAME, STUDENTID, RFIDTAG FROM USERS WHERE USERID = ?',
            [id]
        );
        if (!existingUser) {
            return res.status(404).json({ message: 'User not found' });
        }

        console.log('User found:', existingUser);

        // Handle deletion if rfid_tag is empty
        if (!rfid_tag || rfid_tag.trim() === '') {
            console.log('Deleting RFID tag...');
            await executeQuery(
                'UPDATE USERS SET RFIDTAG = NULL, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = ?',
                [id]
            );
            
            const updatedUser = await getSingleResult(
                'SELECT USERID, FIRSTNAME, LASTNAME, STUDENTID, RFIDTAG FROM USERS WHERE USERID = ?',
                [id]
            );
            
            console.log('RFID deleted successfully:', updatedUser);
            
            return res.json({
                message: 'RFID deleted successfully',
                user: {
                    id: updatedUser.USERID,
                    name: `${updatedUser.FIRSTNAME} ${updatedUser.LASTNAME}`,
                    student_id: updatedUser.STUDENTID,
                    rfid_tag: updatedUser.RFIDTAG
                }
            });
        }

        // Check if RFID tag is already assigned to another user
        const duplicateRfid = await getSingleResult(
            'SELECT USERID, FIRSTNAME, LASTNAME FROM USERS WHERE RFIDTAG = ? AND USERID != ?',
            [rfid_tag, id]
        );
        if (duplicateRfid) {
            console.log('RFID already assigned to:', duplicateRfid);
            return res.status(409).json({
                message: `RFID tag already assigned to ${duplicateRfid.FIRSTNAME} ${duplicateRfid.LASTNAME}`
            });
        }

        // Update user with RFID tag
        console.log('Updating RFID tag...');
        await executeQuery(
            'UPDATE USERS SET RFIDTAG = ?, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = ?',
            [rfid_tag, id]
        );

        // Get updated user
        const updatedUser = await getSingleResult(
            'SELECT USERID, FIRSTNAME, LASTNAME, STUDENTID, RFIDTAG FROM USERS WHERE USERID = ?',
            [id]
        );

        console.log('RFID assigned successfully:', updatedUser);

        res.json({
            message: 'RFID assigned successfully',
            user: {
                id: updatedUser.USERID,
                name: `${updatedUser.FIRSTNAME} ${updatedUser.LASTNAME}`,
                student_id: updatedUser.STUDENTID,
                rfid_tag: updatedUser.RFIDTAG
            }
        });

    } catch (error) {
        console.error('RFID assignment error:', error);
        res.status(500).json({
            message: 'Failed to assign RFID',
            error: error.message,
            details: error.sql || 'No SQL details'
        });
    }
});

// Update user
router.put('/:id', [
    authenticateToken,
    requireAdmin,
    body('first_name').optional().trim().isLength({ min: 1 }),
    body('last_name').optional().trim().isLength({ min: 1 }),
    body('email').optional().custom((value) => {
        if (value === '' || value === null || value === undefined) {
            return true; // Allow empty/null emails
        }
        return require('validator').isEmail(value);
    }).normalizeEmail(),
    body('user_type').optional().isIn(['student', 'instructor', 'admin', 'custodian', 'dean', 'superadmin']),
    body('status').optional().custom((value) => {
        if (!value) return true; // Allow empty/null status
        const validStatuses = ['active', 'inactive', 'Active', 'Inactive'];
        return validStatuses.includes(value);
    }),
    body('rfid_tag').optional().isString().isLength({ min: 4, max: 50 })
], async (req, res) => {
    try {
        console.log('Update user request:', { id: req.params.id, body: req.body });

        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            console.log('Validation errors:', errors.array());
            return res.status(400).json({ 
                message: 'Validation failed',
                errors: errors.array(),
                receivedData: req.body
            });
        }

        const { id } = req.params;
        const updateFields = { ...req.body };
        const requestedRole = updateFields.user_type;
        const hasRoleChange = typeof requestedRole !== 'undefined';

        if (hasRoleChange && req.user?.role !== 'superadmin') {
            return res.status(403).json({ message: 'Only super administrators can modify roles.' });
        }

        // Remove undefined fields and convert empty strings to null for optional fields
        Object.keys(updateFields).forEach(key => {
            if (updateFields[key] === undefined) {
                delete updateFields[key];
            } else if ((key === 'student_id' || key === 'faculty_id') && updateFields[key] === '') {
                updateFields[key] = null;
            } else if (key === 'status') {
                // Normalize status values to match database
                if (updateFields[key] === 'active') {
                    updateFields[key] = 'Active';
                } else if (updateFields[key] === 'inactive') {
                    updateFields[key] = 'Inactive';
                }
            }
        });

        if (hasRoleChange) {
            delete updateFields.user_type;
        }

        const remainingFieldKeys = Object.keys(updateFields);
        if (!hasRoleChange && remainingFieldKeys.length === 0) {
            return res.status(400).json({ message: 'No fields to update' });
        }

        // Check if user exists
        const existingUser = await getSingleResult('SELECT USERID, EMAIL FROM USERS WHERE USERID = ?', [id]);
        if (!existingUser) {
            return res.status(404).json({ message: 'User not found' });
        }

        // Check if student ID is being changed and already exists
        if (updateFields.student_id) {
            const duplicateUser = await getSingleResult(
                'SELECT USERID FROM USERS WHERE STUDENTID = ? AND USERID != ?',
                [updateFields.student_id, id]
            );
            if (duplicateUser) {
                return res.status(409).json({ message: 'Student ID already exists' });
            }
        }

        // Check if RFID tag is being changed and already exists
        if (updateFields.rfid_tag) {
            const duplicateRfid = await getSingleResult(
                'SELECT USERID FROM USERS WHERE RFIDTAG = ? AND USERID != ?',
                [updateFields.rfid_tag, id]
            );
            if (duplicateRfid) {
                return res.status(409).json({ message: 'RFID tag already assigned to another user' });
            }
        }

        // Build update query - map frontend field names to database column names
        const fieldMap = {
            first_name: 'FIRSTNAME',
            last_name: 'LASTNAME',
            email: 'EMAIL',
            year_level: 'YEARLEVEL',
            department: 'DEPARTMENT',
            status: 'STATUS',
            student_id: 'STUDENTID',
            faculty_id: 'FACULTYID',
            rfid_tag: 'RFIDTAG'
        };

        // Filter out fields that don't have a mapping
        const validFields = remainingFieldKeys.filter(key => fieldMap[key]);

        if (!hasRoleChange && validFields.length === 0) {
            return res.status(400).json({ message: 'No valid fields to update' });
        }

        console.log('Valid fields to update:', validFields);
        console.log('Field mappings:', validFields.map(key => `${key} -> ${fieldMap[key]}`));

        if (validFields.length > 0) {
            const setClause = validFields.map(key => `${fieldMap[key]} = ?`).join(', ');
            const values = validFields.map(key => updateFields[key]);
            values.push(id);

            console.log('SQL Update:', `UPDATE USERS SET ${setClause}, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = ?`);
            console.log('Values:', values);

            await executeQuery(
                `UPDATE USERS SET ${setClause}, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = ?`,
                values
            );
        }

        let updatedUser;

        if (hasRoleChange) {
            const latestUser = await getSingleResult('SELECT EMAIL FROM USERS WHERE USERID = ?', [id]);

            try {
                updatedUser = await roleService.updateUserRole({
                    targetUserId: id,
                    email: latestUser ? (latestUser.email || latestUser.EMAIL) : existingUser.EMAIL,
                    newRole: requestedRole,
                    actorId: req.user?.id,
                    ipAddress: req.ip
                });
            } catch (roleError) {
                console.error('Role synchronization error:', roleError);
                if (roleError.statusCode) {
                    return res.status(roleError.statusCode).json({ message: roleError.message });
                }
                return res.status(500).json({ message: 'Failed to synchronize role with Supabase', error: roleError.message });
            }
        }

        if (!updatedUser) {
            updatedUser = await getSingleResult(
                `SELECT USERID, STUDENTID, FACULTYID, FIRSTNAME, LASTNAME, USERTYPE,
                        YEARLEVEL, DEPARTMENT, STATUS, RFIDTAG, CREATED_AT, UPDATED_AT
                 FROM USERS WHERE USERID = ?`,
                [id]
            );
        }

        res.json({
            message: 'User updated successfully',
            user: updatedUser
        });

    } catch (error) {
        console.error('Update user error:', error);
        if (error.code === 'ER_DUP_ENTRY') {
            // Check which field caused the duplicate entry
            if (error.message.includes('STUDENTID')) {
                return res.status(409).json({ message: 'Student ID already exists' });
            } else if (error.message.includes('EMPLOYEEID')) {
                return res.status(409).json({ message: 'Employee ID already exists' });
            } else if (error.message.includes('RFIDTAG')) {
                return res.status(409).json({ message: 'RFID tag already assigned to another user' });
            } else {
                return res.status(409).json({ message: 'A user with this information already exists' });
            }
        }
        res.status(500).json({
            message: 'Internal server error',
            error: error.message,
            details: error.sql || 'No SQL details available'
        });
    }
});

router.patch('/:id/role', [
    authenticateToken,
    requireSuperAdmin,
    body('role').isIn(['student', 'instructor', 'admin', 'custodian', 'dean', 'superadmin'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ message: 'Validation failed', errors: errors.array() });
        }

        const { id } = req.params;
        const { role } = req.body;

        const userRecord = await getSingleResult('SELECT USERID, EMAIL FROM USERS WHERE USERID = ?', [id]);
        if (!userRecord) {
            return res.status(404).json({ message: 'User not found' });
        }

        const updatedUser = await roleService.updateUserRole({
            targetUserId: id,
            email: userRecord.EMAIL,
            newRole: role,
            actorId: req.user?.id,
            ipAddress: req.ip
        });

        res.json({
            message: 'Role updated successfully',
            user: updatedUser
        });
    } catch (error) {
        console.error('Role update error:', error);
        console.error('Error details:', {
            message: error.message,
            code: error.code,
            statusCode: error.statusCode,
            stack: error.stack
        });
        if (error.statusCode) {
            return res.status(error.statusCode).json({ 
                message: error.message,
                ...(process.env.NODE_ENV === 'development' && { error: error.message })
            });
        }
        res.status(500).json({ 
            message: 'Internal server error',
            ...(process.env.NODE_ENV === 'development' && { 
                error: error.message,
                details: error.stack 
            })
        });
    }
});

// Get all users by type
router.get('/by-type/:type', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { type } = req.params;
        
        if (!['student', 'instructor', 'admin', 'custodian', 'dean'].includes(type)) {
            return res.status(400).json({ message: 'Invalid user type' });
        }

        // First, let's check what user types actually exist in the database
        const allUserTypes = await getResults(`
            SELECT DISTINCT USERTYPE, COUNT(*) as count
            FROM USERS 
            GROUP BY USERTYPE
        `);
        
        console.log('Available user types in database:', allUserTypes);

        const users = await getResults(`
            SELECT USERID, FIRSTNAME, LASTNAME, USERTYPE 
            FROM USERS 
            WHERE USERTYPE = ? 
            ORDER BY FIRSTNAME, LASTNAME
        `, [type]);

        console.log(`Found ${users.length} users of type '${type}'`);

        res.json({ users, count: users.length });

    } catch (error) {
        console.error('Get users by type error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Bulk delete users (must be before /:id route)
router.delete('/bulk-delete', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { ids } = req.body;

        if (!ids || !Array.isArray(ids) || ids.length === 0) {
            return res.status(400).json({ message: 'No user IDs provided' });
        }

        // Don't allow deleting the current user
        const currentUserId = req.user.id;
        if (ids.includes(currentUserId)) {
            return res.status(400).json({ message: 'Cannot delete your own account' });
        }

        // Validate that all users exist
        const placeholders = ids.map(() => '?').join(',');
        const existingUsers = await executeQuery(
            `SELECT USERID FROM USERS WHERE USERID IN (${placeholders})`,
            ids
        );

        if (existingUsers.length !== ids.length) {
            return res.status(404).json({ message: 'Some users not found' });
        }

        // Delete from AUTHENTICATIONMETHODS first (cascade delete)
        await executeQuery(
            `DELETE FROM AUTHENTICATIONMETHODS WHERE USERID IN (${placeholders})`,
            ids
        );
        
        // Then delete from USERS
        await executeQuery(
            `DELETE FROM USERS WHERE USERID IN (${placeholders})`,
            ids
        );

        res.json({ 
            message: `${ids.length} users deleted successfully`,
            deletedCount: ids.length
        });

    } catch (error) {
        console.error('Bulk delete users error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Delete user
router.delete('/:id', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { id } = req.params;

        // Check if user exists
        const existingUser = await getSingleResult('SELECT USERID FROM USERS WHERE USERID = ?', [id]);
        if (!existingUser) {
            return res.status(404).json({ message: 'User not found' });
        }

        // Don't allow deleting the current user
        if (req.user.id === id) {
            return res.status(400).json({ message: 'Cannot delete your own account' });
        }

        // Delete from AUTHENTICATIONMETHODS first (cascade delete)
        await executeQuery('DELETE FROM AUTHENTICATIONMETHODS WHERE USERID = ?', [id]);
        
        // Then delete from USERS
        await executeQuery('DELETE FROM USERS WHERE USERID = ?', [id]);

        res.json({ message: 'User deleted successfully' });

    } catch (error) {
        console.error('Delete user error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Promote students to next year level
router.post('/promote', [
    authenticateToken,
    requireAdmin,
    body('student_ids').isArray(),
    body('new_year_level').isInt({ min: 1, max: 6 }),
    body('new_academic_year').notEmpty(),
    body('new_semester').isIn(['1st', '2nd', 'summer'])
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { student_ids, new_year_level, new_academic_year, new_semester } = req.body;

        await transaction(async (connection) => {
            for (const studentId of student_ids) {
                // Check if student exists and is active
                const student = await connection.execute(
                    'SELECT USERID, YEARLEVEL, STATUS FROM USERS WHERE USERID = ? AND USERTYPE = "student"',
                    [studentId]
                );

                if (student[0].length === 0) {
                    throw new Error(`Student with ID ${studentId} not found`);
                }

                const [studentData] = student[0];

                if (studentData.STATUS !== 'active') {
                    throw new Error(`Student with ID ${studentId} is not active`);
                }

                // Check if graduating (year level 4 or higher going to graduate)
                const isGraduating = new_year_level > 4;
                const newStatus = isGraduating ? 'graduated' : 'active';

                // Update student
                await connection.execute(
                    `UPDATE USERS SET 
                     YEARLEVEL = ?, 
                     CREATED_AT = ?, 
                     SEMESTER = ?, 
                     STATUS = ?,
                     UPDATED_AT = CURRENT_TIMESTAMP 
                     WHERE USERID = ?`,
                    [new_year_level, new_academic_year, new_semester, newStatus, studentId]
                );
            }
        });

        res.json({
            message: `Successfully promoted ${student_ids.length} students`,
            promoted_count: student_ids.length
        });

    } catch (error) {
        console.error('Promote students error:', error);
        res.status(500).json({ message: error.message || 'Internal server error' });
    }
});

// Add authentication method to user
router.post('/:id/auth-methods', [
    authenticateToken,
    requireAdmin,
    body('method_type').isIn(['rfid', 'fingerprint']),
    body('identifier').notEmpty()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { id } = req.params;
        const { method_type, identifier } = req.body;

        // Check if user exists
        const user = await getSingleResult('SELECT USERID FROM USERS WHERE USERID = ?', [id]);
        if (!user) {
            return res.status(404).json({ message: 'User not found' });
        }

        // Check if identifier already exists
        const existingAuth = await getSingleResult(
            'SELECT AUTHID FROM AUTHENTICATIONMETHODS WHERE IDENTIFIER = ? AND METHODTYPE = ?',
            [identifier, method_type]
        );

        if (existingAuth) {
            return res.status(409).json({ message: 'Authentication method already exists' });
        }

        const { v4: uuidv4 } = require('uuid');
        const authId = uuidv4();

        await executeQuery(
            'INSERT INTO AUTHENTICATIONMETHODS (AUTHID, USERID, METHODTYPE, IDENTIFIER) VALUES (?, ?, ?, ?)',
            [authId, id, method_type, identifier]
        );

        res.status(201).json({ message: 'Authentication method added successfully' });

    } catch (error) {
        console.error('Add auth method error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Assign fingerprint to user (for ESP32 integration)
router.post('/:id/assign-fingerprint', [
    authenticateToken,
    requireAdmin,
    body('fingerprint_id').isInt({ min: 1, max: 127 }),
    body('fingerprint_template').optional()
], async (req, res) => {
    try {
        const errors = validationResult(req);
        if (!errors.isEmpty()) {
            return res.status(400).json({ errors: errors.array() });
        }

        const { id } = req.params;
        const { fingerprint_id, fingerprint_template } = req.body;

        // Check if user exists
        const user = await getSingleResult(
            'SELECT USERID, FIRSTNAME, LASTNAME, STUDENTID FROM USERS WHERE USERID = ?',
            [id]
        );
        if (!user) {
            return res.status(404).json({ message: 'User not found' });
        }

        // Remove any existing fingerprint for this user
        await executeQuery(
            'DELETE FROM AUTHENTICATIONMETHODS WHERE USERID = ? AND METHODTYPE = ?',
            [id, 'Fingerprint']
        );

        // Remove the specific fingerprint ID from any other user (for reassignment)
        await executeQuery(
            'DELETE FROM AUTHENTICATIONMETHODS WHERE IDENTIFIER = ? AND METHODTYPE = ?',
            [`FP_${fingerprint_id}`, 'Fingerprint']
        );

        // Add new fingerprint
        const { v4: uuidv4 } = require('uuid');
        const authId = uuidv4();

        await executeQuery(
            `INSERT INTO AUTHENTICATIONMETHODS
             (AUTHID, USERID, METHODTYPE, IDENTIFIER, FINGERPRINTTEMPLATE)
             VALUES (?, ?, ?, ?, ?)`,
            [authId, id, 'Fingerprint', `FP_${fingerprint_id}`, fingerprint_template || null]
        );

        res.status(201).json({
            message: 'Fingerprint assigned successfully',
            user: {
                id: user.USERID,
                name: `${user.FIRSTNAME} ${user.LASTNAME}`,
                student_id: user.STUDENTID,
                fingerprint_id: fingerprint_id
            }
        });

    } catch (error) {
        console.error('Assign fingerprint error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get all fingerprint IDs with assignment status
router.get('/fingerprint-ids/available', authenticateToken, requireAdmin, async (req, res) => {
    try {
        // Get all assigned fingerprint IDs with user information
        const assignedIds = await executeQuery(
            `SELECT am.IDENTIFIER, u.FIRSTNAME, u.LASTNAME, u.STUDENTID, u.FACULTYID, u.USERTYPE
             FROM AUTHENTICATIONMETHODS am
             JOIN USERS u ON am.USERID = u.USERID
             WHERE am.METHODTYPE = 'Fingerprint' AND am.IDENTIFIER LIKE 'FP_%' AND am.ISACTIVE = 1`
        );

        const assignmentMap = {};
        assignedIds.forEach(row => {
            const match = row.IDENTIFIER.match(/FP_(\d+)/);
            if (match) {
                const fingerprintId = parseInt(match[1]);
                assignmentMap[fingerprintId] = {
                    name: `${row.FIRSTNAME} ${row.LASTNAME}`,
                    id: row.STUDENTID || row.FACULTYID,
                    userType: row.USERTYPE
                };
            }
        });

        // Generate list of all IDs (1-127) with assignment status
        const allIds = [];
        for (let i = 1; i <= 127; i++) {
            allIds.push({
                id: i,
                assigned: !!assignmentMap[i],
                assignedTo: assignmentMap[i] || null
            });
        }

        // Also provide separate lists for backwards compatibility
        const availableIds = allIds.filter(item => !item.assigned).map(item => item.id);
        const assignedCount = allIds.filter(item => item.assigned).length;

        res.json({
            all_ids: allIds,
            available_ids: availableIds, // Keep for backwards compatibility
            assigned_count: assignedCount,
            total_capacity: 127
        });

    } catch (error) {
        console.error('Get available fingerprint IDs error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get user by fingerprint ID
router.get('/fingerprint/:fingerprintId', authenticateToken, async (req, res) => {
    try {
        const { fingerprintId } = req.params;
        
        // Validate fingerprint ID
        const fingerprintNum = parseInt(fingerprintId);
        if (isNaN(fingerprintNum) || fingerprintNum < 1 || fingerprintNum > 127) {
            return res.status(400).json({ 
                message: 'Invalid fingerprint ID. Must be a number between 1 and 127.' 
            });
        }

        // Find user by fingerprint ID
        const user = await getSingleResult(
            `SELECT u.USERID, u.FIRSTNAME, u.LASTNAME, u.STUDENTID, u.EMPLOYEEID, 
                    u.USERTYPE, u.STATUS, u.YEARLEVEL, u.DEPARTMENT
             FROM USERS u
             JOIN AUTHENTICATIONMETHODS am ON u.USERID = am.USERID
             WHERE am.METHODTYPE = 'Fingerprint' 
             AND am.IDENTIFIER = ? 
             AND am.ISACTIVE = 1`,
            [`FP_${fingerprintNum}`]
        );

        if (!user) {
            return res.status(404).json({
                message: 'No user found with this fingerprint ID',
                fingerprint_id: fingerprintNum
            });
        }

        res.json({
            user: user,
            fingerprint_id: fingerprintNum
        });

    } catch (error) {
        console.error('Get user by fingerprint ID error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Get user statistics
router.get('/stats/overview', authenticateToken, requireInstructor, async (req, res) => {
    try {
        const { academic_year, semester } = req.query;

        let whereClause = '';
        const params = [];

        if (academic_year) {
            whereClause += ' AND CREATED_AT LIKE ?';
            params.push(`${academic_year}-%`);
        }

        if (semester) {
            whereClause += ' AND CREATED_AT LIKE ?';
            params.push(`%${semester}`);
        }

        const stats = await executeQuery(`
            SELECT 
                USERTYPE,
                STATUS,
                COUNT(*) as count
            FROM USERS 
            WHERE 1=1 ${whereClause}
            GROUP BY USERTYPE, STATUS
            ORDER BY USERTYPE, STATUS
        `, params);

        const summary = {
            total_users: 0,
            students: { active: 0, inactive: 0, graduated: 0 },
            instructors: { active: 0, inactive: 0 },
            admins: { active: 0, inactive: 0 }
        };

        stats.forEach(stat => {
            summary.total_users += stat.count;
            if (summary[stat.USERTYPE + 's']) {
                summary[stat.USERTYPE + 's'][stat.STATUS] = stat.count;
            }
        });

        res.json(summary);

    } catch (error) {
        console.error('Get user stats error:', error);
        res.status(500).json({ message: 'Internal server error' });
    }
});

// Simulate attendance scan for a user (Test functionality)
router.post('/:id/test-attendance', authenticateToken, async (req, res) => {
    try {
        const { id: userId } = req.params;
        const { authMethod = 'RFID', location = 'inside' } = req.body;

        // Check if user exists and is a student
        const userQuery = 'SELECT * FROM USERS WHERE USERID = ?';
        const [users] = await executeQuery(userQuery, [userId]);
        
        if (users.length === 0) {
            return res.status(404).json({ message: 'User not found' });
        }

        const user = users[0];
        
        // Get current active sessions to find a suitable class
        const today = new Date().toISOString().split('T')[0];
        const currentTime = new Date().toTimeString().split(' ')[0];
        
        const sessionQuery = `
            SELECT s.*, cs.*, c.COURSECODE, c.COURSENAME, r.ROOMNUMBER, r.ROOMNAME 
            FROM SESSIONS s
            JOIN CLASSSCHEDULES cs ON s.SCHEDULEID = cs.SCHEDULEID
            JOIN COURSES c ON cs.COURSEID = c.COURSEID
            JOIN ROOMS r ON s.ROOMID = r.ROOMID
            WHERE s.SESSIONDATE = ? 
            AND s.STATUS = 'active'
            ORDER BY s.STARTTIME ASC
            LIMIT 1
        `;
        
        let [sessions] = await executeQuery(sessionQuery, [today]);
        
        // If no active sessions, get any session from today or create a mock scenario
        if (sessions.length === 0) {
            // Get the first available schedule for simulation
            const mockSessionQuery = `
                SELECT cs.*, c.COURSECODE, c.COURSENAME, r.ROOMNUMBER, r.ROOMNAME, r.ROOMID
                FROM CLASSSCHEDULES cs
                JOIN COURSES c ON cs.COURSEID = c.COURSEID
                JOIN ROOMS r ON cs.ROOMID = r.ROOMID
                LIMIT 1
            `;
            
            const [mockSessions] = await executeQuery(mockSessionQuery);
            
            if (mockSessions.length === 0) {
                return res.status(400).json({ message: 'No class schedules available for simulation' });
            }
            
            sessions = [{ 
                ...mockSessions[0], 
                SESSIONID: null, // Will be generated
                SESSIONDATE: today,
                STATUS: 'simulated'
            }];
        }
        
        const session = sessions[0];
        
        // Generate attendance record
        const { v4: uuidv4 } = require('uuid');
        const attendanceId = uuidv4();
        const sessionId = session.SESSIONID || uuidv4();
        
        // Determine status based on current time vs class time
        let status = 'Present';
        const currentHour = new Date().getHours();
        const classStartHour = session.STARTTIME ? parseInt(session.STARTTIME.split(':')[0]) : currentHour;
        
        if (currentHour > classStartHour + 1) {
            status = 'Late';
        }
        
        // Random chance for different scenarios
        const random = Math.random();
        if (random < 0.05) status = 'Late'; // 5% chance of being late
        
        const scanDateTime = new Date().toISOString().slice(0, 19).replace('T', ' ');
        const timeIn = new Date().toTimeString().split(' ')[0];
        
        // Insert attendance record
        const insertQuery = `
            INSERT INTO ATTENDANCERECORDS (
                ATTENDANCEID, USERID, SCHEDULEID, SESSIONID, SCANTYPE,
                SCANDATETIME, DATE, TIMEIN, AUTHMETHOD, LOCATION,
                STATUS, ACADEMICYEAR, SEMESTER, CREATED_AT, UPDATED_AT
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())
        `;
        
        await executeQuery(insertQuery, [
            attendanceId,
            userId,
            session.SCHEDULEID,
            sessionId,
            'time_in',
            scanDateTime,
            today,
            timeIn,
            authMethod,
            location,
            status,
            session.ACADEMICYEAR || '2024-2025',
            session.SEMESTER || 'First Semester'
        ]);
        
        // If this was a simulated session, create the session record too
        if (!session.SESSIONID) {
            const createSessionQuery = `
                INSERT INTO SESSIONS (
                    SESSIONID, SCHEDULEID, INSTRUCTORID, ROOMID, SESSIONDATE,
                    STARTTIME, ENDTIME, STATUS, DOORUNLOCKEDAT, DOORLOCKEDAT,
                    ACADEMICYEAR, SEMESTER, CREATED_AT, UPDATED_AT
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())
            `;
            
            const startTime = `${today} ${currentHour.toString().padStart(2, '0')}:00:00`;
            const endTime = `${today} ${(currentHour + 2).toString().padStart(2, '0')}:00:00`;
            
            await executeQuery(createSessionQuery, [
                sessionId,
                session.SCHEDULEID,
                'temp-instructor-id', // placeholder
                session.ROOMID,
                today,
                startTime,
                endTime,
                'simulated',
                startTime,
                null,
                session.ACADEMICYEAR || '2024-2025',
                session.SEMESTER || 'First Semester'
            ]);
        }
        
        // Create corresponding access log
        const accessLogId = uuidv4();
        const accessLogQuery = `
            INSERT INTO ACCESSLOGS (
                LOGID, USERID, ROOMID, TIMESTAMP, ACCESSTYPE, AUTHMETHOD,
                LOCATION, RESULT, REASON
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        `;
        
        await executeQuery(accessLogQuery, [
            accessLogId,
            userId,
            session.ROOMID,
            scanDateTime,
            'entry',
            authMethod,
            location,
            'success',
            `Test attendance scan - ${status}`
        ]);
        
        res.json({
            message: 'Test attendance recorded successfully',
            attendance: {
                id: attendanceId,
                user: `${user.FIRSTNAME} ${user.LASTNAME}`,
                course: session.COURSECODE,
                room: session.ROOMNUMBER,
                status: status,
                scanTime: scanDateTime,
                authMethod: authMethod,
                location: location
            }
        });
        
    } catch (error) {
        console.error('Test attendance error:', error);
        res.status(500).json({ 
            message: 'Internal server error', 
            error: error.message 
        });
    }
});



// Test endpoint to check database schema
router.get('/test/schema', authenticateToken, async (req, res) => {
    try {
        // Check if RFIDTAG column exists
        const columns = await executeQuery('DESCRIBE USERS');
        const rfidColumn = columns.find(col => col.Field === 'RFIDTAG');

        res.json({
            message: 'Database schema check',
            rfidColumnExists: !!rfidColumn,
            allColumns: columns.map(col => col.Field),
            rfidColumn: rfidColumn
        });
    } catch (error) {
        console.error('Schema check error:', error);
        res.status(500).json({ message: 'Schema check failed', error: error.message });
    }
});

module.exports = router;