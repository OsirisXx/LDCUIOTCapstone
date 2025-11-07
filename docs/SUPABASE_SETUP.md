# Supabase Configuration Checklist

> Keep service credentials out of the frontend and version control.

## 1. Enable Google OAuth
- In Supabase Dashboard → `Authentication → Providers`, enable **Google**.
- Create/obtain a Google OAuth client ID & secret; paste them into the provider settings.
- Set the **Authorized Redirect URI** to `https://igsznmsegzglantjdalt.supabase.co/auth/v1/callback`.

## 2. Configure Redirects & Allowlists
- Under `Authentication → URL Configuration`, add your local frontend origin `http://localhost:3000` (and any staging/production domains).
- Add `http://localhost:3000` as an allowed redirect URL for OAuth flows.
- Use `Authentication → Policies` to restrict signups to approved domains or specific emails.

## 3. Manage Supabase Keys
- From `Project Settings → API`, copy the **anon** key for the frontend and the **service role** key for backend-only use.
- Store secrets only via environment variables:
  - Frontend: `frontend/env.example` (copy to `.env.local`, keep git-ignored).
  - Backend: `backend/env.example` (copy to `.env`, keep git-ignored).
- Rotate keys immediately if they leak or a developer departs.

## 4. Secure Admin Access
- Assign the SuperAdmin role inside Supabase `auth.users` metadata (via SQL or Dashboard) before enabling the UI.
- Keep at least two trusted admin accounts for redundancy.
- Disable password recovery for admin accounts if your flow relies strictly on Google SSO.

## 5. Logging & Auditing
- Enable Authentication logs in Supabase Settings.
- Consider streaming logs to an external SIEM for retention.
- Review failed login attempts before granting admin access.

## 6. Production Hardening
- Require HTTPS origins before deploying to public users.
- Enforce domain checks in backend middleware to ensure tokens originate from your Supabase project.
- Schedule regular key rotation & access reviews.

Document any deviations from this checklist in `README.md` to keep operations aligned.

