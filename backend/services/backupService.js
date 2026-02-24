const fs = require('fs').promises;
const path = require('path');
const archiver = require('archiver');
const { pool, executeQuery, getResults, getSingleResult } = require('../config/database');

class BackupService {
  constructor() {
    this.backupDir = path.join(__dirname, '../backups');
    this.uploadsDir = path.join(__dirname, '../uploads');
    this.archivedBackupDir = path.join(__dirname, '../archived_backups');
  }

  // Initialize backup directory
  async ensureBackupDir() {
    try {
      await fs.mkdir(this.backupDir, { recursive: true });
      await fs.mkdir(this.archivedBackupDir, { recursive: true });
    } catch (error) {
      if (error.code !== 'EEXIST') {
        throw error;
      }
    }
  }

  // Get backup statistics
  async getBackupStats() {
    try {
      // Get database size
      const dbSizeQuery = `
        SELECT 
          ROUND(SUM(data_length + index_length) / 1024 / 1024, 2) AS size_mb
        FROM information_schema.tables
        WHERE table_schema = DATABASE()
      `;
      const dbResult = await getSingleResult(dbSizeQuery);
      const dbSize = dbResult?.size_mb || 0;

      // Get table counts
      const tablesQuery = `
        SELECT table_name, table_rows
        FROM information_schema.tables
        WHERE table_schema = DATABASE()
        ORDER BY table_name
      `;
      const tables = await getResults(tablesQuery);

      // Count files in uploads directory
      let fileCount = 0;
      let totalFileSize = 0;
      try {
        const files = await fs.readdir(this.uploadsDir);
        fileCount = files.length;
        for (const file of files) {
          const filePath = path.join(this.uploadsDir, file);
          const stats = await fs.stat(filePath);
          totalFileSize += stats.size;
        }
      } catch (error) {
        // Directory might not exist
        fileCount = 0;
      }

      // Get recent backups
      const backups = await this.listBackups();

      return {
        databaseSize: dbSize,
        tableCount: tables.length,
        uploadFileCount: fileCount,
        uploadTotalSize: Math.round(totalFileSize / 1024 / 1024 * 100) / 100, // MB
        recentBackups: backups.slice(0, 5),
        lastBackup: backups.length > 0 ? backups[0].date : null
      };
    } catch (error) {
      console.error('Error getting backup stats:', error);
      throw error;
    }
  }

  // List available backups
  async listBackups() {
    try {
      await this.ensureBackupDir();
      const files = await fs.readdir(this.backupDir);
      const backups = [];
      
      for (const file of files) {
        if (file.endsWith('.zip')) {
          const filePath = path.join(this.backupDir, file);
          const stats = await fs.stat(filePath);
          backups.push({
            filename: file,
            size: Math.round(stats.size / 1024 / 1024 * 100) / 100, // MB
            date: stats.mtime
          });
        }
      }
      
      return backups.sort((a, b) => b.date - a.date);
    } catch (error) {
      console.error('Error listing backups:', error);
      return [];
    }
  }

  // List archived backups
  async listArchivedBackups() {
    try {
      await this.ensureBackupDir();
      const files = await fs.readdir(this.archivedBackupDir);
      const backups = [];
      for (const file of files) {
        if (file.endsWith('.zip')) {
          const filePath = path.join(this.archivedBackupDir, file);
          const stats = await fs.stat(filePath);
          backups.push({
            filename: file,
            size: Math.round(stats.size / 1024 / 1024 * 100) / 100,
            date: stats.mtime
          });
        }
      }
      return backups.sort((a, b) => b.date - a.date);
    } catch (error) {
      console.error('Error listing archived backups:', error);
      return [];
    }
  }

  // Archive backup files (move from backups to archived_backups)
  async archiveBackups(filenames = []) {
    await this.ensureBackupDir();
    let archivedCount = 0;
    for (const name of filenames) {
      const src = path.join(this.backupDir, name);
      const dest = path.join(this.archivedBackupDir, name);
      try {
        await fs.rename(src, dest);
        archivedCount++;
      } catch (err) {
        // skip missing files
      }
    }
    return { archived: archivedCount };
  }

