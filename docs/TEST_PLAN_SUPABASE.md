# Supabase Auth & SuperAdmin Test Plan

## Pre-requisites
- Supabase Google OAuth provider enabled and redirect URLs configured.
- Backend `.env` populated with Supabase keys and `SUPABASE_ALLOWED_*` filters.
- `SECURITY_AUDIT_LOGS` table created via `database/migration_add_security_audit_logs.sql`.

## 1. Authentication Flow
1. Launch frontend at `http://localhost:3000/login`.
2. Click **Continue with Google** and authenticate using an allowlisted account.
3. Verify browser redirects to dashboard and network tab shows `POST /api/auth/supabase` (HTTP 200).
4. Confirm `SECURITY_AUDIT_LOGS` contains a `supabase_login` entry with user ID and IP.
5. Attempt login with a non-allowlisted email; expect Supabase callback followed by 403 JSON error and no dashboard access.

## 2. Token Exchange Protections
1. Perform >20 rapid login attempts from same IP; verify `POST /api/auth/supabase` returns HTTP 429.
2. Inspect server logs to ensure rate-limit message emitted without stack traces.

## 3. SuperAdmin Console
1. Navigate to `/superadmin` as a superadmin; ensure page renders and lists users.
2. Attempt to visit `/superadmin` as an admin/instructor; verify redirect back to `/dashboard`.
3. Change another user’s role via dropdown:
   - Confirm toast notification, table updates, and database `USERTYPE` changes.
   - Check `SECURITY_AUDIT_LOGS` for `role_update` metadata (actor ID, target ID, role).
4. Toggle user status and ensure table + database reflect new `STATUS`.
5. Attempt to downgrade own superadmin role; expect toast error and no change.

## 4. API Authorization Guards
1. Using an admin (non-superadmin) token, call `PATCH /api/users/:id/role`; expect HTTP 403 payload.
2. Using superadmin token, call same endpoint; expect HTTP 200 and audit log entry.

## 5. Regression Checks
- Verify standard admin pages (`/users`, `/archive`, `/backup`) remain accessible to admins and superadmins.
- Validate legacy password login still functions for seeded accounts (if applicable).
- Run automated suite: `cd backend && npm test`.

Document results (pass/fail, notes) in the project tracking sheet after every release.

