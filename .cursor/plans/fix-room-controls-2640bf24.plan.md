<!-- 2640bf24-5b8b-4c69-b330-a2c6adeba8c7 fdc7a584-2463-4597-a92f-51be4a4277aa -->
# Fix Room Selection Layout

## Problem

The Room dropdown is extremely wide and the layout looks messy because:

1. The Room dropdown (`cmbRoom`) and Change button (`btnChangeRoom`) are both added to cell (3,1), causing overlap
2. The TableLayoutPanel only has 4 columns but needs 5 to properly accommodate all controls
3. Column 3 has `SizeType.Percent, 100F` which makes the Room dropdown take all remaining space

## Solution

Restructure the TableLayoutPanel to use 5 columns with proper sizing:

### Updated Column Layout

- **Column 0**: Location label (70px fixed)
- **Column 1**: Location dropdown (100px fixed)
- **Column 2**: Room label (50px fixed)
- **Column 3**: Room dropdown (250px fixed or percent with max)
- **Column 4**: Change button (100px fixed)

### Changes Required

**Fingerprint Attendance Tab** (`CreateFingerprintAttendanceTab` method, ~lines 1688-1749):

1. Change `ColumnCount` from 4 to 5
2. Update column styles to 5 columns with balanced widths
3. Update `lblCurrentRoom` span from 4 to 5 columns
4. Keep control positions for Location label (0,1) and dropdown (1,1)
5. Keep Room label at (2,1) and dropdown at (3,1)
6. Move Change button from (3,1) to **(4,1)** - this is the key fix

**RFID Attendance Tab** (`CreateRfidAttendanceTab` method, ~lines 1614-1675):

- Apply the same 5-column structure and positioning

## Files to Modify

- `FutronicAttendanceSystem/MainForm.cs` - Fix both tab layouts