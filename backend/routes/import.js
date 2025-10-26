const express = require('express');
const multer = require('multer');
const { executeQuery, getSingleResult } = require('../config/database');
const { authenticateToken, requireAdmin } = require('../middleware/auth');
const PDFParserService = require('../services/pdfParserService');
const { v4: uuidv4 } = require('uuid');

const router = express.Router();
const pdfParser = new PDFParserService();

// Configure multer for file upload
const upload = multer({
    storage: multer.memoryStorage(),
    limits: {
        fileSize: 500 * 1024 * 1024 // 500MB limit to support very large PDFs
    },
    fileFilter: (req, file, cb) => {
        if (file.mimetype === 'application/pdf') {
            cb(null, true);
        } else {
            cb(new Error('Only PDF files are allowed'), false);
        }
    }
});

// Parse PDF and preview data without importing
router.post('/preview', [authenticateToken, requireAdmin, upload.single('pdf')], async (req, res) => {
    try {
        if (!req.file) {
            return res.status(400).json({ message: 'No PDF file uploaded' });
        }

        console.log('Parsing PDF for preview...');
        const parsedData = await pdfParser.parsePDF(req.file.buffer);

        // Return preview data without importing to database
        res.json({
            success: true,
            message: 'PDF parsed successfully',
            preview: true,
            data: parsedData
        });

    } catch (error) {
        console.error('PDF preview error:', error);
        res.status(400).json({ 
            message: 'Failed to parse PDF', 
            error: error.message 
        });
    }
});

// Import parsed data to database
router.post('/execute', [authenticateToken, requireAdmin], async (req, res) => {
    try {
        const { parsedData, options = {} } = req.body;

        if (!parsedData) {
            return res.status(400).json({ message: 'No parsed data provided' });
        }

        console.log('Starting database import...');

        // Ensure schema supports lab flag on schedules and import logs table
        await ensureColumnExists('CLASSSCHEDULES', 'ISLAB', 'TINYINT(1) DEFAULT 0');
        await ensureTableExists('IMPORT_LOGS', `
            CREATE TABLE IMPORT_LOGS (
                ID VARCHAR(36) PRIMARY KEY,
                ACADEMIC_YEAR VARCHAR(20),
                SEMESTER VARCHAR(50),
                IMPORT_DATE DATETIME,
                SUMMARY JSON,
                STATUS VARCHAR(20)
            )
        `);
        
        // Start transaction-like operations
        const importResults = {
            subjects: { created: 0, updated: 0, errors: [] },
            instructors: { created: 0, updated: 0, errors: [] },
            students: { created: 0, updated: 0, errors: [] },
            rooms: { created: 0, updated: 0, errors: [] },
            schedules: { created: 0, updated: 0, errors: [] },
            enrollments: { created: 0, updated: 0, errors: [] }
        };

        // Import instructors first (needed for subjects)
        await importInstructors(parsedData.instructors, importResults.instructors, options);

        // Import rooms (needed for schedules)
        await importRooms(parsedData.rooms, importResults.rooms, options);

        // Import subjects (needed for schedules and enrollments)
        await importSubjects(parsedData.subjects, importResults.subjects, options);

        // Import students (needed for enrollments)
        await importStudents(parsedData.students, importResults.students, options);

        // Import schedules
        await importSchedules(parsedData.schedules, importResults.schedules, options);

        // Import enrollments
        await importEnrollments(parsedData.enrollments, importResults.enrollments, options);

        // Create import log
        await createImportLog(parsedData, importResults);

        res.json({
            success: true,
            message: 'Data imported successfully',
            results: importResults,
            statistics: parsedData.statistics
        });

    } catch (error) {
        console.error('Import execution error:', error);
        res.status(500).json({ 
            message: 'Import failed', 
            error: error.message 
        });
    }
});
// Ensure a column exists on a table (idempotent)
async function ensureColumnExists(table, column, definition) {
    try {
        const exists = await getSingleResult(
            `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ? AND COLUMN_NAME = ?`,
            [table, column]
        );
        if (!exists || exists.cnt === 0) {
            console.log(`Altering table ${table}: adding column ${column} ${definition}`);
            await executeQuery(`ALTER TABLE ${table} ADD COLUMN ${column} ${definition}`);
        }
    } catch (e) {
        console.warn(`Could not ensure column ${table}.${column}:`, e.message);
    }
}

