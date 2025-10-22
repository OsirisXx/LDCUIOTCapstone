const pdf = require('pdf-parse');
const { v4: uuidv4 } = require('uuid');

class PDFParserService {
    constructor() {
        // Day abbreviations mapping
        this.dayMappings = {
            'M': 'Monday',
            'T': 'Tuesday', 
            'W': 'Wednesday',
            'TH': 'Thursday',
            'F': 'Friday',
            'S': 'Saturday',
            'SAT': 'Saturday',
            'SUN': 'Sunday'
        };
    }

    /**
     * Infer undergraduate year from subject context
     * - Prefer section like BSIT3-1 → 3
     * - Else from subject code like BSIT-3101 → 3 (hundreds digit)
     */
    inferYearFromContext(subject) {
        if (!subject) return null;
        try {
            const section = String(subject.section || '');
            const code = String(subject.code || '');

            // From section e.g., BSIT3-1, BLIS4-1
            const secMatch = section.match(/([A-Z]{2,}\d)-?(\d+)/);
            if (secMatch) {
                const digits = section.match(/(\d)/);
                const yr = digits ? parseInt(digits[1], 10) : null;
                if (yr && yr >= 1 && yr <= 5) return yr;
            }

            // From subject code e.g., BSIT-3101, BLIS-4102
            const codeNum = code.match(/-(\d{4})/);
            if (codeNum) {
                const n = codeNum[1];
                const yr = parseInt(n[0], 10); // hundreds place indicates year
                if (yr && yr >= 1 && yr <= 5) return yr;
            }
        } catch (_) { /* ignore */ }
        return null;
    }

    /**
     * Parse PDF buffer and extract academic data
     * @param {Buffer} pdfBuffer - PDF file buffer
     * @returns {Object} Parsed academic data
     */
    async parsePDF(pdfBuffer) {
        try {
            console.log('📄 Starting PDF parsing...');
            console.log('📄 PDF buffer size:', pdfBuffer.length, 'bytes');
            
            // Parse PDF with no timeout limits
            const pdfData = await pdf(pdfBuffer, {
                max: 0, // No page limit
                version: 'v1.10.100' // Use latest version
            });
            const text = pdfData.text;
            
            console.log('📄 Raw PDF text (first 2000 chars):', text.substring(0, 2000));
            console.log('📄 PDF text length:', text.length);
            
            // Search for student data patterns
            const studentPatterns = [
                /Count\s+ID Number\s+Name\s+Gender\s+Course\s+Year/,
                /^\d+\.\s+\d+\s+[A-Z\s,.-]+\s+[MF]\s+[A-Z\s-]+\s+\d+$/m
            ];
            
            studentPatterns.forEach((pattern, index) => {
                const matches = text.match(pattern);
                console.log(`🔍 Student pattern ${index + 1} matches:`, matches ? matches.length : 0);
                if (matches && matches.length > 0) {
                    console.log(`📝 Sample match:`, matches[0]);
                }
            });
            
            // Split into pages/sections based on university header
            const sections = this.splitIntoSections(text);
            console.log('📑 Found sections:', sections.length);
            
            const parsedData = {
                metadata: this.extractMetadata(text),
                subjects: [],
                rooms: new Map(),
                instructors: new Map(),
                students: new Map(),
                schedules: [],
                enrollments: []
            };

            // Process each section with memory optimization
            console.log(`\n🔄 Processing ${sections.length} sections...`);
            
            for (let i = 0; i < sections.length; i++) {
                const section = sections[i];
                console.log(`\n🔍 Processing section ${i + 1}/${sections.length}:`);
                console.log('Section text (first 500 chars):', section.substring(0, 500));
                
                try {
                    const sectionData = this.parseSection(section);
                    if (sectionData) {
                        console.log('✅ Section parsed successfully:');
                        console.log('- Subject:', sectionData.subject?.code, sectionData.subject?.name);
                        console.log('- Students:', sectionData.students?.length || 0);
                        console.log('- Instructors:', sectionData.instructors?.length || 0);
                        console.log('- Schedules:', sectionData.schedules?.length || 0);
                        
                        // Validate section data before consolidating
                        if (this.validateSectionData(sectionData)) {
                            this.consolidateData(parsedData, sectionData);
                            console.log(`✅ Section ${i + 1} consolidated successfully`);
                        } else {
                            console.log('❌ Section data validation failed, skipping');
                        }
                    } else {
                        console.log('❌ Failed to parse section - no valid data extracted');
                    }
                } catch (error) {
                    console.error(`❌ Error processing section ${i + 1}:`, error);
                    // Continue processing other sections
                }
                
                // Clear section from memory to prevent memory buildup
                sections[i] = null;
                
                // Log progress for large PDFs
                if (i % 10 === 0 && i > 0) {
                    console.log(`📊 Progress: ${i}/${sections.length} sections processed`);
                }
            }

            console.log('\n📊 Final parsed data summary:');
            console.log('- Subjects:', parsedData.subjects.length);
            console.log('- Students:', parsedData.students.size);
            console.log('- Instructors:', parsedData.instructors.size);
            console.log('- Rooms:', parsedData.rooms.size);
            console.log('- Schedules:', parsedData.schedules.length);
            
            // Log detailed breakdown by subject code
            console.log('\n📚 Subjects breakdown:');
            parsedData.subjects.forEach(subject => {
                console.log(`- ${subject.code}: ${subject.name} (${subject.section})`);
            });
            
            // Log schedules breakdown by subject
            console.log('\n📅 Schedules breakdown:');
            parsedData.schedules.forEach(schedule => {
                console.log(`- ${schedule.subject_code}: ${schedule.day_of_week} ${schedule.start_time}-${schedule.end_time} ${schedule.room_number}`);
            });

            // Convert Maps to Arrays for final output
            const instructorsArray = Array.from(parsedData.instructors.values());
            console.log('🔍 Final instructor array length:', instructorsArray.length);
            console.log('🔍 Final instructor map size:', parsedData.instructors.size);
            console.log('🔍 Instructor names in final array:', instructorsArray.map(i => i.name || 'NO_NAME'));
            console.log('🔍 Sample instructor object:', instructorsArray[0]);
            
            return {
                metadata: parsedData.metadata,
                subjects: parsedData.subjects,
                rooms: Array.from(parsedData.rooms.values()),
                instructors: instructorsArray,
                students: Array.from(parsedData.students.values()),
                schedules: parsedData.schedules,
                enrollments: parsedData.enrollments,
                statistics: this.generateStatistics(parsedData)
            };

        } catch (error) {
            console.error('PDF parsing error:', error);
            throw new Error(`Failed to parse PDF: ${error.message}`);
        }
    }

