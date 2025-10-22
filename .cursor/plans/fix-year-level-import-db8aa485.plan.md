<!-- db8aa485-11de-4e28-b0dd-dab728660f73 f9e735e0-9f8f-472a-bcf7-2a123e3da071 -->
# Fix Multi-Line Student Name Parsing with Double Commas

## Problem
The student "RESOMADERO,, JAMIMA CHLOE R." (ID: 20255237912) in section E-EL-ITE-BSPT1A is being incorrectly parsed. The parser is failing to recognize the multi-line format for this student entry, likely due to the double comma in the name.

## Root Cause
The multi-line parser in `backend/services/pdfParserService.js` is not correctly handling student entries when the PDF text extraction combines multiple lines into a compressed format. The function `parseMultiLineStudentFlexible` expects lines to be properly separated, but the PDF parser might be receiving compressed text.

## Solution
1. **Investigate PDF text extraction** (lines 84-98 in `pdfParserService.js`)
   - Check how `pdf-parse` is extracting the text from the PDF
   - Verify if section texts are being properly split into lines
   - Add debugging to show how many lines are in each section

2. **Fix multi-line student parsing** (lines 1196-1250 in `pdfParserService.js`)
   - Update `parseMultiLineStudentFlexible` to handle names with double commas
   - Ensure the function validates student ID format correctly
   - Add better error handling for malformed name entries

3. **Improve student parsing loop** (lines 954-1037 in `pdfParserService.js`)
   - Ensure the parser correctly identifies count lines like "37."
   - Verify that multi-line parsing is attempted before tabular parsing
   - Add logging to show which parsing method is being used

## Files to Modify
- `backend/services/pdfParserService.js` - Update multi-line parsing logic to handle double commas and ensure proper line detection

## Testing
After changes, re-upload the PDF and verify:
- Student "RESOMADERO, JAMIMA CHLOE R." shows correct name (not "RESO")
- Course shows "BSPT" (not mixed with name)
- Year level shows "1" (not "Graduate Program")
- All other students in the section are still parsed correctly

### To-dos

- [ ] Update pdfParserService.js to properly handle missing year levels and improve graduate course detection
- [ ] Update UnifiedManagement.jsx to display 'Graduate Program' instead of 'Year null' for students without year levels
- [ ] Test the import with the classlist file to verify year levels are parsed and displayed correctly