  // Generate SQL dump (schema + data)
  async generateSqlDump() {
    try {
      const tables = await this.getAllTables();
      let sqlDump = `-- IoT Attendance System Database Backup\n`;
      sqlDump += `-- Generated: ${new Date().toISOString()}\n\n`;
      sqlDump += `SET FOREIGN_KEY_CHECKS=0;\n`;
      sqlDump += `SET UNIQUE_CHECKS=0;\n`;
      sqlDump += `SET AUTOCOMMIT=0;\n\n`;

      // Optional: include CREATE DATABASE and USE
      try {
        const dbNameRow = await getSingleResult('SELECT DATABASE() AS db');
        const dbName = dbNameRow?.db;
        if (dbName) {
          sqlDump += `-- Database: ${dbName}\n`;
          sqlDump += `CREATE DATABASE IF NOT EXISTS \`${dbName}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;\n`;
          sqlDump += `USE \`${dbName}\`;\n\n`;
        }
      } catch (_) {
        // ignore if cannot fetch DB name
      }

      for (const table of tables) {
        try {
          // Schema: DROP + CREATE TABLE
          const createResult = await getSingleResult(`SHOW CREATE TABLE \`${table}\``);
          const createSql = createResult?.['Create Table'] || createResult?.['Create TABLE'] || createResult?.CreateTable;
          if (createSql) {
            sqlDump += `--\n-- Table structure for table \`${table}\`\n--\n`;
            sqlDump += `DROP TABLE IF EXISTS \`${table}\`;\n`;
            sqlDump += `${createSql};\n\n`;
          }

          // Data: INSERT rows
          const rows = await getResults(`SELECT * FROM \`${table}\``);
          if (rows.length > 0) {
            const columns = Object.keys(rows[0]);
            sqlDump += `--\n-- Dumping data for table \`${table}\`\n--\n`;

            for (const row of rows) {
              const values = columns.map(col => {
                const value = row[col];
                if (value === null) return 'NULL';
                if (Buffer.isBuffer(value)) {
                  const hex = value.toString('hex');
                  return hex ? `0x${hex}` : 'NULL';
                }
                if (typeof value === 'string') {
                  return `'${value.replace(/\\/g, '\\\\').replace(/'/g, "''").replace(/\n/g, '\\n').replace(/\r/g, '\\r')}'`;
                }
                if (value instanceof Date) return `'${value.toISOString().slice(0, 19).replace('T', ' ')}'`;
                if (typeof value === 'boolean') return value ? '1' : '0';
                if (typeof value === 'number') return String(value);
                return `'${String(value).replace(/'/g, "''")}'`;
              });
              sqlDump += `INSERT INTO \`${table}\` (\`${columns.join('`, `')}\`) VALUES (${values.join(', ')});\n`;
            }
            sqlDump += `\n`;
          }
        } catch (tableError) {
          console.error(`Error processing table ${table}:`, tableError);
          sqlDump += `-- Error processing table ${table}: ${tableError.message}\n\n`;
        }
      }

      sqlDump += `SET FOREIGN_KEY_CHECKS=1;\n`;
      sqlDump += `SET UNIQUE_CHECKS=1;\n`;
      sqlDump += `COMMIT;\n`;
      return sqlDump;
    } catch (error) {
      console.error('Error generating SQL dump:', error);
      throw error;
    }
  }

  // Generate JSON export
  async generateJsonExport() {
    try {
      const tables = await this.getAllTables();
      const exportData = {
        metadata: {
          database: process.env.DB_NAME || 'iot_attendance',
          exportedAt: new Date().toISOString(),
          version: '1.0'
        },
        tables: {}
      };

      for (const table of tables) {
        const rows = await getResults(`SELECT * FROM \`${table}\``);
        exportData.tables[table] = rows;
      }

      return JSON.stringify(exportData, null, 2);
    } catch (error) {
      console.error('Error generating JSON export:', error);
      throw error;
    }
  }

  // Get all table names
  async getAllTables() {
    const query = `
      SELECT table_name
      FROM information_schema.tables
      WHERE table_schema = DATABASE()
      ORDER BY table_name
    `;
    const results = await getResults(query);
    return results.map(row => row.TABLE_NAME || row.table_name).filter(Boolean);
  }

  // Copy uploaded files
  async copyUploadedFiles() {
    try {
      const files = await fs.readdir(this.uploadsDir);
      const filesToBackup = [];
      
      for (const file of files) {
        const filePath = path.join(this.uploadsDir, file);
        const stats = await fs.stat(filePath);
        if (stats.isFile()) {
          filesToBackup.push({
            name: file,
            path: filePath
          });
        }
      }
      
      return filesToBackup;
    } catch (error) {
      if (error.code === 'ENOENT') {
        return [];
      }
      throw error;
    }
  }

