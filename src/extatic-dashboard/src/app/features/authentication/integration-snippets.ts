export interface IntegrationSnippet {
  title: string;
  code: string;
}

/** Copy-paste fetch() examples for a static site, using the App's public id. */
export function buildIntegrationSnippets(apiBase: string, publicId: string): IntegrationSnippet[] {
  const base = `${apiBase.replace(/\/$/, '')}/${publicId}/auth`;
  return [
    {
      title: 'Register',
      code: `// Always answers 202 with the same message (no account enumeration).
await fetch('${base}/register', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, password, display_name: name }),
});`,
    },
    {
      title: 'Verify email (on your verify page)',
      code: `const token = new URLSearchParams(location.search).get('token');
const res = await fetch('${base}/verify-email', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ token }),
});`,
    },
    {
      title: 'Log in',
      code: `const res = await fetch('${base}/login', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, password }),
});
if (res.status === 403 && (await res.clone().json()).code === 'email_not_verified') {
  // ask the user to check their inbox (POST ${base}/resend-verification)
}
const { access_token, expires_in, refresh_token, user } = await res.json();
// Keep access_token in memory; send it as: Authorization: Bearer <access_token>
sessionStorage.setItem('refresh_token', refresh_token);`,
    },
    {
      title: 'Refresh (rotate) the session',
      code: `// Each refresh token works once; always store the new one.
const res = await fetch('${base}/refresh', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ refresh_token: sessionStorage.getItem('refresh_token') }),
});
if (!res.ok) { /* signed out: show the login form */ }
const { access_token, refresh_token } = await res.json();
sessionStorage.setItem('refresh_token', refresh_token);`,
    },
  ];
}