    /**
     * Split text into sections based on university header
     * @param {string} text - Full PDF text
     * @returns {Array} Array of section texts
     */
    splitIntoSections(text) {
        console.log('📄 Starting section splitting...');
        console.log('📄 PDF text length:', text.length);
        console.log('📄 First 1000 chars:', text.substring(0, 1000));
        
        // Try multiple header patterns to catch all sections
        const headerPatterns = [
            /LICEO DE CAGAYAN UNIVERSITY\s+FINAL AND OFFICIAL LIST OF STUDENTS\./gi,
            /LICEO DE CAGAYAN UNIVERSITY/gi,
            /FINAL AND OFFICIAL LIST OF STUDENTS/gi,
            /First Term, SY \d{4} - \d{4}/gi,
            /Subject Code:/gi
        ];
        
        let sections = [text]; // Start with full text
        
        // Try each pattern until we get a good split
        for (const pattern of headerPatterns) {
            const testSections = text.split(pattern);
            console.log(`📑 Testing pattern "${pattern}" - found ${testSections.length} sections`);
            
            // If we get a reasonable number of sections (more than 1, less than 1000), use it
            if (testSections.length > 1 && testSections.length < 1000) {
                sections = testSections;
                console.log(`📑 Using pattern "${pattern}" - ${sections.length} sections`);
                break;
            }
        }
        
        console.log('📑 Split into', sections.length, 'sections using best pattern');
        
        // Remove empty first element and filter sections
        const validSections = sections.slice(1).filter((section, index) => {
            const trimmed = section.trim();
            
            // Log each section for debugging
            console.log(`📑 Section ${index + 1} length:`, trimmed.length);
            console.log(`📑 Section ${index + 1} preview:`, trimmed.substring(0, 200));
            
            // Must contain key identifiers (no minimum length requirement)
            const hasSubjectCode = trimmed.includes('Subject Code:');
            const hasStudentList = trimmed.includes('Count');
            const hasDatePrinted = trimmed.includes('Date Printed:');
            const hasPageInfo = trimmed.includes('Page #:');
            const hasFaculty = trimmed.includes('Faculty:');
            const hasTime = trimmed.includes('Time:');
            const hasStudentId = /\d{7,12}/.test(trimmed); // Look for student ID patterns
            const hasStudentName = /[A-Z][A-Z\s,.'-]+/.test(trimmed); // Look for name patterns
            
            console.log(`📑 Section ${index + 1} validation:`, {
                hasSubjectCode,
                hasStudentList,
                hasDatePrinted,
                hasPageInfo,
                hasFaculty,
                hasTime,
                hasStudentId,
                hasStudentName
            });
            
            // Accept section if it has at least one key identifier (no length requirement)
            // Be more flexible - accept sections with student data even without headers
            return hasSubjectCode || hasStudentList || hasDatePrinted || hasPageInfo || hasFaculty || hasTime || hasStudentId || hasStudentName;
        });
        
        console.log('📑 Valid sections after filtering:', validSections.length);
        
        // Log each valid section for debugging
        validSections.forEach((section, index) => {
            console.log(`📑 Valid Section ${index + 1}:`);
            console.log('- Length:', section.length);
            console.log('- Preview:', section.substring(0, 300));
            console.log('- Contains Subject Code:', section.includes('Subject Code:'));
            console.log('- Contains Count:', section.includes('Count'));
        });
        
        return validSections;
    }

    /**
     * Extract document metadata (academic year, semester, etc.)
     * @param {string} text - PDF text
     * @returns {Object} Metadata object
     */
    extractMetadata(text) {
        const metadata = {};
        
        // Extract academic year and semester
        const termMatch = text.match(/First Term|Second Term|Summer Term/i);
        const syMatch = text.match(/SY\s+(\d{4})\s*-\s*(\d{4})/i);
        const dateMatch = text.match(/Date Printed:\s*(.+?)(?:\n|Page)/i);

        if (termMatch) {
            const term = termMatch[0].toLowerCase();
            if (term.includes('first')) {
                metadata.semester = 'First Semester';
            } else if (term.includes('second')) {
                metadata.semester = 'Second Semester';
            } else if (term.includes('summer')) {
                metadata.semester = 'Summer';
            }
        }

        if (syMatch) {
            metadata.academic_year = `${syMatch[1]}-${syMatch[2]}`;
            metadata.year = parseInt(syMatch[1]);
        }

        if (dateMatch) {
            metadata.import_date = new Date(dateMatch[1].trim());
        }

        metadata.institution = 'LICEO DE CAGAYAN UNIVERSITY';
        
        return metadata;
    }

    /**
     * Parse a single section (subject/class)
     * @param {string} sectionText - Text of one section
     * @returns {Object} Parsed section data
     */
    parseSection(sectionText) {
        try {
            const lines = sectionText.split('\n').map(line => line.trim()).filter(line => line.length > 0);
            console.log('🔍 Section lines count:', lines.length);
            console.log('🔍 First 15 lines:', lines.slice(0, 15));
            
            // Check if this section contains the problematic student
            const hasProblematicStudent = lines.some(line => line.includes('20255237912') || line.includes('RESOMADERO'));
            if (hasProblematicStudent) {
                console.log('🔍 FOUND PROBLEMATIC STUDENT SECTION!');
                console.log('🔍 Section text (first 2000 chars):', sectionText.substring(0, 2000));
                console.log('🔍 All lines in section:', lines);
            }
            
            const sectionData = {
                subject: null,
                schedules: [],
                students: [],
                instructors: []
            };

            // Extract subject information
            sectionData.subject = this.extractSubjectInfo(lines);
            if (!sectionData.subject) return null;

            // Extract schedule information (can be multiple)
            sectionData.schedules = this.extractScheduleInfo(lines, sectionData.subject);
            
            // If no schedules found, create a default TBA schedule
            if (sectionData.schedules.length === 0) {
                console.log('⚠️ No schedules found in this section, creating default TBA schedule');
                sectionData.schedules = [{
                    id: uuidv4(),
                    subject_id: sectionData.subject.id,
                    subject_code: sectionData.subject.code,
                    day_of_week: 'Monday',
                    start_time: '08:00',
                    end_time: '09:00',
                    room_number: 'TBA',
                    room_name: 'To Be Announced',
                    building: '',
                    type: 'Lecture',
                    is_lab: false
                }];
            }

            // Extract instructor information
            sectionData.instructors = this.extractInstructorInfo(lines);
            
            // If no instructors found, create a default "No Faculty Assigned" entry
            if (sectionData.instructors.length === 0) {
                console.log('⚠️ No instructors found in this section, creating default entry');
                sectionData.instructors = [{
                    id: uuidv4(),
                    name: 'No Faculty Assigned',
                    full_name: {
                        first_name: 'No',
                        last_name: 'Faculty Assigned',
                        middle_name: '',
                        full_name: 'No Faculty Assigned'
                    },
                    type: 'main'
                }];
            }

            // Extract student list
            sectionData.students = this.extractStudentList(lines);
            
            // If no students found, log warning but don't fail
            if (sectionData.students.length === 0) {
                console.log('⚠️ No students found in this section');
            }

            return sectionData;

        } catch (error) {
            console.error('Section parsing error:', error);
            return null;
        }
    }

    /**
     * Extract subject information from section
     * @param {Array} lines - Array of text lines
     * @returns {Object} Subject data
     */
    extractSubjectInfo(lines) {
        const subject = {
            id: uuidv4(),
            code: null,
            name: null,
            section: null,
            type: 'Lec'
        };

        // Look for the subject information block at the beginning of the section
        // Expected format:
        // Subject Code: BLIS-1101
        // Time: T 5:30PM-7:00PM Room: TBA
        // Type: Lec
        // Descriptive Title: Introduction to Library and InformationScience
        // Section: BLIS1-1
        // Faculty:PAJARA, RICHEL I.

        console.log('🔍 Extracting subject info from first 20 lines:');
        console.log(lines.slice(0, 20));

        // Find the subject information block (scan all lines to be thorough)
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];

            // Subject Code
            if (line.includes('Subject Code:')) {
                let code = line.replace(/.*Subject Code:\s*/i, '').trim();
                // Clean up any mixed data that might be on the same line
                code = this.extractFieldValue(code, ['Time:', 'Room:', 'Type:', 'Descriptive Title:', 'Section:', 'Faculty:']);
                subject.code = code;
                console.log('📚 Found Subject Code:', code);
            }

            // Descriptive Title
            if (line.includes('Descriptive Title:')) {
                let name = line.replace(/.*Descriptive Title:\s*/i, '').trim();
                // Clean up any mixed data
                name = this.extractFieldValue(name, ['Time:', 'Room:', 'Type:', 'Section:', 'Faculty:', 'Subject Code:']);
                
                // If the title is split across multiple lines, capture continuation
                if (name && name.length > 0) {
                    let j = i + 1;
                    while (j < lines.length && j < i + 10) { // Allow up to 10 continuation lines
                        const nextLine = lines[j].trim();
                        // Stop if we hit another field label
                        if (this.isFieldLabel(nextLine)) break;
                        // Add continuation if it looks like part of the title
                        if (nextLine && !this.isFieldLabel(nextLine) && nextLine.length > 3) {
                            name = `${name} ${nextLine}`.trim();
                        }
                        j++;
                    }
                }
                subject.name = name;
                console.log('📚 Found Descriptive Title:', name);
            }

            // Section
            if (line.includes('Section:')) {
                let section = line.replace(/.*Section:\s*/i, '').trim();
                section = this.extractFieldValue(section, ['Time:', 'Room:', 'Type:', 'Faculty:', 'Subject Code:', 'Descriptive Title:']);
                subject.section = section;
                console.log('📚 Found Section:', section);
            }

            // Type
            if (line.includes('Type:')) {
                let type = line.replace(/.*Type:\s*/i, '').trim();
                type = this.extractFieldValue(type, ['Time:', 'Room:', 'Section:', 'Faculty:', 'Subject Code:', 'Descriptive Title:']);
                subject.type = type;
                console.log('📚 Found Type:', type);
            }
        }

        console.log(`📚 Extracted subject: ${subject.code} - ${subject.name} (${subject.section})`);
        
        // Fill in default values for missing fields
        if (!subject.code) {
            subject.code = 'UNKNOWN-' + Date.now().toString().slice(-4);
            console.log('⚠️ No subject code found, using default:', subject.code);
        }
        
        if (!subject.name) {
            subject.name = 'Unknown Subject';
            console.log('⚠️ No subject name found, using default:', subject.name);
        }
        
        if (!subject.section) {
            // Use subject code + timestamp to create unique section identifier
            const timestamp = Date.now().toString().slice(-4);
            subject.section = `${subject.code || 'UNK'}-${timestamp}`;
            console.log('⚠️ No section found, using generated unique identifier:', subject.section);
        }
        
        if (!subject.type) {
            subject.type = 'Lec';
            console.log('⚠️ No type found, using default:', subject.type);
        }
        
