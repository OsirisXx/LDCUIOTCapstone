const test = require('node:test');
const assert = require('node:assert/strict');

const originalAllowedEmails = process.env.SUPABASE_ALLOWED_EMAILS;
const originalAllowedDomains = process.env.SUPABASE_ALLOWED_DOMAINS;

const { isEmailAllowlisted } = require('../services/supabaseAuthService');

const resetEnv = () => {
  process.env.SUPABASE_ALLOWED_EMAILS = originalAllowedEmails;
  process.env.SUPABASE_ALLOWED_DOMAINS = originalAllowedDomains;
};

test('allows any email when no allowlist configured', (t) => {
  process.env.SUPABASE_ALLOWED_EMAILS = '';
  process.env.SUPABASE_ALLOWED_DOMAINS = '';

  assert.equal(isEmailAllowlisted('user@example.com'), true);
  assert.equal(isEmailAllowlisted('admin@ldcu.edu.ph'), true);

  t.after(resetEnv);
});

test('enforces domain allowlist when configured', (t) => {
  process.env.SUPABASE_ALLOWED_EMAILS = '';
  process.env.SUPABASE_ALLOWED_DOMAINS = 'ldcu.edu.ph';

  assert.equal(isEmailAllowlisted('faculty@ldcu.edu.ph'), true);
  assert.equal(isEmailAllowlisted('student@ldcu.edu.ph'), true);
  assert.equal(isEmailAllowlisted('intruder@example.com'), false);

  t.after(resetEnv);
});

test('email allowlist overrides domain allowlist when present', (t) => {
  process.env.SUPABASE_ALLOWED_EMAILS = 'admin@ldcu.edu.ph, security@ldcu.edu.ph';
  process.env.SUPABASE_ALLOWED_DOMAINS = 'ldcu.edu.ph';

  assert.equal(isEmailAllowlisted('admin@ldcu.edu.ph'), true);
  assert.equal(isEmailAllowlisted('security@ldcu.edu.ph'), true);
  assert.equal(isEmailAllowlisted('faculty@ldcu.edu.ph'), false);

  t.after(resetEnv);
});


