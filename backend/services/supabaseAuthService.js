const jwt = require('jsonwebtoken');
const { getSupabaseAdmin, isSupabaseConfigured } = require('./supabaseAdmin');

const parseCsv = (value = '') =>
  value
    .split(',')
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);

const isEmailAllowlisted = (email) => {
  const normalizedEmail = (email || '').toLowerCase();
  const domain = normalizedEmail.split('@')[1];

  const allowedEmails = parseCsv(process.env.SUPABASE_ALLOWED_EMAILS);
  const allowedDomains = parseCsv(process.env.SUPABASE_ALLOWED_DOMAINS);

  if (allowedEmails.length > 0 && !allowedEmails.includes(normalizedEmail)) {
    return false;
  }

  if (allowedDomains.length > 0 && (!domain || !allowedDomains.includes(domain))) {
    return false;
  }

  return true;
};

const verifySupabaseAccessToken = async (accessToken) => {
  if (!isSupabaseConfigured()) {
    const error = new Error('Supabase integration is not configured.');
    error.statusCode = 503;
    throw error;
  }

  let decoded;
  try {
    decoded = jwt.decode(accessToken, { json: true });

    if (!decoded) {
      const malformedError = new Error('Failed to parse Supabase token.');
      malformedError.statusCode = 400;
      throw malformedError;
    }

    if (!decoded.sub) {
      const invalidTokenError = new Error('Invalid Supabase access token: missing user ID.');
      invalidTokenError.statusCode = 401;
      throw invalidTokenError;
    }

    const expectedAudience = (process.env.SUPABASE_JWT_AUDIENCE || '').trim();
    const expectedIssuer = (process.env.SUPABASE_JWT_ISSUER || '').trim();

    if (expectedAudience && decoded.aud && decoded.aud !== expectedAudience) {
      const audienceError = new Error('Supabase token audience mismatch.');
      audienceError.statusCode = 401;
      throw audienceError;
    }

    if (expectedIssuer && decoded.iss && decoded.iss !== expectedIssuer) {
      const issuerError = new Error('Supabase token issuer mismatch.');
      issuerError.statusCode = 401;
      throw issuerError;
    }
  } catch (decodeError) {
    if (decodeError.statusCode) {
      throw decodeError;
    }

    const malformedError = new Error('Failed to parse Supabase token.');
    malformedError.statusCode = 400;
    malformedError.cause = decodeError;
    throw malformedError;
  }

  const supabaseAdmin = getSupabaseAdmin();

  try {

    // Use admin API to get user by ID
    const { data, error } = await supabaseAdmin.auth.admin.getUserById(decoded.sub);

    if (error) {
      const invalidTokenError = new Error('Invalid Supabase access token.');
      invalidTokenError.statusCode = 401;
      invalidTokenError.cause = error;
      throw invalidTokenError;
    }

    if (!data || !data.user) {
      const invalidTokenError = new Error('User not found in Supabase.');
      invalidTokenError.statusCode = 404;
      throw invalidTokenError;
    }

    return data.user;
  } catch (err) {
    if (err.statusCode) {
      throw err;
    }

    const upstreamError = new Error('Unable to verify Supabase token.');
    upstreamError.statusCode = 503;
    upstreamError.cause = err;
    throw upstreamError;
  }
};

module.exports = {
  verifySupabaseAccessToken,
  isEmailAllowlisted,
  isSupabaseConfigured
};