// Ensure a table exists; create if missing
async function ensureTableExists(table, createSql) {
    try {
        const exists = await getSingleResult(
            `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ?`,
            [table]
        );
        if (!exists || exists.cnt === 0) {
            console.log(`Creating table ${table} ...`);
            await executeQuery(createSql);
        }
    } catch (e) {
        console.warn(`Could not ensure table ${table}:`, e.message);
    }
}

// Import instructors with batch processing for large datasets
async function importInstructors(instructors, results, options) {
    console.log(`📚 Importing ${instructors.length} instructors...`);
    
    // Process in batches of 50 for better performance
    const batchSize = 50;
    for (let i = 0; i < instructors.length; i += batchSize) {
        const batch = instructors.slice(i, i + batchSize);
        console.log(`📚 Processing instructor batch ${Math.floor(i/batchSize) + 1}/${Math.ceil(instructors.length/batchSize)}`);
        
        for (const instructor of batch) {
            try {
                // Check if instructor exists by email, faculty ID, or name (including archived)
                let existing = null;
                if (instructor.email) {
                    existing = await getSingleResult(
                        'SELECT USERID, ARCHIVED_AT FROM USERS WHERE EMAIL = ? AND USERTYPE = "instructor"',
                        [instructor.email]
                    );
                }
                if (!existing && instructor.employee_id) {
                    existing = await getSingleResult(
                        'SELECT USERID, ARCHIVED_AT FROM USERS WHERE FACULTYID = ? AND USERTYPE = "instructor"',
                        [instructor.employee_id]
                    );
                }
                // If still not found, check by name to catch duplicates
                if (!existing && instructor.first_name && instructor.last_name) {
                    existing = await getSingleResult(
                        'SELECT USERID, ARCHIVED_AT FROM USERS WHERE FIRSTNAME = ? AND LASTNAME = ? AND USERTYPE = "instructor"',
                        [instructor.first_name, instructor.last_name]
                    );
                }

                if (existing && !options.updateExisting) {
                    // If instructor is archived, unarchive it; otherwise skip
                    if (existing.ARCHIVED_AT) {
                        await executeQuery(
                            `UPDATE USERS SET 
                             EMAIL = ?, DEPARTMENT = ?, STATUS = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                             WHERE USERID = ?`,
                            [instructor.email || null, instructor.department, instructor.status, existing.USERID]
                        );
                        results.updated++;
                    } else {
                        // Skip if exists and not updating
                        continue;
                    }
                }

                if (existing && options.updateExisting) {
                    // Update existing instructor (including unarchiving if needed)
                    await executeQuery(
                        `UPDATE USERS SET 
                         FIRSTNAME = ?, LASTNAME = ?, EMAIL = ?, DEPARTMENT = ?, STATUS = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                         WHERE USERID = ?`,
                        [instructor.first_name, instructor.last_name, instructor.email || null,
                         instructor.department, instructor.status, existing.USERID]
                    );
                    results.updated++;
                } else if (!existing) {
                    // Create new instructor
                    await executeQuery(
                        `INSERT INTO USERS (USERID, USERTYPE, FACULTYID, FIRSTNAME, LASTNAME, EMAIL, DEPARTMENT, STATUS)
                         VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
                        [instructor.id, instructor.user_type, instructor.employee_id, instructor.first_name,
                         instructor.last_name, instructor.email || null, instructor.department, instructor.status]
                    );
                    results.created++;
                }

            } catch (error) {
                console.error('Instructor import error:', error);
                results.errors.push(`Failed to import instructor ${instructor.full_name}: ${error.message}`);
            }
        }
    }
    
    console.log(`✅ Instructors import completed: ${results.created} created, ${results.updated} updated, ${results.errors.length} errors`);
}

// Import students with batch processing for large datasets
async function importStudents(students, results, options) {
    console.log(`🎓 Importing ${students.length} students...`);
    
    // Process in batches of 100 for better performance (students are usually more numerous)
    const batchSize = 100;
    for (let i = 0; i < students.length; i += batchSize) {
        const batch = students.slice(i, i + batchSize);
        console.log(`🎓 Processing student batch ${Math.floor(i/batchSize) + 1}/${Math.ceil(students.length/batchSize)}`);
        
        for (const student of batch) {
            try {
                // Check if student exists by student ID (including archived)
                const existing = await getSingleResult(
                    'SELECT USERID, ARCHIVED_AT FROM USERS WHERE STUDENTID = ? AND USERTYPE = "student"',
                    [student.student_id]
                );

                if (existing && !options.updateExisting) {
                    // If student is archived, unarchive it; otherwise skip
                    if (existing.ARCHIVED_AT) {
                        await executeQuery(
                            `UPDATE USERS SET 
                             FIRSTNAME = ?, LASTNAME = ?, EMAIL = ?, YEARLEVEL = ?, DEPARTMENT = ?, STATUS = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                             WHERE USERID = ?`,
                            [student.first_name, student.last_name, student.email || null,
                             student.year_level, student.department, student.status, existing.USERID]
                        );
                        results.updated++;
                    } else {
                        // Skip if exists and not updating
                        continue;
                    }
                }

                if (existing && options.updateExisting) {
                    // Update existing student (including unarchiving if needed)
                    await executeQuery(
                        `UPDATE USERS SET 
                         FIRSTNAME = ?, LASTNAME = ?, EMAIL = ?, YEARLEVEL = ?, DEPARTMENT = ?, STATUS = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                         WHERE USERID = ?`,
                        [student.first_name, student.last_name, student.email || null,
                         student.year_level, student.department, student.status, existing.USERID]
                    );
                    results.updated++;
                } else if (!existing) {
                    // Create new student
                    await executeQuery(
                        `INSERT INTO USERS (USERID, USERTYPE, STUDENTID, FIRSTNAME, LASTNAME, EMAIL, YEARLEVEL, DEPARTMENT, STATUS)
                         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                        [student.id, student.user_type, student.student_id, student.first_name,
                         student.last_name, student.email || null, student.year_level, student.department, student.status]
                    );
                    results.created++;
                }

            } catch (error) {
                console.error('Student import error:', error);
                results.errors.push(`Failed to import student ${student.full_name}: ${error.message}`);
            }
        }
    }
    
    console.log(`✅ Students import completed: ${results.created} created, ${results.updated} updated, ${results.errors.length} errors`);
}

