const { executeQuery } = require('../config/database');

// Optional; disabled by default so no DB schema changes are required
const isAuditEnabled = () => (process.env.ENABLE_SECURITY_AUDIT || '0') === '1';

const logSecurityEvent = async ({ eventType, actorUserId = null, targetUserId = null, metadata = {}, ipAddress = null }) => {
  if (!isAuditEnabled()) {
    return; // no-op when auditing is disabled
  }

  try {
    await executeQuery(
      `INSERT INTO SECURITY_AUDIT_LOGS (EVENT_TYPE, ACTOR_USER_ID, TARGET_USER_ID, METADATA_JSON, IP_ADDRESS)
       VALUES (?, ?, ?, ?, ?)`,
      [eventType, actorUserId, targetUserId, JSON.stringify(metadata || {}), ipAddress]
    );
  } catch (error) {
    // Swallow errors to avoid impacting auth/role flows if table doesn't exist
    // eslint-disable-next-line no-console
    console.warn('Security audit logging skipped:', error?.message || error);
  }
};

module.exports = {
  logSecurityEvent
};

