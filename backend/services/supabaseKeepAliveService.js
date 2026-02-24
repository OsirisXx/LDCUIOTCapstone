require('dotenv').config();
const { getSupabaseAdmin, isSupabaseConfigured } = require('./supabaseAdmin');

const DAYS_TO_MS = 24 * 60 * 60 * 1000;
const DEFAULT_INTERVAL_DAYS = 3;

const isEnabled = () => {
  const flag = (process.env.SUPABASE_KEEPALIVE_ENABLED || 'true').toLowerCase();
  return flag === 'true' || flag === '1';
};

const getIntervalMs = () => {
  const rawDays = Number(process.env.SUPABASE_KEEPALIVE_INTERVAL_DAYS || DEFAULT_INTERVAL_DAYS);
  const sanitizedDays = Number.isFinite(rawDays) && rawDays > 0 ? rawDays : DEFAULT_INTERVAL_DAYS;
  return sanitizedDays * DAYS_TO_MS;
};

const pingSupabase = async () => {
  const supabaseAdmin = getSupabaseAdmin();

  if (!supabaseAdmin) {
    console.warn('[Supabase KeepAlive] Admin client unavailable – skipping ping');
    return;
  }

  try {
    const { error } = await supabaseAdmin.auth.admin.listUsers({ page: 1, perPage: 1 });

    if (error) {
      throw error;
    }

    console.log('[Supabase KeepAlive] Ping OK');
  } catch (error) {
    console.error('[Supabase KeepAlive] Ping failed:', error.message);
  }
};

const startSupabaseKeepAlive = () => {
  if (!isEnabled()) {
    console.log('[Supabase KeepAlive] Disabled via SUPABASE_KEEPALIVE_ENABLED');
    return;
  }

  if (!isSupabaseConfigured()) {
    console.warn('[Supabase KeepAlive] Supabase not configured – keep-alive skipped');
    return;
  }

  const intervalMs = getIntervalMs();

  pingSupabase();
  setInterval(pingSupabase, intervalMs);

  console.log(`[Supabase KeepAlive] Scheduled every ${intervalMs / DAYS_TO_MS} day(s)`);
};

module.exports = {
  startSupabaseKeepAlive
};