// Import rooms with batch processing for large datasets
async function importRooms(rooms, results, options) {
    console.log(`🏫 Importing ${rooms.length} rooms...`);
    
    // Process in batches of 20 for better performance
    const batchSize = 20;
    for (let i = 0; i < rooms.length; i += batchSize) {
        const batch = rooms.slice(i, i + batchSize);
        console.log(`🏫 Processing room batch ${Math.floor(i/batchSize) + 1}/${Math.ceil(rooms.length/batchSize)}`);
        
        for (const room of batch) {
            try {
                // Check if room exists by room number (including archived)
                const existing = await getSingleResult(
                    'SELECT ROOMID, ARCHIVED_AT FROM ROOMS WHERE ROOMNUMBER = ?',
                    [room.room_number]
                );

                if (existing && !options.updateExisting) {
                    // If room is archived, unarchive it; otherwise skip
                    if (existing.ARCHIVED_AT) {
                        await executeQuery(
                            `UPDATE ROOMS SET 
                             ROOMNAME = ?, BUILDING = ?, CAPACITY = ?, STATUS = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                             WHERE ROOMID = ?`,
                            [room.room_name, room.building, room.capacity, room.status, existing.ROOMID]
                        );
                        results.updated++;
                    } else {
                        // Skip if exists and not updating
                        continue;
                    }
                }

                if (existing && options.updateExisting) {
                    // Update existing room (including unarchiving if needed)
                    await executeQuery(
                        `UPDATE ROOMS SET 
                         ROOMNAME = ?, BUILDING = ?, CAPACITY = ?, STATUS = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                         WHERE ROOMID = ?`,
                        [room.room_name, room.building, room.capacity, room.status, existing.ROOMID]
                    );
                    results.updated++;
                } else if (!existing) {
                    // Create new room
                    await executeQuery(
                        `INSERT INTO ROOMS (ROOMID, ROOMNUMBER, ROOMNAME, BUILDING, CAPACITY, STATUS)
                         VALUES (?, ?, ?, ?, ?, ?)`,
                        [room.id, room.room_number, room.room_name, room.building, room.capacity, room.status]
                    );
                    results.created++;
                }

            } catch (error) {
                console.error('Room import error:', error);
                results.errors.push(`Failed to import room ${room.room_number}: ${error.message}`);
            }
        }
    }
    
    console.log(`✅ Rooms import completed: ${results.created} created, ${results.updated} updated, ${results.errors.length} errors`);
}