  // Copy config files (sanitized)
  async copyConfigFiles() {
    const configFiles = [];
    
    try {
      // Copy package.json (safe to include)
      const packageJsonPath = path.join(__dirname, '../package.json');
      const packageJsonContent = await fs.readFile(packageJsonPath, 'utf8');
      configFiles.push({
        name: 'package.json',
        content: packageJsonContent
      });

      // Copy .env.example if exists (safe template)
      const envExamplePath = path.join(__dirname, '../env.example');
      try {
        const envExampleContent = await fs.readFile(envExamplePath, 'utf8');
        configFiles.push({
          name: 'env.example',
          content: envExampleContent
        });
      } catch (error) {
        // File doesn't exist, skip
      }

      // Create sanitized .env backup (remove sensitive data)
      const envPath = path.join(__dirname, '../.env');
      try {
        const envContent = await fs.readFile(envPath, 'utf8');
        const sanitizedContent = envContent
          .split('\n')
          .map(line => {
            if (line.includes('PASSWORD') || line.includes('SECRET') || line.includes('KEY')) {
              return line.split('=')[0] + '=[REDACTED]';
            }
            return line;
          })
          .join('\n');
        
        configFiles.push({
          name: 'env.sanitized',
          content: sanitizedContent
        });
      } catch (error) {
        // File doesn't exist, skip
      }
    } catch (error) {
      console.error('Error copying config files:', error);
    }

    return configFiles;
  }

  // Create ZIP archive
  async createZipArchive(options = {}) {
    const {
      includeDatabase = true,
      dbFormat = 'both', // 'sql', 'json', or 'both'
      includeFiles = true,
      includeConfig = true
    } = options;

    await this.ensureBackupDir();
    
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, -5);
    const filename = `backup_${timestamp}.zip`;
    const filepath = path.join(this.backupDir, filename);

    return new Promise(async (resolve, reject) => {
      try {
        const output = require('fs').createWriteStream(filepath);
        const archive = archiver('zip', { zlib: { level: 9 } });

        output.on('close', () => {
          resolve({
            filename,
            filepath,
            size: archive.pointer()
          });
        });

        archive.on('error', (err) => {
          reject(err);
        });

        archive.pipe(output);

        // Add database export
        if (includeDatabase) {
          if (dbFormat === 'sql' || dbFormat === 'both') {
            try {
              const sqlDump = await this.generateSqlDump();
              archive.append(sqlDump, { name: 'database_backup.sql' });
            } catch (error) {
              console.error('Error generating SQL dump:', error);
              reject(error);
              return;
            }
          }
          
          if (dbFormat === 'json' || dbFormat === 'both') {
            try {
              const jsonExport = await this.generateJsonExport();
              archive.append(jsonExport, { name: 'database_backup.json' });
            } catch (error) {
              console.error('Error generating JSON export:', error);
              reject(error);
              return;
            }
          }
        }

        // Add uploaded files
        if (includeFiles) {
          try {
            const files = await this.copyUploadedFiles();
            for (const file of files) {
              archive.file(file.path, { name: `uploads/${file.name}` });
            }
          } catch (error) {
            console.error('Error copying uploaded files:', error);
            reject(error);
            return;
          }
        }

        // Add config files
        if (includeConfig) {
          try {
            const configFiles = await this.copyConfigFiles();
            for (const file of configFiles) {
              archive.append(file.content, { name: `config/${file.name}` });
            }
          } catch (error) {
            console.error('Error copying config files:', error);
            reject(error);
            return;
          }
        }

        // Add backup metadata
        const metadata = {
          created: new Date().toISOString(),
          options: options,
          system: 'IoT Attendance System',
          version: '1.0'
        };
        archive.append(JSON.stringify(metadata, null, 2), { name: 'backup_metadata.json' });

        archive.finalize();
      } catch (error) {
        reject(error);
      }
    });
  }

  // Get backup file path
  async getBackupFilePath(filename) {
    const filepath = path.join(this.backupDir, filename);
    try {
      await fs.access(filepath);
      return filepath;
    } catch (error) {
      throw new Error('Backup file not found');
    }
  }

  // Cleanup old backups (optional)
  async cleanupOldBackups(daysOld = 30) {
    try {
      const backups = await this.listBackups();
      const cutoffDate = new Date();
      cutoffDate.setDate(cutoffDate.getDate() - daysOld);

      let deletedCount = 0;
      for (const backup of backups) {
        if (backup.date < cutoffDate) {
          const filepath = path.join(this.backupDir, backup.filename);
          await fs.unlink(filepath);
          deletedCount++;
        }
      }

      return { deletedCount };
    } catch (error) {
      console.error('Error cleaning up old backups:', error);
      throw error;
    }
  }
}

module.exports = new BackupService();

