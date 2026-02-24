# Archive Attendance Fix - Migration Required

## Problem
Unknown logs (ACCESSLOGS entries) were still showing in the attendance logs page even after archiving attendance records.

## Solution
The fix adds archive support to the ACCESSLOGS table so that denied access logs and unknown scans can be properly archived along with attendance records.

## Steps to Fix

### 1. Run the Migration
You **must** run the migration script to add the ARCHIVED_AT column to the ACCESSLOGS table:

```sql
-- Execute this file in MySQL:
database/migration_add_archived_to_accesslogs.sql
```

Or run it directly in MySQL Workbench/Command Line:
```bash
mysql -u your_username -p iot_attendance < database/migration_add_archived_to_accesslogs.sql
```

### 2. Archive Attendance Records Again
After running the migration, you need to **re-archive** your attendance records:

1. Go to the Archive page (`http://localhost:3000/archive`)
2. Click "Archive Attendance"
3. Select the academic year and semester you want to archive
4. The system will now also archive related ACCESSLOGS (unknown scans and denied access logs) within the same date range

### 3. Verify
- Check that unknown logs no longer appear in `/attendance-logs`
- Check that archived logs appear in `/archive` under "Archived Attendance"

## What Changed

1. **Migration Script** (`database/migration_add_archived_to_accesslogs.sql`):
   - Adds `ARCHIVED_AT`, `ARCHIVED_BY`, and `ARCHIVE_REASON` columns to ACCESSLOGS table
   - Creates an index on `ARCHIVED_AT` for better performance

2. **Archive Endpoint** (`backend/routes/archive.js`):
   - Now archives ACCESSLOGS with `ACCESSTYPE = 'attendance_scan'` that fall within the date range of archived attendance records
   - Returns count of both attendance records and access logs archived

3. **Attendance Logs Query** (`backend/routes/logs.js`):
   - Filters out archived ACCESSLOGS records (only if migration has been run)
   - Handles gracefully if migration hasn't been run yet

4. **Unarchive Endpoint** (`backend/routes/archive.js`):
   - Also unarchives related ACCESSLOGS when unarchiving attendance records

## Important Notes

- **The migration must be run first** before the archive functionality will work correctly
- **Existing logs will not be automatically archived** - you need to re-archive attendance records after running the migration
- The system will gracefully handle cases where the migration hasn't been run (logs will still show, but archiving won't fail)

## Troubleshooting

If you still see unknown logs after running the migration and archiving:

1. Verify the migration ran successfully:
   ```sql
   SELECT COLUMN_NAME 
   FROM INFORMATION_SCHEMA.COLUMNS 
   WHERE TABLE_SCHEMA = 'iot_attendance' 
   AND TABLE_NAME = 'ACCESSLOGS' 
   AND COLUMN_NAME = 'ARCHIVED_AT';
   ```
   This should return a row if the migration was successful.

2. Check if the logs you're seeing are from dates that were actually archived:
   - The archive operation only archives ACCESSLOGS within the date range of archived attendance records
   - Logs from dates outside the archived range will still show

3. Check the archive operation response:
   - Look for `accessLogsArchived` count in the response
   - If it's 0, the ACCESSLOGS might not have been archived (check date ranges)