// Import subjects with batch processing for large datasets
async function importSubjects(subjects, results, options) {
    console.log(`📚 Importing ${subjects.length} subjects...`);
    
    // Process in batches of 30 for better performance
    const batchSize = 30;
    for (let i = 0; i < subjects.length; i += batchSize) {
        const batch = subjects.slice(i, i + batchSize);
        console.log(`📚 Processing subject batch ${Math.floor(i/batchSize) + 1}/${Math.ceil(subjects.length/batchSize)}`);
        
        for (const subject of batch) {
        try {
            // Resolve instructor by id, email, or name (in that order)
            let instructorId = null;
            let instructor = null;

            if (subject.instructor_id) {
                instructor = await getSingleResult(
                    'SELECT USERID FROM USERS WHERE USERID = ? AND USERTYPE = "instructor"',
                    [subject.instructor_id]
                );
            }

            if (!instructor && subject.instructor_faculty_id) {
                instructor = await getSingleResult(
                    'SELECT USERID FROM USERS WHERE FACULTYID = ? AND USERTYPE = "instructor"',
                    [subject.instructor_faculty_id]
                );
            }

            if (!instructor && subject.instructor_name) {
                instructor = await getSingleResult(
                    'SELECT USERID FROM USERS WHERE CONCAT(FIRSTNAME, " ", LASTNAME) = ? AND USERTYPE = "instructor"',
                    [subject.instructor_name]
                );
            }

            instructorId = instructor ? instructor.USERID : null;

            // Log instructor matching for debugging
            if (!instructorId) {
                console.log(`⚠️  No instructor found for subject ${subject.code}`);
                console.log(`   Looking for: instructor_id=${subject.instructor_id}, faculty_id=${subject.instructor_faculty_id}, name=${subject.instructor_name}`);
            } else {
                console.log(`✅ Found instructor ${instructorId} for subject ${subject.code}`);
            }

            // Check if subject exists by code, semester, academic year, description AND instructor (including archived)
            const existing = await getSingleResult(
                'SELECT SUBJECTID, ARCHIVED_AT FROM SUBJECTS WHERE SUBJECTCODE = ? AND SEMESTER = ? AND ACADEMICYEAR = ? AND DESCRIPTION <=> ? AND INSTRUCTORID <=> ?',
                [subject.code, subject.semester, subject.academic_year, subject.description, instructorId]
            );

            if (existing && !options.updateExisting) {
                // If subject is archived, unarchive it; otherwise skip
                if (existing.ARCHIVED_AT) {
                    await executeQuery(
                        `UPDATE SUBJECTS SET 
                         SUBJECTNAME = ?, INSTRUCTORID = ?, DESCRIPTION = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                         WHERE SUBJECTID = ?`,
                        [subject.name, instructorId, subject.description, existing.SUBJECTID]
                    );
                    results.updated++;
                } else {
                    // Skip if exists and not updating
                    continue;
                }
            }

            if (existing && options.updateExisting) {
                // Update existing subject (including unarchiving if needed)
                await executeQuery(
                    `UPDATE SUBJECTS SET 
                     SUBJECTNAME = ?, INSTRUCTORID = ?, DESCRIPTION = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE SUBJECTID = ?`,
                    [subject.name, instructorId, subject.description, existing.SUBJECTID]
                );
                results.updated++;
            } else if (!existing) {
                // Create new distinct subject (allow null instructor ID)
                await executeQuery(
                    `INSERT INTO SUBJECTS (SUBJECTID, SUBJECTCODE, SUBJECTNAME, INSTRUCTORID, SEMESTER, YEAR, ACADEMICYEAR, DESCRIPTION)
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
                    [subject.id, subject.code, subject.name, instructorId || null, subject.semester, 
                     subject.year, subject.academic_year, subject.description]
                );
                results.created++;
            }

        } catch (error) {
            console.error('Subject import error:', error);
            console.error('Subject data:', JSON.stringify(subject, null, 2));
            results.errors.push(`Failed to import subject ${subject.code}: ${error.message}`);
        }
        }
    }
    
    console.log(`✅ Subjects import completed: ${results.created} created, ${results.updated} updated, ${results.errors.length} errors`);
}

// Import schedules with batch processing for large datasets
async function importSchedules(schedules, results, options) {
    console.log(`📅 Importing ${schedules.length} schedules...`);
    
    // Process in batches of 50 for better performance
    const batchSize = 50;
    for (let i = 0; i < schedules.length; i += batchSize) {
        const batch = schedules.slice(i, i + batchSize);
        console.log(`📅 Processing schedule batch ${Math.floor(i/batchSize) + 1}/${Math.ceil(schedules.length/batchSize)}`);
        
        for (const schedule of batch) {
        try {
            // Get room ID if room exists (including TBA rooms)
            let roomId = null;
            if (schedule.room_number) {
                const room = await getSingleResult(
                    'SELECT ROOMID FROM ROOMS WHERE ROOMNUMBER = ?',
                    [schedule.room_number]
                );
                roomId = room ? room.ROOMID : null;
            }

            // Resolve subject ID: prefer provided subject_id; fallback to code+term, then code+term+description
            // Only look up non-archived subjects
            let subject = null;
            if (schedule.subject_id) {
                subject = await getSingleResult(
                    'SELECT SUBJECTID FROM SUBJECTS WHERE SUBJECTID = ? AND ARCHIVED_AT IS NULL',
                    [schedule.subject_id]
                );
            }
            // Prefer matching by description (section-aware) before broad code+term
            if (!subject && schedule.description) {
                subject = await getSingleResult(
                    'SELECT SUBJECTID FROM SUBJECTS WHERE SUBJECTCODE = ? AND SEMESTER = ? AND ACADEMICYEAR = ? AND DESCRIPTION <=> ? AND ARCHIVED_AT IS NULL',
                    [schedule.subject_code, schedule.semester, schedule.academic_year, schedule.description]
                );
            }
            // REMOVED: Fallback to broad code+term matching to prevent incorrect subject linking
            // This was causing schedules to link to wrong sections when multiple sections exist

            if (!subject) {
                results.errors.push(`Subject not found for schedule: ${schedule.subject_code}`);
                continue;
            }

            // Check for existing schedule to avoid duplicates (including archived)
            // If ISLAB column isn't present yet, select without it
            let existing;
            try {
                existing = await getSingleResult(
                    `SELECT SCHEDULEID, ISLAB, ARCHIVED_AT FROM CLASSSCHEDULES 
                     WHERE SUBJECTID = ? AND DAYOFWEEK = ? AND STARTTIME = ? AND ENDTIME = ?`,
                    [subject.SUBJECTID, schedule.day_of_week, schedule.start_time, schedule.end_time]
                );
            } catch (e) {
                if (e.code === 'ER_BAD_FIELD_ERROR') {
                    existing = await getSingleResult(
                        `SELECT SCHEDULEID, ARCHIVED_AT FROM CLASSSCHEDULES 
                         WHERE SUBJECTID = ? AND DAYOFWEEK = ? AND STARTTIME = ? AND ENDTIME = ?`,
                        [subject.SUBJECTID, schedule.day_of_week, schedule.start_time, schedule.end_time]
                    );
                } else {
                    throw e;
                }
            }

            if (existing && !options.updateExisting) {
                // If schedule is archived, unarchive it; otherwise backfill ISLAB flag
                if (existing.ARCHIVED_AT) {
                    await executeQuery(
                        `UPDATE CLASSSCHEDULES SET 
                         ROOMID = ?, ISLAB = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                         WHERE SCHEDULEID = ?`,
                        [roomId, schedule.is_lab ? 1 : 0, existing.SCHEDULEID]
                    );
                    results.updated++;
                } else {
                    // Backfill only the ISLAB flag if it differs
                    const targetLab = schedule.is_lab ? 1 : 0;
                    if (typeof existing.ISLAB === 'number' && existing.ISLAB !== targetLab) {
                        await executeQuery(
                            `UPDATE CLASSSCHEDULES SET ISLAB = ?, UPDATED_AT = CURRENT_TIMESTAMP WHERE SCHEDULEID = ?`,
                            [targetLab, existing.SCHEDULEID]
                        );
                        results.updated++;
                    }
                }
                continue;
            }

            if (existing && options.updateExisting) {
                // Update existing schedule (including unarchiving if needed)
                await executeQuery(
                    `UPDATE CLASSSCHEDULES SET 
                     ROOMID = ?, ISLAB = ?, ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL, UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE SCHEDULEID = ?`,
                    [roomId, schedule.is_lab ? 1 : 0, existing.SCHEDULEID]
                );
                results.updated++;
            } else if (!existing) {
                // Create new schedule
                await executeQuery(
                    `INSERT INTO CLASSSCHEDULES (SCHEDULEID, SUBJECTID, ROOMID, DAYOFWEEK, STARTTIME, ENDTIME, ACADEMICYEAR, SEMESTER, ISLAB)
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                    [schedule.id, subject.SUBJECTID, roomId, schedule.day_of_week, 
                     schedule.start_time, schedule.end_time, schedule.academic_year, schedule.semester, schedule.is_lab ? 1 : 0]
                );
                results.created++;
            }

        } catch (error) {
            console.error('Schedule import error:', error);
            results.errors.push(`Failed to import schedule for ${schedule.subject_code}: ${error.message}`);
        }
        }
    }
    
    console.log(`✅ Schedules import completed: ${results.created} created, ${results.updated} updated, ${results.errors.length} errors`);
}