        console.log(`📚 Final subject: ${subject.code} - ${subject.name} (${subject.section})`);
        return subject;
    }

    /**
     * Extract field value and clean up any mixed data
     * @param {string} value - Raw field value
     * @param {Array} stopFields - Fields to stop at
     * @returns {string} Cleaned field value
     */
    extractFieldValue(value, stopFields) {
        if (!value) return '';
        
        // Find the earliest occurrence of any stop field
        let earliestStop = value.length;
        for (const stopField of stopFields) {
            const stopIndex = value.indexOf(stopField);
            if (stopIndex !== -1 && stopIndex < earliestStop) {
                earliestStop = stopIndex;
            }
        }
        
        return value.substring(0, earliestStop).trim();
    }

    /**
     * Check if a line is a field label
     * @param {string} line - Line to check
     * @returns {boolean} True if it's a field label
     */
    isFieldLabel(line) {
        const fieldLabels = [
            'Subject Code:',
            'Descriptive Title:',
            'Section:',
            'Type:',
            'Time:',
            'Faculty:',
            'Date Printed:',
            'Count',
            'ID Number',
            'Name',
            'Gender',
            'Course',
            'Year'
        ];
        
        return fieldLabels.some(label => line.startsWith(label));
    }

    /**
     * Validate section data before consolidating
     * @param {Object} sectionData - Section data to validate
     * @returns {boolean} True if valid
     */
    validateSectionData(sectionData) {
        console.log('🔍 Validating section data...');
        console.log('🔍 Subject:', sectionData.subject);
        console.log('🔍 Students count:', sectionData.students?.length || 0);
        console.log('🔍 Instructors count:', sectionData.instructors?.length || 0);
        console.log('🔍 Schedules count:', sectionData.schedules?.length || 0);

        // Must have at least a subject
        if (!sectionData.subject) {
            console.log('❌ Validation failed: No subject found');
            return false;
        }

        // Subject must have either code or name (be more flexible)
        if (!sectionData.subject.code && !sectionData.subject.name) {
            console.log('⚠️ Subject missing both code and name, but continuing...');
            // Don't fail validation, just log warning
        }

        // Students are optional for validation; we still import the subject/schedule
        let validStudents = 0;
        if (Array.isArray(sectionData.students)) {
            for (const student of sectionData.students) {
                if (student && student.student_id && student.full_name && student.gender && student.course) {
                    validStudents++;
                }
            }
        }

        console.log(`✅ Section data validation passed (valid students: ${validStudents})`);
        return true;
    }

    /**
     * Extract schedule information (can be multiple schedules)
     * @param {Array} lines - Array of text lines
     * @param {Object} subject - Subject data
     * @returns {Array} Array of schedule objects
     */
    extractScheduleInfo(lines, subject) {
        const schedules = [];
        let currentRoom = 'TBA';

        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];

            // Main schedule line: "Time: TF 6:00PM-7:00PM Room: WAC-201" (can be combined with other fields)
            if (line.includes('Time:')) {
                const scheduleData = this.parseScheduleLine(line, subject, false);
                if (scheduleData) {
                    schedules.push(...scheduleData);
                    // Update current room for potential lab schedules
                    if (scheduleData.length > 0) {
                        currentRoom = scheduleData[0].room_number || 'TBA';
                    }
                }
            }

            // Lab schedule line: "(LAB) MTH 5:00PM-6:30PM Room: WAC-301"
            if (/^\(LAB\)/i.test(line)) {
                const scheduleData = this.parseScheduleLine(line, subject, true);
                if (scheduleData) {
                    schedules.push(...scheduleData);
                }
            }
        }

        return schedules;
    }

    /**
     * Parse a single schedule line
     * @param {string} line - Schedule line text
     * @param {Object} subject - Subject data
     * @param {boolean} isLab - Whether this is a lab schedule
     * @returns {Array} Array of schedule objects (one per day)
     */
    parseScheduleLine(line, subject, isLab = false) {
        const schedules = [];

        try {
            // Remove (LAB) prefix if present
            const hasLabPrefix = /^\(LAB\)/i.test(line);
            let cleanLine = line.replace(/^\(LAB\)/i, '').replace(/.*Time:/i, '').trim();
            
            // Use the passed isLab parameter, not the prefix detection
            const actualIsLab = isLab || hasLabPrefix;
            
            // Extract time and room
            const timeRoomMatch = cleanLine.match(/([MTWFSH]+|SAT|SUN)\s+(\d{1,2}:\d{2}[AP]M)-(\d{1,2}:\d{2}[AP]M)\s+Room:\s*(.+?)(?:\s|$)/i);
            
            if (timeRoomMatch) {
                const [, dayStr, startTime, endTime, roomStr] = timeRoomMatch;
                const days = this.parseDayString(dayStr);
                const room = this.cleanRoomString(roomStr);

                // Create schedule for each day
                for (const day of days) {
                    schedules.push({
                        id: uuidv4(),
                        subject_id: subject.id,
                        subject_code: subject.code,
                        day_of_week: day,
                        start_time: this.convertTo24Hour(startTime),
                        end_time: this.convertTo24Hour(endTime),
                        room_number: room,
                        room_name: this.generateRoomName(room),
                        building: this.extractBuilding(room),
                        type: actualIsLab ? 'Laboratory' : 'Lecture',
                        is_lab: actualIsLab
                    });
                }
            }

        } catch (error) {
            console.error('Schedule line parsing error:', error);
        }

        return schedules;
    }

    /**
     * Clean room string by removing trailing tokens like "Type:", "Descriptive Title:", etc.
     */
    cleanRoomString(roomStr) {
        if (!roomStr) return '';
        let room = String(roomStr).trim();
        // If concatenated without space (e.g., "WAC-201Type:"), cut at Type:
        const stopTokens = [
            'Type:',
            'Descriptive Title:',
            'Section:',
            'Faculty:',
            'Count',
            'Time:',
        ];
        let earliest = room.length;
        for (const token of stopTokens) {
            const idx = room.indexOf(token);
            if (idx !== -1 && idx < earliest) earliest = idx;
        }
        room = room.substring(0, earliest).trim();
        // Remove stray punctuation
        room = room.replace(/[.;,]+$/g, '').trim();
        return room;
    }

    /**
     * Parse day string (e.g., "TF" -> ["Tuesday", "Friday"])
     * @param {string} dayStr - Day string
     * @returns {Array} Array of day names
     */
    parseDayString(dayStr) {
        const days = [];
        const dayStr_upper = dayStr.toUpperCase();

        // Handle special cases
        if (dayStr_upper === 'SAT' || dayStr_upper === 'SATURDAY') {
            return ['Saturday'];
        }
        if (dayStr_upper === 'SUN' || dayStr_upper === 'SUNDAY') {
            return ['Sunday'];
        }

        // Handle combined days like "TF", "MTH", "MW"
        let i = 0;
        while (i < dayStr_upper.length) {
            // Check for "TH" first (two characters)
            if (i < dayStr_upper.length - 1 && dayStr_upper.substr(i, 2) === 'TH') {
                days.push('Thursday');
                i += 2;
            } else {
                // Single character days
                const char = dayStr_upper[i];
                if (this.dayMappings[char]) {
                    days.push(this.dayMappings[char]);
                }
                i++;
            }
        }

        return days;
    }

    /**
     * Convert 12-hour time to 24-hour format
     * @param {string} time12 - Time in 12-hour format (e.g., "5:30PM")
     * @returns {string} Time in 24-hour format (e.g., "17:30")
     */
    convertTo24Hour(time12) {
        const match = time12.match(/(\d{1,2}):(\d{2})(AM|PM)/i);
        if (!match) return time12;

        let [, hours, minutes, period] = match;
        hours = parseInt(hours);
        
        if (period.toUpperCase() === 'PM' && hours !== 12) {
            hours += 12;
        } else if (period.toUpperCase() === 'AM' && hours === 12) {
            hours = 0;
        }

        return `${hours.toString().padStart(2, '0')}:${minutes}`;
    }

    /**
     * Extract instructor information
     * @param {Array} lines - Array of text lines
     * @returns {Array} Array of instructor objects
     */
    extractInstructorInfo(lines) {
        const instructors = [];
        const instructorIds = new Set(); // store normalized keys

        console.log('🔍 Extracting instructor info from', lines.length, 'lines');
        console.log('🔍 Sample lines:', lines.slice(0, 10));
        
        // Check if any lines contain "Faculty:" at all
        const facultyLines = lines.filter(line => /Faculty\s*:/i.test(line));
        console.log('🔍 Lines containing "Faculty:":', facultyLines);

        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];

            // Focus specifically on Faculty: tag detection
            if (/Faculty\s*:/i.test(line)) {
                console.log('🔍 Found Faculty line:', line);
                
                // Extract faculty name from the line
                let facultyName = line.replace(/.*Faculty\s*:/i, '').trim();
                console.log('🔍 Extracted faculty name:', facultyName);
                
                // Clean up any mixed data
                facultyName = facultyName.split('Time:')[0].trim();
                facultyName = facultyName.split('Room:')[0].trim();
                facultyName = facultyName.split('Type:')[0].trim();
                facultyName = facultyName.split('Section:')[0].trim();
                facultyName = facultyName.split('Subject Code:')[0].trim();
                facultyName = facultyName.split('Descriptive Title:')[0].trim();
                facultyName = facultyName.split('Count')[0].trim();
                // Remove trailing punctuation consistently
                facultyName = facultyName.replace(/[.;,]+$/g, '').trim();

                // If name is missing on the same line, check the next lines
                if (!facultyName || facultyName.length < 3) {
                    console.log('🔍 Faculty name missing or too short, checking next lines...');
                    for (let lookahead = 1; lookahead <= 10 && (i + lookahead) < lines.length; lookahead++) {
                        const nextLine = lines[i + lookahead].trim();
                        console.log(`🔍 Checking next line ${lookahead}:`, nextLine);
                        
                        // Skip empty lines, URLs, and system messages
                        if (!nextLine || nextLine.length < 3 || 
                            /https?:\/\//i.test(nextLine) || 
                            /Date Printed:/i.test(nextLine) ||
                            /Untitled Document/i.test(nextLine)) {
                            continue;
                        }
                        
                        // Stop if we hit another section marker
                        const stopTokens = ['Time:', 'Room:', 'Type:', 'Count', 'Subject Code:', 'Section:', 'Descriptive Title:'];
                        const isStop = stopTokens.some(t => nextLine.startsWith(t));
                        if (isStop) break;
                        
                        // Look for name patterns
                        if (/^[A-Z][A-Z\-']+,\s*[A-Z .\-']+/i.test(nextLine) || 
                            /^[A-Z .\-']+$/.test(nextLine)) {
                            facultyName = nextLine
                                .split('Time:')[0]
                                .split('Room:')[0]
                                .split('Type:')[0]
                                .split('Section:')[0]
                                .split('Subject Code:')[0]
                                .split('Descriptive Title:')[0]
                                .split('Count')[0]
                                .replace(/[.;,]+$/g, '')
                                .trim();
                            console.log('🔍 Found faculty name on next line:', facultyName);
                            if (facultyName && facultyName.length >= 3) break;
                        }
                    }
                }

                // Only add if we found a valid faculty name
                if (facultyName && facultyName.length >= 3) {
                    const normalizedFacultyName = this.normalizeInstructorKey(facultyName);
                    if (!instructorIds.has(normalizedFacultyName)) {
                        console.log(`✅ Faculty found: ${facultyName} (normalized: ${normalizedFacultyName})`);
                        instructors.push({
                            id: uuidv4(),
                            name: facultyName,
                            full_name: this.parseInstructorName(facultyName),
                            type: 'main'
                        });
                        instructorIds.add(normalizedFacultyName);
                    } else {
                        console.log(`🔄 Duplicate faculty skipped: ${facultyName} (normalized: ${normalizedFacultyName})`);
                    }
                } else {
                    console.log('⚠️ No valid faculty name found for Faculty: line, will use default');
                }
            }

            // Lab instructor - only extract if it looks like a name, not schedule data
            if (line.includes('(LAB)') && !line.startsWith('Time:')) {
                console.log('🔍 Found LAB line:', line);
                const labInstructorMatch = line.match(/\(LAB\)\s*(.+)/);
                if (labInstructorMatch) {
                    const facultyName = labInstructorMatch[1].trim();
                    console.log('🔍 Extracted lab faculty name:', facultyName);
                    
                    // Validate that this looks like a name, not schedule data
                    const isScheduleData = /(\d{1,2}:\d{2}[AP]M|room:|wac-|capacity|@liceo\.edu\.ph)/i.test(facultyName);
                    
                    const normalizedLabFacultyName = this.normalizeInstructorKey(facultyName);
                    if (facultyName && !isScheduleData && !instructorIds.has(normalizedLabFacultyName)) {
                        const isNameLike = /^[A-Z\s,.-]+$/i.test(facultyName) && facultyName.length > 2;
                        
                        if (isNameLike) {
                            console.log(`✅ Valid lab instructor found: ${facultyName} (normalized: ${normalizedLabFacultyName})`);
                            instructors.push({
                                id: uuidv4(),
                                name: facultyName,
                                full_name: this.parseInstructorName(facultyName),
                                type: 'lab'
                            });
                            instructorIds.add(normalizedLabFacultyName);
                        } else {
                            console.log(`❌ Skipping invalid lab instructor (not name-like): ${facultyName}`);
                        }
                    }
                }
            }
        }

        console.log('🎓 Total instructors found in this section:', instructors.length);
        console.log('🎓 Instructor names:', instructors.map(i => i.name));
        
        return instructors;
    }

    /**
     * Build a canonical key for instructor deduplication
     */
    normalizeInstructorKey(name) {
        if (!name) return '';
        // Uppercase, collapse spaces, remove trailing punctuation and duplicate periods, keep comma separator
        let n = name.trim().toUpperCase();
        n = n.replace(/[.;,]+$/g, ''); // Remove trailing punctuation including commas
        n = n.replace(/\s+/g, ' '); // Normalize spaces
        // Normalize spaces around comma
        n = n.replace(/\s*,\s*/g, ', ');
        return n;
    }

    /**
     * Parse instructor name into components
     * @param {string} fullName - Full instructor name
     * @returns {Object} Parsed name components
     */
    parseInstructorName(fullName) {
        // Format: "LASTNAME, FIRSTNAME MIDDLE I."
        const parts = fullName.split(',');
        if (parts.length >= 2) {
            const lastName = parts[0].trim();
            const firstPart = parts[1].trim(); // This includes first name + middle name + initial
            
            // Keep the complete first part as first_name (includes middle name and initial)
            return {
                first_name: firstPart, // "JUN BRIAN P"
                last_name: lastName,   // "TUBONGBANUA"
                middle_name: '',      // Not used anymore
                full_name: fullName
            };
        }

        return {
            first_name: fullName,
            last_name: '',
            middle_name: '',
            full_name: fullName
        };
    }

    /**
     * Extract student list from section
     * @param {Array} lines - Array of text lines
     * @returns {Array} Array of student objects
     */
    extractStudentList(lines) {
        const students = [];
        let isStudentSection = false;
        let headerLineIndex = -1;

        console.log('🎓 Looking for students in', lines.length, 'lines');

        // First, find the student header - look for multiple patterns
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];

            // Look for tabular student header: "Count ID Number Name Gender Course Year"
            if (line.includes('Count') && line.includes('ID Number') && 
                line.includes('Name') && line.includes('Gender') && 
                line.includes('Course') && line.includes('Year')) {
                console.log('📝 Found tabular student header at line', i, ':', line);
                isStudentSection = true;
                headerLineIndex = i;
                break;
            }
            
            // Also look for just "Count" as a header indicator (more flexible)
            if (line.trim() === 'Count' || line.includes('Count')) {
                console.log('📝 Found "Count" header at line', i, ':', line);
                // Check if the next few lines contain the other header fields
                let hasAllFields = false;
                for (let j = i; j < Math.min(i + 5, lines.length); j++) {
                    const checkLine = lines[j];
                    if (checkLine.includes('ID Number') || checkLine.includes('Name') || 
                        checkLine.includes('Gender') || checkLine.includes('Course') || 
                        checkLine.includes('Year')) {
                        hasAllFields = true;
                        break;
                    }
                }
                if (hasAllFields) {
                    isStudentSection = true;
                    headerLineIndex = i;
                    break;
                }
            }
        }

        if (!isStudentSection) {
            console.log('❌ No student header found');
            return students;
        }

        // Now parse student lines starting after the header
        console.log(`🔍 Starting student parsing from line ${headerLineIndex + 1} to ${lines.length}`);
        for (let i = headerLineIndex + 1; i < lines.length; i++) {
            const line = lines[i].trim();
            console.log(`🔍 Processing line ${i}: "${line}" (length: ${line.length})`);

            // Stop only on real headers; skip PDF footers like "Untitled Document" and URLs
            if (this.isFieldLabel(line) || /Date Printed:/i.test(line)) {
                console.log('🛑 Stopping student parsing at line', i, ':', line);
                break;
            }
            if (/Untitled Document/i.test(line) || /^https?:\/\//i.test(line)) {
                console.log('↷ Skipping footer artifact at line', i, ':', line);
                continue;
            }

            // Skip empty lines
            if (!line || line.length < 2) {
                console.log(`↷ Skipping empty/short line ${i}: "${line}"`);
                continue;
            }

            // Check if this line looks like a student ID (7-12 digits)
            if (/^\d{7,12}$/.test(line)) {
                console.log('🔍 Found potential student ID line:', line);
                // Try multi-line parsing without count line
                const multiLineStudent = this.parseMultiLineStudentWithoutCount(lines, i);
                if (multiLineStudent) {
                    console.log('✅ Multi-line student parsed (no count):', multiLineStudent.full_name, multiLineStudent.student_id);
                    students.push(multiLineStudent);
                    i += 4; // Skip the next 4 lines (name, gender, course, year)
                    continue;
                } else {
                    console.log('❌ Multi-line parsing failed for student ID:', line);
                }
            }

            // Handle multi-line student entries starting with count line like "1."
            console.log(`🔍 Checking line ${i} for multi-line pattern: "${line}" (length: ${line.length})`);
            
            // More flexible pattern to catch count lines
            const isCountLine = /^\d+\.?\s*$/.test(line) || /^\d+\.$/.test(line);
            console.log(`🔍 Testing count line pattern: "${line}" -> isCountLine: ${isCountLine}`);
            if (isCountLine) {
                console.log('🔍 Found multi-line student entry at line', i, ':', line);
                const result = this.parseMultiLineStudentFlexible(lines, i);
                if (result && result.student) {
                    students.push(result.student);
                    console.log('✅ Multi-line student parsed:', result.student.full_name, result.student.student_id);
                    i += (result.consumedLines - 1); // advance past consumed lines
                    continue;
                } else {
                    console.log('❌ Multi-line parsing failed, trying tabular fallback');
                }
            } else {
                console.log(`🔍 Line ${i} does not match multi-line pattern: "${line}"`);
            }

            // Try tabular parsing as fallback for single-line compressed format
            console.log('👤 Attempting tabular parsing for line', i, ':', line);
            const student = this.parseTabularStudentLine(line);
            if (student) {
                console.log('✅ Tabular student parsed:', student.full_name, student.student_id);
                students.push(student);
            } else {
                // Try multi-line parsing without count line
                console.log('🔍 Attempting multi-line parsing without count line for line', i, ':', line);
                const multiLineStudent = this.parseMultiLineStudentWithoutCount(lines, i);
                if (multiLineStudent) {
                    console.log('✅ Multi-line student parsed (no count):', multiLineStudent.full_name, multiLineStudent.student_id);
                    students.push(multiLineStudent);
                } else {
                    console.log('❌ Failed to parse student line with any method');
                }
            }
        }

        console.log('🎓 Total students found:', students.length);
        return students;
    }

    /**
     * Parse multi-line student entry
     * @param {Array} lines - Array of all lines
     * @param {number} startIndex - Starting index for this student
     * @returns {Object|null} Student object or null if not valid
     */
    parseMultiLineStudent(lines, startIndex) {
        // Check if we have enough lines for a complete student entry
        if (startIndex + 5 >= lines.length) return null;

        const countLine = lines[startIndex]?.trim();
        const idLine = lines[startIndex + 1]?.trim();
        const nameLine = lines[startIndex + 2]?.trim();
        const genderLine = lines[startIndex + 3]?.trim();
        const courseLine = lines[startIndex + 4]?.trim();
        const yearLine = lines[startIndex + 5]?.trim();

        console.log('🔍 Parsing multi-line student:');
        console.log('  Count:', countLine);
        console.log('  ID:', idLine);
        console.log('  Name:', nameLine);
        console.log('  Gender:', genderLine);
        console.log('  Course:', courseLine);
        console.log('  Year:', yearLine);

        // Validate the entry format
        if (!countLine?.match(/^\d+\.$/)) {
            console.log('❌ Invalid count format:', countLine);
            return null;
        }

        if (!idLine?.match(/^\d{10,11}$/)) {
            console.log('❌ Invalid ID format:', idLine);
            return null;
        }

        if (!nameLine || nameLine.length < 3) {
            console.log('❌ Invalid name:', nameLine);
            return null;
        }

        if (!genderLine?.match(/^[MF]$/)) {
            console.log('❌ Invalid gender:', genderLine);
            return null;
        }

        if (!courseLine || courseLine.length < 2) {
            console.log('❌ Invalid course:', courseLine);
            return null;
        }

        if (!yearLine?.match(/^\d+$/)) {
            console.log('❌ Invalid year:', yearLine);
            return null;
        }

        // Parse the name
        const parsedName = this.parseStudentName(nameLine);

        const student = {
            id: uuidv4(),
            student_id: idLine,
            first_name: parsedName.first_name,
            last_name: parsedName.last_name,
            middle_name: parsedName.middle_name,
            full_name: nameLine,
            gender: genderLine,
            course: courseLine,
            year_level: parseInt(yearLine),
            email: this.generateStudentEmail(idLine, parsedName),
            status: 'Active'
        };

        console.log('👤 Created multi-line student:', student);
        return student;
    }

    /**
     * Parse multi-line student entry without count line
     * @param {Array} lines - All lines
     * @param {number} startIndex - Index of the student ID line
     * @returns {Object|null} Student object or null if not valid
     */
    parseMultiLineStudentWithoutCount(lines, startIndex) {
        // Check if we have enough lines for a complete student entry
        if (startIndex + 4 >= lines.length) return null;

        const idLine = lines[startIndex]?.trim();
        const nameLine = lines[startIndex + 1]?.trim();
        const genderLine = lines[startIndex + 2]?.trim();
        const courseLine = lines[startIndex + 3]?.trim();
        const yearLine = lines[startIndex + 4]?.trim();

        console.log('🔍 Parsing multi-line student without count:');
        console.log('  ID:', idLine);
        console.log('  Name:', nameLine);
        console.log('  Gender:', genderLine);
        console.log('  Course:', courseLine);
        console.log('  Year:', yearLine);

        // Validate the entry format
        if (!idLine?.match(/^\d{7,12}$/)) {
            console.log('❌ Invalid ID format:', idLine);
            return null;
        }

        if (!nameLine || nameLine.length < 3) {
            console.log('❌ Invalid name:', nameLine);
            return null;
        }

        if (!genderLine?.match(/^[MF]$/)) {
            console.log('❌ Invalid gender:', genderLine);
            return null;
        }

        if (!courseLine || courseLine.length < 2) {
            console.log('❌ Invalid course:', courseLine);
            return null;
        }

        if (!yearLine?.match(/^\d+$/)) {
            console.log('❌ Invalid year:', yearLine);
            return null;
        }

        // Parse the name
        const parsedName = this.parseStudentName(nameLine);

        const student = {
            id: uuidv4(),
            student_id: idLine,
            first_name: parsedName.first_name,
            last_name: parsedName.last_name,
            middle_name: parsedName.middle_name,
            full_name: nameLine,
            gender: genderLine,
            course: courseLine,
            year_level: parseInt(yearLine),
            email: this.generateStudentEmail(idLine, parsedName),
            status: 'Active'
        };

        console.log('👤 Created multi-line student (no count):', student);
        return student;
    }

    /**
     * Parse multi-line student entry with optional Year (more lenient)
     * @param {Array} lines - All lines
     * @param {number} startIndex - Index of the count line (e.g., "1.")
     * @returns {{student: Object|null, consumedLines: number}|null}
     */
    parseMultiLineStudentFlexible(lines, startIndex) {
        const countLine = lines[startIndex]?.trim();
        const idLine = lines[startIndex + 1]?.trim();
        const nameLine = lines[startIndex + 2]?.trim();
        const genderLine = lines[startIndex + 3]?.trim();
        const courseLine = lines[startIndex + 4]?.trim();
        const possibleYearLine = lines[startIndex + 5]?.trim();

        console.log('🔍 Parsing flexible multi-line student starting at', startIndex);
        console.log('🔍 Lines:', {
            countLine,
            idLine,
            nameLine,
            genderLine,
            courseLine,
            possibleYearLine
        });

        if (!/^\d+\.$/.test(countLine)) {
            console.log('❌ Invalid count line:', countLine);
            return null;
        }
        if (!/^\d{7,12}$/.test(idLine)) {
            console.log('❌ Invalid ID format:', idLine, '(expected 7-12 digits)');
            return null;
        }
        if (!nameLine || nameLine.length < 3) {
            console.log('❌ Invalid name line:', nameLine);
            return null;
        }
        if (!/^[MF]$/.test(genderLine)) {
            console.log('❌ Invalid gender line:', genderLine);
            return null;
        }
        if (!courseLine || courseLine.length < 2) {
            console.log('❌ Invalid course line:', courseLine);
            return null;
        }

        let yearLevel = null;
        let consumed = 5 + 1; // default assume year exists
        console.log(`🔍 Checking year line: "${possibleYearLine}"`);
        if (/^\d+$/.test(possibleYearLine)) {
            yearLevel = parseInt(possibleYearLine);
            console.log(`✅ Parsed year level: ${yearLevel} for student: ${nameLine}`);
        } else {
            // No year line; treat as missing and set default, reduce consumed lines
            console.log(`⚠️ No valid year found in line: "${possibleYearLine}" for student: ${nameLine}`);
            console.log(`🔍 This line appears to be the next student's count (${possibleYearLine}), so no year field exists`);
            consumed = 5; 
        }

        const parsedName = this.parseStudentName(nameLine);
        
        let finalYearLevel = this.deriveYearLevel(courseLine, yearLevel);
        
        // If still null/undefined for undergraduate, try to infer from context
        if ((finalYearLevel == null || finalYearLevel <= 0) && courseLine && !this.isGraduateCourse(courseLine)) {
            // This is a placeholder - we'll infer from section context during consolidation
            console.log(`🔍 Will attempt to infer year for undergraduate: ${nameLine} (${courseLine})`);
        }
        
        console.log(`🎓 Final year level for ${nameLine}: ${finalYearLevel} (parsed: ${yearLevel}, course: ${courseLine})`);
        
        const student = {
            id: uuidv4(),
            student_id: idLine,
            first_name: parsedName.first_name,
            last_name: parsedName.last_name,
            middle_name: parsedName.middle_name,
            full_name: nameLine,
            gender: genderLine,
            course: courseLine,
            year_level: finalYearLevel,
            email: this.generateStudentEmail(idLine, parsedName),
            status: 'Active'
        };

        return { student, consumedLines: consumed };
    }

    /**
     * Heuristic: decide final year level given course and parsed year
     * - If a course looks like a graduate program (e.g., MSIT, MM-ITM), prefer null when missing
     * - Otherwise default missing to 1
     */
    deriveYearLevel(course, parsedYear) {
        // If we have a valid parsed year, use it
        if (Number.isFinite(parsedYear) && parsedYear > 0) {
            console.log(`✅ Using parsed year level: ${parsedYear} for course: ${course}`);
            return parsedYear;
        }
        
        // For graduate programs, return null when year is missing
        const isGrad = this.isGraduateCourse(course || '');
        if (isGrad) {
            console.log(`🎓 Graduate course detected (${course}), returning null for missing year`);
            return null;
        }
        
        // For undergraduate programs, try to infer year from course name
        const inferredYear = this.inferYearFromCourse(course);
        if (inferredYear) {
            console.log(`🔍 Inferred year level ${inferredYear} from course: ${course}`);
            return inferredYear;
        }
        
        // If we can't infer, default to 1 for undergraduate
        console.log(`⚠️ No year level found for undergraduate course: ${course}, defaulting to 1`);
        return 1;
    }
    
    /**
     * Try to infer year level from course name patterns
     */
    inferYearFromCourse(course) {
        if (!course) return null;
        
        const courseStr = String(course).toUpperCase();
        
        // Look for year patterns in course names
        // Examples: BSIT1, BSIT2, BSIT3, BSIT4, BLIS1, BLIS2, etc.
        const yearMatch = courseStr.match(/(\d+)$/);
        if (yearMatch) {
            const year = parseInt(yearMatch[1]);
            if (year >= 1 && year <= 5) {
                return year;
            }
        }
        
        // Look for section patterns like BSIT1-1, BSIT2-1, etc.
        const sectionMatch = courseStr.match(/(\d+)-(\d+)$/);
        if (sectionMatch) {
            const year = parseInt(sectionMatch[1]);
            if (year >= 1 && year <= 5) {
                return year;
            }
        }
        
        return null;
    }

    /**
     * Simple check for graduate program keywords
     */
    isGraduateCourse(course) {
        const text = String(course).toUpperCase();
        // Enhanced pattern to catch more graduate program variations
        return /(MSIT|MM-ITM|MBA|MA|MSC|MMITM|MM-IT|GRADUATE|POSTGRAD)/.test(text);
    }

    /**
     * Parse tabular student line
     * @param {string} line - Student line text in tabular format
     * @returns {Object|null} Student object or null if not valid
     */
    parseTabularStudentLine(line) {
        // Skip empty lines and non-student lines
        if (!line || line.length < 10) return null;
        
        // Normalize hidden characters that may break regex
        let normalized = line
            .replace(/[\u200B-\u200D\uFEFF]/g, '') // zero-width spaces
            .replace(/\s+$/g, ''); // trailing whitespace

        console.log('🔍 Attempting to parse tabular line:', normalized);
        console.log('🔍 Line length:', normalized.length);
        console.log('🔍 Line characters:', normalized.split('').map(c => c.charCodeAt(0)).slice(0, 20));
        
        // Based on your example format:
        // "1. 20235390104 ABAS, GAIL ISABELLE A. F BSIT-CISCO 1"
        // "2. 20150971422 ABRAGAN, ANGELIE B. F BEED 1"
        
        // Try different patterns to handle various formatting
        const tabularPatterns = [
            // Compressed format WITH explicit gender and NO year
            // Require gender to come after a period (end of middle initial) to avoid grabbing the
            // first letter of the given name (e.g., "MARJUNE") as gender
            // e.g. "1.20257024703ABAO, RUD ALBERT P.FMSIT"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?\.)\s*([MF])([A-Z0-9\s\-().\/]+)$/,

            // Compressed format WITH explicit gender and WITH year at the end
            // e.g. "1.20220185385ALIÑAB, JESECA HEART S.FBLIS4"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?\.)\s*([MF])([A-Z0-9\s\-().\/]+?)(\d+)$/,

            // Compressed format with NO explicit gender, name ends with '.' then course
            // e.g. "1.20237527612ABREGANA, MARJUNE O.MMSIT"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?\.)\s*([A-Z0-9\s\-().\/]+)$/,

            // Standard format with spaces: "1. 20235390104 ABAS, GAIL ISABELLE A. F BSIT-CISCO 1"
            /^(\d+)\.\s+(\d{7,12})\s+(.+?)\s+([MF])\s+(.+?)\s+(\d+)\s*$/,
            
            // Format with minimal spaces including year: "1.20235390104ABAS, GAIL ISABELLE A.FBSIT-CISCO1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?\.)\s*([MF])([A-Z0-9\s\-().\/]+?)(\d+)$/,
            
            // Format with multiple spaces between fields
            /^(\d+)\.\s*(\d{7,12})\s+(.+?)\s+([MF])\s+(.+?)\s+(\d+)\s*$/,
            
            // More flexible format that handles various spacing
            /^(\d+)\.\s*(\d{7,12})\s+(.+?)\s+([MF])\s+(.+?)\s+(\d+)\s*$/,
            
            // Ultra-flexible pattern for complex course names with parentheses and spaces
            // e.g. "2.20226078455ADIONG, SHAID MAHDI B.MMM-ITM (Plan B)"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?\.)\s*([MF])(.+)$/,
            
            // Pattern for names with commas and complex formatting
            // e.g. "1.20255237912ADERO, JAMIMA CHLOE R.FBSPT1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?\.)\s*([MF])([A-Z0-9\s\-().\/]+)$/,
            
            // More specific pattern for names with commas that might not end with period
            // e.g. "1.20255237912ADERO, JAMIMA CHLOE R.FBSPT1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?)\s*([MF])([A-Z0-9\s\-().\/]+)$/,
            
            // Pattern specifically for names with commas where gender comes after name
            // e.g. "1.20255237912ADERO, JAMIMA CHLOE R.FBSPT1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?)\s*([MF])([A-Z0-9\s\-().\/]+)$/,
            
            // Pattern for cases where gender might be missing or incorrectly parsed
            // e.g. "1.20255237912ADERO, JAMIMA CHLOE R.FBSPT1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?)([A-Z0-9\s\-().\/]+)$/,
            
            // Very specific pattern for the problematic case where name has comma and no clear gender
            // e.g. "1.20255237912ADERO, JAMIMA CHLOE R.FBSPT1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?)([A-Z0-9\s\-().\/]+)$/,
            
            // Pattern specifically for names with commas where gender might be missing
            // e.g. "1.20255237912ADERO, JAMIMA CHLOE R.FBSPT1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?)([A-Z0-9\s\-().\/]+)$/,
            
            // Pattern for the specific problematic case where name has comma and gender is missing
            // e.g. "1.20255237912ADERO, JAMIMA CHLOE R.FBSPT1"
            /^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?)([A-Z0-9\s\-().\/]+)$/,
            
            // Even more flexible pattern for any remaining edge cases
            // e.g. "1.20237527612ABREGANA, MARJUNE O.MMSIT" or any other format
            /^(\d+)\.(\d{7,12})(.+?)([MF])(.+)$/,
            
            // Fallback pattern for any line that starts with number and has student ID
            /^(\d+)\.(\d{7,12})(.+)$/
        ];
        
        // Try each pattern until one matches
        for (let i = 0; i < tabularPatterns.length; i++) {
            const pattern = tabularPatterns[i];
            const match = normalized.match(pattern);
            
            if (match) {
                console.log(`✅ Tabular pattern ${i + 1} matched:`, match);
                
                // Determine capture layout depending on which pattern matched
                let count, studentId, fullName, gender, course, year;
                if (i === 0) {
                    [, count, studentId, fullName, gender, course] = match;
                } else if (i === 1) {
                    [, count, studentId, fullName, gender, course, year] = match;
                } else if (i === 2) {
                    // No gender version
                    [, count, studentId, fullName, course] = match;
                    gender = null;
                } else if (i === tabularPatterns.length - 4) {
                    // Ultra-flexible pattern for complex course names
                    [, count, studentId, fullName, gender, course] = match;
                    year = null; // No year in this pattern
                } else if (i === tabularPatterns.length - 3) {
                    // Pattern for names with commas and complex formatting
                    [, count, studentId, fullName, gender, course] = match;
                    year = null; // No year in this pattern
                } else if (i === tabularPatterns.length - 2) {
                    // More specific pattern for names with commas that might not end with period
                    [, count, studentId, fullName, gender, course] = match;
                    year = null; // No year in this pattern
                } else if (i === tabularPatterns.length - 1) {
                    // Pattern for cases where gender might be missing or incorrectly parsed
                    [, count, studentId, fullName, course] = match;
                    gender = null; // No gender in this pattern
                    year = null; // No year in this pattern
                } else if (i === tabularPatterns.length) {
                    // Fallback pattern - try to extract what we can
                    [, count, studentId, rest] = match;
                    // Try to parse the rest as best we can
                    const parts = rest.trim().split(/\s+/);
                    if (parts.length >= 2) {
                        fullName = parts.slice(0, -1).join(' ');
                        gender = parts[parts.length - 1];
                        course = 'Unknown';
                    } else {
                        fullName = rest;
                        gender = 'M'; // Default
                        course = 'Unknown';
                    }
                    year = null;
                } else {
                    [, count, studentId, fullName, gender, course, year] = match;
                }
                
                // Clean up the name (remove extra spaces, handle formatting)
                const cleanName = fullName.trim().replace(/\s+/g, ' ');
                const parsedName = this.parseStudentName(cleanName);

                // Validate the data
                if (!studentId || !fullName || !gender || !course) {
                    console.log('❌ Missing required fields:', { studentId, fullName, gender, course });
                    continue;
                }

                // Validate student ID format (accept 7-12 digits to cover edge IDs)
                if (!/^\d{7,12}$/.test(studentId)) {
                    console.log('❌ Invalid student ID format:', studentId);
                    continue;
                }

                // Validate gender
                if (gender && !['M', 'F'].includes(gender)) {
                    console.log('❌ Invalid gender:', gender);
                    continue;
                }

                // Default year when missing (e.g., graduate programs not using year)
                const yearLevel = parseInt(year, 10);
                let finalYear = Number.isFinite(yearLevel) ? yearLevel : this.deriveYearLevel(course, null);
                
                // If still null/undefined for undergraduate, will try to infer from context during consolidation
                if ((finalYear == null || finalYear <= 0) && course && !this.isGraduateCourse(course)) {
                    console.log(`🔍 Will attempt to infer year for undergraduate: ${cleanName} (${course})`);
                }

                const student = {
                    id: uuidv4(),
                    student_id: studentId,
                    first_name: parsedName.first_name,
                    last_name: parsedName.last_name,
                    middle_name: parsedName.middle_name,
                    full_name: cleanName,
                    gender: gender || null,
                    course: course.trim(),
                    year_level: finalYear,
                    email: this.generateStudentEmail(studentId, parsedName),
                    status: 'Active'
                };
                
                // Guard against bad name extraction like "LASTNAME," only
                if (!student.first_name || student.full_name.endsWith(',')) {
                    console.log('❌ Detected incomplete name parse, continue to next pattern');
                    continue;
                }

                console.log('👤 Created tabular student:', student);
                return student;
            }
        }

        // Ultra-fallback: very lenient compressed line matcher
        // Format: COUNT.IDNAME,GIVEN M./FCOURSE[optional YEAR]
        // Use a more specific pattern to avoid consuming course letters as gender
        const ultra = normalized.match(/^(\d+)\.(\d{7,12})([A-ZÀ-ÿ\s,.'-]+?\.)\s*([MF])([A-Z0-9\s\-().\/]+)$/);
        if (ultra) {
            let [, count, studentId, fullName, gender, courseAndMaybeYear] = ultra;
            fullName = fullName.trim();
            let course = courseAndMaybeYear.trim();
            let year = null;
            const yearMatch = course.match(/(\d+)\s*$/);
            if (yearMatch) {
                year = yearMatch[1];
                course = course.slice(0, course.length - yearMatch[0].length).trim();
            }
            const cleanName = fullName.replace(/\s+/g, ' ');
            const parsedName = this.parseStudentName(cleanName);
            if (!/^\d{7,12}$/.test(studentId)) return null;
            if (!['M', 'F'].includes(gender)) return null;

            const yearLevel = parseInt(year, 10);
            let finalYear = Number.isFinite(yearLevel) ? yearLevel : this.deriveYearLevel(course, null);
            
            // If still null/undefined for undergraduate, will try to infer from context during consolidation
            if ((finalYear == null || finalYear <= 0) && course && !this.isGraduateCourse(course)) {
                console.log(`🔍 Will attempt to infer year for undergraduate: ${cleanName} (${course})`);
            }

            const student = {
                id: uuidv4(),
                student_id: studentId,
                first_name: parsedName.first_name,
                last_name: parsedName.last_name,
                middle_name: parsedName.middle_name,
                full_name: cleanName,
                gender: gender,
                course: course,
                year_level: finalYear,
                email: this.generateStudentEmail(studentId, parsedName),
                status: 'Active'
            };
            console.log('👤 Created tabular student (fallback):', student);
            return student;
        }
        
        console.log('❌ No tabular pattern matched for line:', line);
        return null;
    }

    /**
     * Parse a single student line (legacy method, kept for compatibility)
     * @param {string} line - Student line text
     * @returns {Object|null} Student object or null if not a valid student line
     */
    parseStudentLine(line) {
        // This method is now used as fallback for single-line formats
        console.log('🔍 Using legacy single-line parsing for:', line);
        return null; // Disabled for now since we're using tabular format
    }

    /**
     * Parse student name into components
     * @param {string} fullName - Full student name
     * @returns {Object} Parsed name components
     */
    parseStudentName(fullName) {
        // Format: "LASTNAME, FIRSTNAME MIDDLE I." or "LASTNAME,, FIRSTNAME MIDDLE I." (with double comma)
        const parts = fullName.split(',');
        if (parts.length >= 2) {
            const lastName = parts[0].trim();
            
            // Handle double comma case: "RESOMADERO,, JAMIMA CHLOE R."
            // parts[1] will be empty, so we need to get the first non-empty part after the last name
            let firstPart = '';
            for (let i = 1; i < parts.length; i++) {
                const trimmed = parts[i].trim();
                if (trimmed) {
                    firstPart = trimmed;
                    break;
                }
            }
            
            // If we found a non-empty first part, use it
            if (firstPart) {
                return {
                    first_name: firstPart, // "JAMIMA CHLOE R"
                    last_name: lastName,   // "RESOMADERO"
                    middle_name: ''        // Not used anymore
                };
            }
        }

        // If no comma or no valid first name found, assume it's all first name
        return {
            first_name: fullName,
            last_name: '',
            middle_name: ''
        };
    }

    /**
     * Generate student email based on ID and name
     * @param {string} studentId - Student ID
     * @param {Object} parsedName - Parsed name object
     * @returns {string} Generated email (now returns null to avoid fake emails)
     */
    generateStudentEmail(studentId, parsedName) {
        // Return null instead of generating fake emails
        return null;
    }

    /**
     * Extract building from room string
     * @param {string} roomStr - Room string (e.g., "WAC-201")
     * @returns {string} Building name
     */
    extractBuilding(roomStr) {
        if (!roomStr || roomStr === 'TBA') return '';
        
        const buildingMatch = roomStr.match(/^([A-Z]+)/);
        return buildingMatch ? buildingMatch[1] : '';
    }

    /**
     * Generate room name from room number
     * @param {string} roomNumber - Room number
     * @returns {string} Generated room name
     */
    generateRoomName(roomNumber) {
        if (!roomNumber || roomNumber === 'TBA') return 'To Be Announced';
        
        // Return the room number as-is to preserve the original format from PDF
        // This ensures "WAC-201" stays as "WAC-201" instead of "West Academic Center Room 201"
        return roomNumber;
    }

    /**
     * Consolidate parsed section data into main data structure
     * @param {Object} parsedData - Main parsed data object
     * @param {Object} sectionData - Section data to consolidate
     */
    consolidateData(parsedData, sectionData) {
        // Add subject
        if (sectionData.subject) {
            const baseCode = (sectionData.subject.code || '').toString().replace(/\s+/g, '').toUpperCase();
            // Make subject code unique per section to avoid database constraint conflicts
            const sectionSuffix = sectionData.subject.section ? `-${sectionData.subject.section.replace(/\s+/g, '')}` : '';
            const uniqueCode = `${baseCode}${sectionSuffix}`;
            
            // Derive instructor fields from first instructor, if any
            let subjectInstructorId = null;
            let subjectInstructorName = null;
            let subjectInstructorEmail = null;
            if (sectionData.instructors && sectionData.instructors.length > 0) {
                const firstInstructor = sectionData.instructors[0];
                subjectInstructorId = firstInstructor.id;
                const nameObj = firstInstructor.full_name || {};
                subjectInstructorName = [nameObj.first_name, nameObj.last_name].filter(Boolean).join(' ').trim() || firstInstructor.name;
                subjectInstructorEmail = this.generateInstructorEmail(nameObj);
            }
            parsedData.subjects.push({
                ...sectionData.subject,
                code: uniqueCode, // Use section-aware unique code
                original_code: baseCode, // Store original code for reference
                academic_year: parsedData.metadata.academic_year,
                semester: parsedData.metadata.semester,
                year: parsedData.metadata.year,
                instructor_id: subjectInstructorId,
                instructor_name: subjectInstructorName,
                instructor_email: subjectInstructorEmail,
                description: `${sectionData.subject.name} - ${sectionData.subject.section}`
            });
        }

        // Add instructors
        for (const instructor of sectionData.instructors) {
            // Use normalized key to prevent duplicates from punctuation variations
            const normalizedKey = this.normalizeInstructorKey(instructor.name);
            if (!parsedData.instructors.has(normalizedKey)) {
                console.log(`➕ Adding instructor to global list: ${instructor.name} (normalized: ${normalizedKey})`);
                parsedData.instructors.set(normalizedKey, {
                    id: instructor.id,
                    name: instructor.name, // Keep the original name (cleaned)
                    user_type: 'instructor',
                    employee_id: this.generateEmployeeId(),
                    ...instructor.full_name,
                    email: this.generateInstructorEmail(instructor.full_name),
                    status: 'Active',
                    department: this.extractDepartmentFromSubject(sectionData.subject?.code)
                });
            } else {
                console.log(`🔄 Instructor already in global list: ${instructor.name} (normalized: ${normalizedKey})`);
            }
        }

        // Add students (merge year levels intelligently across sections)
        console.log(`📚 Processing ${sectionData.students.length} students from section: ${sectionData.subject?.code} (${sectionData.subject?.name})`);
        for (const student of sectionData.students) {
            console.log(`👤 Processing student: ${student.full_name} (${student.student_id}) - Year: ${student.year_level} from section: ${sectionData.subject?.code}`);
            // Infer missing undergraduate year level from section/subject code when possible
            if ((student.year_level == null || student.year_level <= 0) && student.course && !this.isGraduateCourse(student.course)) {
                const inferred = this.inferYearFromContext(sectionData.subject);
                if (Number.isFinite(inferred)) {
                    console.log(`  🧠 Inferred year ${inferred} from context for ${student.full_name}`);
                    student.year_level = inferred;
                }
            }
            const existing = parsedData.students.get(student.student_id);
            if (!existing) {
                console.log(`  ➕ New student, adding to global list`);
                parsedData.students.set(student.student_id, {
                    ...student,
                    user_type: 'student',
                    department: this.extractDepartmentFromCourse(student.course)
                });
            } else {
                // Prefer defined year over undefined/null, and prefer higher numeric year
                const existingYear = typeof existing.year_level === 'number' ? existing.year_level : null;
                const incomingYear = typeof student.year_level === 'number' ? student.year_level : null;

                console.log(`🔄 Consolidating student ${student.student_id} (${student.full_name}):`);
                console.log(`  Existing year: ${existingYear}, Incoming year: ${incomingYear}`);

                let mergedYear = existingYear;
                // Smart consolidation: prefer higher year levels, but only if both are valid
                if (incomingYear != null && incomingYear > 0) {
                    if (existingYear == null || existingYear <= 0) {
                        // No existing year or invalid existing year, use incoming
                        mergedYear = incomingYear;
                        console.log(`  ✅ Using incoming year: ${incomingYear} (no valid existing year)`);
                    } else if (incomingYear > existingYear) {
                        // Incoming year is higher, use it
                        mergedYear = incomingYear;
                        console.log(`  ✅ Using incoming year: ${incomingYear} (higher than existing: ${existingYear})`);
                    } else {
                        // Keep existing year (it's higher or equal)
                        console.log(`  ✅ Keeping existing year: ${existingYear} (higher than incoming: ${incomingYear})`);
                    }
                } else {
                    console.log(`  ✅ Keeping existing year: ${existingYear} (incoming invalid: ${incomingYear})`);
                }

                // Merge keeping other best-known fields
                parsedData.students.set(student.student_id, {
                    ...existing,
                    // keep first/last names from latest parsed (they should be consistent)
                    first_name: student.first_name || existing.first_name,
                    last_name: student.last_name || existing.last_name,
                    middle_name: student.middle_name ?? existing.middle_name,
                    full_name: student.full_name || existing.full_name,
                    gender: student.gender || existing.gender,
                    course: student.course || existing.course,
                    year_level: mergedYear,
                    // Prefer department derived from course if missing
                    department: existing.department || this.extractDepartmentFromCourse(student.course),
                    status: existing.status || student.status,
                    email: existing.email ?? student.email,
                    user_type: 'student'
                });
            }
        }

        // Add rooms (including TBA rooms)
        for (const schedule of sectionData.schedules) {
            if (schedule.room_number) {
                if (!parsedData.rooms.has(schedule.room_number)) {
                    parsedData.rooms.set(schedule.room_number, {
                        id: uuidv4(),
                        room_number: schedule.room_number,
                        room_name: schedule.room_name || this.generateRoomName(schedule.room_number),
                        building: schedule.building || this.extractBuilding(schedule.room_number),
                        capacity: this.estimateRoomCapacity(sectionData.students.length),
                        status: 'Available',
                        room_type: schedule.is_lab ? 'laboratory' : 'classroom'
                    });
                }
            }
        }

        // Add schedules
        for (const schedule of sectionData.schedules) {
            const baseCode = (schedule.subject_code || '').toString().replace(/\s+/g, '').toUpperCase();
            const sectionSuffix = sectionData.subject?.section ? `-${sectionData.subject.section.replace(/\s+/g, '')}` : '';
            const uniqueCode = `${baseCode}${sectionSuffix}`;
            
            parsedData.schedules.push({
                ...schedule,
                subject_code: uniqueCode, // Use section-aware unique code
                academic_year: parsedData.metadata.academic_year,
                semester: parsedData.metadata.semester,
                description: sectionData.subject ? `${sectionData.subject.name} - ${sectionData.subject.section}` : null,
                room_id: schedule.room_number 
                    ? parsedData.rooms.get(schedule.room_number)?.id 
                    : null
            });
        }

        // Add enrollments
        for (const student of sectionData.students) {
            if (sectionData.subject) {
                const baseCode = (sectionData.subject.code || '').toString().replace(/\s+/g, '').toUpperCase();
                const sectionSuffix = sectionData.subject.section ? `-${sectionData.subject.section.replace(/\s+/g, '')}` : '';
                const uniqueCode = `${baseCode}${sectionSuffix}`;
                
                parsedData.enrollments.push({
                    id: uuidv4(),
                    student_id: student.student_id,
                    subject_id: sectionData.subject.id,
                    subject_code: uniqueCode, // Use section-aware unique code
                    description: `${sectionData.subject.name} - ${sectionData.subject.section}`,
                    academic_year: parsedData.metadata.academic_year,
                    semester: parsedData.metadata.semester,
                    status: 'enrolled',
                    enrollment_date: new Date()
                });
            }
        }
    }

    /**
     * Generate employee ID for instructor
     * @returns {string} Generated employee ID
     */
    generateEmployeeId() {
        return 'EMP' + Date.now().toString().slice(-6);
    }

    /**
     * Generate instructor email
     * @param {Object} nameData - Name data object
     * @returns {string} Generated email (now returns null to avoid fake emails)
     */
    generateInstructorEmail(nameData) {
        // Return null instead of generating fake emails
        return null;
    }

    /**
     * Extract department from subject code
     * @param {string} subjectCode - Subject code
     * @returns {string} Department name
     */
    extractDepartmentFromSubject(subjectCode) {
        if (!subjectCode) return 'General';
        
        const departmentMappings = {
            'BSIT': 'Information Technology',
            'BLIS': 'Library and Information Science',
            'BSCS': 'Computer Science',
            'BSED': 'Education',
            'BSBA': 'Business Administration',
            'BEED': 'Elementary Education',
            'BSAM': 'Applied Mathematics',
            'BSEE': 'Electrical Engineering',
            'BSNED': 'Special Needs Education',
            'BSHM': 'Hospitality Management',
            'BSMA': 'Management Accounting',
            'BSCE': 'Civil Engineering',
            'BSRT': 'Respiratory Therapy',
            'BSN': 'Nursing',
            'BSREM': 'Real Estate Management',
            'BSMED': 'Medical Laboratory Science',
            'B-PED': 'Physical Education',
            'BSIE': 'Industrial Engineering'
        };

        const prefix = subjectCode.split('-')[0];
        return departmentMappings[prefix] || 'General';
    }

    /**
     * Extract department from course name
     * @param {string} course - Course name
     * @returns {string} Department name
     */
    extractDepartmentFromCourse(course) {
        const departmentMappings = {
            'BSIT': 'Information Technology',
            'BLIS': 'Library and Information Science',
            'BSCS': 'Computer Science',
            'BSED': 'Education',
            'BSBA': 'Business Administration',
            'BEED': 'Elementary Education',
            'BSAM': 'Applied Mathematics',
            'BSEE': 'Electrical Engineering',
            'BSNED': 'Special Needs Education',
            'BSHM': 'Hospitality Management',
            'BSMA': 'Management Accounting',
            'BSCE': 'Civil Engineering',
            'BSRT': 'Respiratory Therapy',
            'BSN': 'Nursing',
            'BSREM': 'Real Estate Management',
            'BSMED': 'Medical Laboratory Science',
            'B-PED': 'Physical Education',
            'BSIE': 'Industrial Engineering'
        };

        for (const [key, value] of Object.entries(departmentMappings)) {
            if (course.includes(key)) {
                return value;
            }
        }

        return 'General';
    }

    /**
     * Estimate room capacity based on student count
     * @param {number} studentCount - Number of students
     * @returns {number} Estimated capacity
     */
    estimateRoomCapacity(studentCount) {
        // Add 20% buffer to student count, minimum 20, round to nearest 5
        const capacity = Math.max(20, Math.ceil(studentCount * 1.2));
        return Math.ceil(capacity / 5) * 5;
    }

    /**
     * Generate statistics from parsed data
     * @param {Object} parsedData - Parsed data object
     * @returns {Object} Statistics object
     */
    generateStatistics(parsedData) {
        return {
            total_subjects: parsedData.subjects.length,
            total_students: parsedData.students.size,
            total_instructors: parsedData.instructors.size,
            total_rooms: parsedData.rooms.size,
            total_schedules: parsedData.schedules.length,
            total_enrollments: parsedData.enrollments.length,
            academic_year: parsedData.metadata.academic_year,
            semester: parsedData.metadata.semester,
            import_date: new Date()
        };
    }
}

module.exports = PDFParserService;
