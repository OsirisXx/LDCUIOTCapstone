# Quick Fix: Archive Nov 7, 2025 Logs

Since you're seeing unknown logs from Nov 7, 2025 that weren't archived, here are two ways to fix it:

## Option 1: Use the New Archive Endpoint (Recommended)

The archive endpoint now has improved logic that:
- Gets date ranges from both attendance records AND sessions
- Expands the date range by 7 days on each side
- Better handles cases where ACCESSLOGS exist without attendance records

**To fix your Nov 7 logs:**

1. **Re-archive the academic year/semester that includes Nov 7:**
   - Go to `/archive` page
   - Click "Archive Attendance"
   - Select the academic year and semester that includes Nov 7, 2025
   - The improved logic should now catch those logs

2. **OR use the new manual archive endpoint:**
   - You can now manually archive ACCESSLOGS by date range using:
   ```javascript
   // Example API call:
   POST /api/archive/access-logs
   {
     "start_date": "2025-11-07",
     "end_date": "2025-11-07",
     "reason": "Archive Nov 7 unknown logs"
   }
   ```

## Option 2: Direct SQL Query (Quick Fix)

If you want to quickly archive those Nov 7 logs right now, run this SQL:

```sql
UPDATE ACCESSLOGS 
SET ARCHIVED_AT = NOW(), 
    ARCHIVED_BY = (SELECT USERID FROM USERS WHERE USERTYPE = 'admin' LIMIT 1),
    ARCHIVE_REASON = 'Manual archive - Nov 7, 2025 logs'
WHERE ACCESSTYPE = 'attendance_scan' 
  AND ARCHIVED_AT IS NULL
  AND DATE(TIMESTAMP) = '2025-11-07';
```

## What Was Fixed

1. **Improved Archive Logic:**
   - Now gets date ranges from both ATTENDANCERECORDS and SESSIONS
   - Expands date range by 7 days on each side to catch edge cases
   - Better handles ACCESSLOGS that don't have corresponding attendance records

2. **New Manual Archive Endpoint:**
   - `/api/archive/access-logs` - Archive ACCESSLOGS by date range
   - Useful for archiving specific dates manually

3. **Better Logging:**
   - Console logs show how many ACCESSLOGS were archived
   - Shows the date range used for archiving

## Next Steps

1. Archive the Nov 7 logs using one of the methods above
2. Verify the logs no longer appear in `/attendance-logs`
3. Check that archived logs appear in `/archive` (if we add ACCESSLOGS to the archive view)

## Note

The improved archive logic will work better going forward, but existing unarchived logs need to be archived manually or by re-archiving the academic year/semester.





