const { getConnection, getSingleResult } = require('../config/database');
const { isSupabaseConfigured, getSupabaseAdmin } = require('./supabaseAdmin');
const { logSecurityEvent } = require('./auditService');

const VALID_ROLES = new Set(['student', 'instructor', 'admin', 'custodian', 'dean', 'superadmin']);

const updateUserRole = async ({ targetUserId, email, newRole, actorId, ipAddress }) => {
  const normalizedRole = (newRole || '').toLowerCase();

  if (!VALID_ROLES.has(normalizedRole)) {
    const error = new Error(`Invalid role: ${newRole}`);
    error.statusCode = 400;
    throw error;
  }

  const connection = await getConnection();

  try {
    await connection.beginTransaction();

    await connection.execute(
      'UPDATE USERS SET USERTYPE = ?, UPDATED_AT = CURRENT_TIMESTAMP WHERE USERID = ?',
      [normalizedRole, targetUserId]
    );

    // Try to sync with Supabase, but don't fail if it doesn't work
    if (isSupabaseConfigured() && email) {
      try {
        const supabaseAdmin = getSupabaseAdmin();

        const { data: supabaseUserData, error: lookupError } = await supabaseAdmin.auth.admin.getUserByEmail(email);

        if (lookupError) {
          console.warn(`Supabase user lookup failed for ${email}:`, lookupError.message);
          // Continue with MySQL update even if Supabase lookup fails
        } else if (supabaseUserData?.user) {
          const { error: updateError } = await supabaseAdmin.auth.admin.updateUserById(
            supabaseUserData.user.id,
            {
              app_metadata: {
                ...(supabaseUserData.user.app_metadata || {}),
                role: normalizedRole,
                updatedBy: actorId || 'system'
              }
            }
          );

          if (updateError) {
            console.warn(`Supabase metadata update failed for ${email}:`, updateError.message);
            // Continue with MySQL update even if Supabase update fails
          } else {
            console.log(`Successfully updated Supabase metadata for ${email} to role: ${normalizedRole}`);
          }
        } else {
          console.warn(`No Supabase user found for email: ${email}. MySQL role updated, but Supabase sync skipped.`);
        }
      } catch (supabaseError) {
        // Log but don't fail the transaction - MySQL update is primary source of truth
        console.warn('Supabase role sync failed (non-fatal):', supabaseError.message);
      }
    } else if (!email) {
      console.warn(`User ${targetUserId} has no email, skipping Supabase sync`);
    }

    await connection.commit();

    // Try to log security event, but don't fail if it doesn't work
    try {
      await logSecurityEvent({
        eventType: 'role_update',
        actorUserId: actorId || null,
        targetUserId,
        metadata: {
          email: email || 'no-email',
          role: normalizedRole
        },
        ipAddress
      });
    } catch (auditError) {
      console.warn('Security audit logging failed (non-fatal):', auditError.message);
    }
  } catch (error) {
    await connection.rollback();
    throw error;
  } finally {
    connection.release();
  }

  // Get updated user data
  const updatedUser = await getSingleResult(
    `SELECT USERID, EMAIL, USERTYPE, FIRSTNAME, LASTNAME,
            STATUS, DEPARTMENT, STUDENTID, FACULTYID, YEARLEVEL, RFIDTAG,
            CREATED_AT, UPDATED_AT
     FROM USERS WHERE USERID = ?`,
    [targetUserId]
  );

  if (!updatedUser) {
    throw new Error('User was updated but could not be retrieved');
  }

  return updatedUser;
};

module.exports = {
  updateUserRole,
  VALID_ROLES
};

