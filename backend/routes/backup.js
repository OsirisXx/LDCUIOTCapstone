const express = require('express');
const router = express.Router();
const path = require('path');
const fs = require('fs');
const { authenticateToken } = require('../middleware/auth');
const backupService = require('../services/backupService');

// Get backup statistics
router.get('/stats', authenticateToken, async (req, res) => {
  try {
    // Check if user is admin
    if (req.user.role !== 'admin') {
      return res.status(403).json({ message: 'Admin access required' });
    }

    const stats = await backupService.getBackupStats();
    res.json(stats);
  } catch (error) {
    console.error('Error fetching backup stats:', error);
    res.status(500).json({ message: 'Failed to fetch backup statistics' });
  }
});

// Create backup
router.post('/create', authenticateToken, async (req, res) => {
  try {
    // Check if user is admin
    if (req.user.role !== 'admin') {
      return res.status(403).json({ message: 'Admin access required' });
    }

    const {
      includeDatabase = true,
      dbFormat = 'both',
      includeFiles = true,
      includeConfig = true
    } = req.body;

    // Validate dbFormat
    if (!['sql', 'json', 'both'].includes(dbFormat)) {
      return res.status(400).json({ message: 'Invalid dbFormat. Must be sql, json, or both' });
    }

    // Create backup
    console.log('Creating backup with options:', { includeDatabase, dbFormat, includeFiles, includeConfig });
    const backup = await backupService.createZipArchive({
      includeDatabase,
      dbFormat,
      includeFiles,
      includeConfig
    });

    console.log('Backup created successfully:', backup.filename);
    res.json({
      message: 'Backup created successfully',
      filename: backup.filename,
      size: Math.round(backup.size / 1024 / 1024 * 100) / 100, // MB
      downloadUrl: `/api/backup/download/${backup.filename}`
    });
  } catch (error) {
    console.error('Error creating backup:', error);
    console.error('Error stack:', error.stack);
    res.status(500).json({ message: 'Failed to create backup', error: error.message });
  }
});

// Download backup file
router.get('/download/:filename', authenticateToken, async (req, res) => {
  try {
    // Check if user is admin
    if (req.user.role !== 'admin') {
      return res.status(403).json({ message: 'Admin access required' });
    }

    const { filename } = req.params;
    
    // Validate filename to prevent directory traversal
    if (!filename.match(/^backup_\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.zip$/)) {
      return res.status(400).json({ message: 'Invalid filename' });
    }

    const filepath = await backupService.getBackupFilePath(filename);
    
    // Check if file exists
    if (!fs.existsSync(filepath)) {
      return res.status(404).json({ message: 'Backup file not found' });
    }

    // Stream the file
    res.setHeader('Content-Type', 'application/zip');
    res.setHeader('Content-Disposition', `attachment; filename="${filename}"`);
    
    const fileStream = fs.createReadStream(filepath);
    fileStream.pipe(res);
  } catch (error) {
    console.error('Error downloading backup:', error);
    res.status(500).json({ message: 'Failed to download backup' });
  }
});

// List available backups
router.get('/list', authenticateToken, async (req, res) => {
  try {
    // Check if user is admin
    if (req.user.role !== 'admin') {
      return res.status(403).json({ message: 'Admin access required' });
    }

    const backups = await backupService.listBackups();
    res.json({ backups });
  } catch (error) {
    console.error('Error listing backups:', error);
    res.status(500).json({ message: 'Failed to list backups' });
  }
});

module.exports = router;

