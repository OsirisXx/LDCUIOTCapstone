require('dotenv').config();
const { createClient } = require('@supabase/supabase-js');

const supabaseUrl = process.env.SUPABASE_URL;
const serviceRoleKey = process.env.SUPABASE_SERVICE_ROLE_KEY;

if (!supabaseUrl || !serviceRoleKey) {
  // eslint-disable-next-line no-console
  console.warn('Supabase URL/service role key not fully configured. Supabase admin client disabled.');
}

let supabaseAdminClient = null;

if (supabaseUrl && serviceRoleKey) {
  supabaseAdminClient = createClient(supabaseUrl, serviceRoleKey, {
    auth: {
      autoRefreshToken: false,
      persistSession: false,
      detectSessionInUrl: false
    }
  });
}

const getSupabaseAdmin = () => supabaseAdminClient;

const isSupabaseConfigured = () => Boolean(supabaseAdminClient);

module.exports = {
  getSupabaseAdmin,
  isSupabaseConfigured
};