// Import enrollments with batch processing for large datasets
async function importEnrollments(enrollments, results, options) {
    console.log(`🎓 Importing ${enrollments.length} enrollments...`);
    
    // Process in batches of 100 for better performance
    const batchSize = 100;
    for (let i = 0; i < enrollments.length; i += batchSize) {
        const batch = enrollments.slice(i, i + batchSize);
        console.log(`🎓 Processing enrollment batch ${Math.floor(i/batchSize) + 1}/${Math.ceil(enrollments.length/batchSize)}`);
        
        for (const enrollment of batch) {
        try {
            // Get actual user ID by student ID and subject ID from database (including archived)
            const user = await getSingleResult(
                'SELECT USERID, ARCHIVED_AT FROM USERS WHERE STUDENTID = ? AND USERTYPE = "student"',
                [enrollment.student_id]
            );
            
            // If user is archived, unarchive them
            if (user && user.ARCHIVED_AT) {
                await executeQuery(
                    'UPDATE USERS SET ARCHIVED_AT = NULL, ARCHIVED_BY = NULL, ARCHIVE_REASON = NULL WHERE USERID = ?',
                    [user.USERID]
                );
            }

            // Resolve subject: prefer exact SUBJECTID from parsed data, then code+term+description, then code+term
            // Only look up non-archived subjects
            let subject = null;
            if (enrollment.subject_id) {
                subject = await getSingleResult(
                    'SELECT SUBJECTID FROM SUBJECTS WHERE SUBJECTID = ? AND ARCHIVED_AT IS NULL',
                    [enrollment.subject_id]
                );
            }
            if (!subject && enrollment.subject_code && enrollment.description) {
                subject = await getSingleResult(
                    'SELECT SUBJECTID FROM SUBJECTS WHERE SUBJECTCODE = ? AND ACADEMICYEAR = ? AND SEMESTER = ? AND DESCRIPTION <=> ? AND ARCHIVED_AT IS NULL',
                    [enrollment.subject_code.replace(/\s+/g, '').toUpperCase(), enrollment.academic_year, enrollment.semester, enrollment.description]
                );
            }
            // REMOVED: Fallback to broad code+term matching to prevent incorrect subject linking
            // This was causing enrollments to link to wrong sections when multiple sections exist

            if (!user || !subject) {
                results.errors.push(`User or subject not found for enrollment`);
                continue;
            }

            // Check if enrollment exists
            const existing = await getSingleResult(
                `SELECT ENROLLMENTID FROM SUBJECTENROLLMENT 
                 WHERE USERID = ? AND SUBJECTID = ? AND ACADEMICYEAR = ? AND SEMESTER = ?`,
                [user.USERID, subject.SUBJECTID, enrollment.academic_year, enrollment.semester]
            );

            if (existing && !options.updateExisting) {
                // Skip if exists and not updating
                continue;
            }

            if (existing && options.updateExisting) {
                // Update existing enrollment
                await executeQuery(
                    `UPDATE SUBJECTENROLLMENT SET 
                     STATUS = ?, UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ENROLLMENTID = ?`,
                    [enrollment.status, existing.ENROLLMENTID]
                );
                results.updated++;
            } else {
                // Create new enrollment
                await executeQuery(
                    `INSERT INTO SUBJECTENROLLMENT (ENROLLMENTID, USERID, SUBJECTID, ACADEMICYEAR, SEMESTER, STATUS)
                     VALUES (?, ?, ?, ?, ?, ?)`,
                    [enrollment.id, user.USERID, subject.SUBJECTID, 
                     enrollment.academic_year, enrollment.semester, enrollment.status]
                );
                results.created++;
            }

        } catch (error) {
            console.error('Enrollment import error:', error);
            results.errors.push(`Failed to import enrollment: ${error.message}`);
        }
        }
    }
    
    console.log(`✅ Enrollments import completed: ${results.created} created, ${results.updated} updated, ${results.errors.length} errors`);
}

