const crypto = require('crypto');

// In-memory device registry with auto-expiry on stale devices
class DeviceRegistry {
    constructor(options = {}) {
        this.deviceIdToDevice = new Map();
        this.heartbeatTtlMs = options.heartbeatTtlMs || 60 * 1000; // 60s default
        this.cleanupIntervalMs = options.cleanupIntervalMs || 30 * 1000; // 30s
        this.startCleanupLoop();
    }

    startCleanupLoop() {
        setInterval(() => {
            const now = Date.now();
            for (const [deviceId, info] of this.deviceIdToDevice.entries()) {
                if (now - info.lastHeartbeatMs > this.heartbeatTtlMs) {
                    this.deviceIdToDevice.delete(deviceId);
                }
            }
        }, this.cleanupIntervalMs).unref();
    }

    upsertHeartbeat(payload) {
        const now = Date.now();
        const deviceId = payload.deviceId || this.generateDeterministicId(payload);
        const record = {
            deviceId,
            deviceType: payload.deviceType || 'Unknown',
            location: payload.location || null,
            roomId: payload.roomId || null,
            roomNumber: payload.roomNumber || null,
            ipAddress: payload.ipAddress || null,
            hostname: payload.hostname || null,
            appVersion: payload.appVersion || null,
            capabilities: payload.capabilities || [],
            lastHeartbeatMs: now,
        };
        this.deviceIdToDevice.set(deviceId, record);
        return record;
    }

    generateDeterministicId(payload) {
        const basis = `${payload.hostname || ''}|${payload.ipAddress || ''}|${payload.location || ''}|${payload.roomId || ''}|${payload.roomNumber || ''}`;
        return crypto.createHash('sha1').update(basis).digest('hex').slice(0, 16);
    }

    listOnline() {
        const now = Date.now();
        return Array.from(this.deviceIdToDevice.values()).map(d => ({
            ...d,
            online: now - d.lastHeartbeatMs <= this.heartbeatTtlMs
        })).filter(d => d.online);
    }
}

// Singleton instance
const registry = new DeviceRegistry();
module.exports = registry;