// Create import log
async function createImportLog(parsedData, importResults) {
    try {
        const logId = uuidv4();
        const summary = {
            total_subjects: importResults.subjects.created + importResults.subjects.updated,
            total_students: importResults.students.created + importResults.students.updated,
            total_instructors: importResults.instructors.created + importResults.instructors.updated,
            total_rooms: importResults.rooms.created + importResults.rooms.updated,
            total_schedules: importResults.schedules.created + importResults.schedules.updated,
            total_enrollments: importResults.enrollments.created + importResults.enrollments.updated
        };

        await executeQuery(
            `INSERT INTO IMPORT_LOGS (ID, ACADEMIC_YEAR, SEMESTER, IMPORT_DATE, SUMMARY, STATUS)
             VALUES (?, ?, ?, ?, ?, ?)`,
            [logId, parsedData.metadata.academic_year, parsedData.metadata.semester, 
             new Date(), JSON.stringify(summary), 'completed']
        );

    } catch (error) {
        console.error('Failed to create import log:', error);
    }
}

// Get import history
router.get('/history', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const { page = 1, limit = 10 } = req.query;
        const offset = (page - 1) * limit;

        const imports = await executeQuery(
            `SELECT * FROM IMPORT_LOGS 
             ORDER BY IMPORT_DATE DESC 
             LIMIT ? OFFSET ?`,
            [parseInt(limit), offset]
        );

        const totalCount = await getSingleResult(
            'SELECT COUNT(*) as total FROM IMPORT_LOGS'
        );

        res.json({
            imports: imports.map(imp => ({
                ...imp,
                SUMMARY: JSON.parse(imp.SUMMARY || '{}')
            })),
            pagination: {
                page: parseInt(page),
                limit: parseInt(limit),
                total: totalCount.total,
                pages: Math.ceil(totalCount.total / limit)
            }
        });

    } catch (error) {
        console.error('Import history error:', error);
        res.status(500).json({ message: 'Failed to fetch import history' });
    }
});

module.exports = router;
