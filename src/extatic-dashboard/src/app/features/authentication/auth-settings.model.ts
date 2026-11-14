export interface AuthSettings {
  registration_enabled: boolean;
  require_email_verification: boolean;
  password_min_length: number;
  access_token_minutes: number;
  refresh_token_days: number;
  max_failed_attempts: number;
  lockout_minutes: number;
  verify_url_template: string | null;
  reset_url_template: string | null;
  allowed_email_domains: string[];
  updated_at: string | null;
}

export type UpdateAuthSettings = Omit<AuthSettings, 'updated_at'>;

/**
 * Mirrors the API rule: absolute https URL (http only for localhost / 127.0.0.1), no credentials,
 * containing the {token} placeholder. Empty is allowed (the token is then emailed as a code).
 */
export function urlTemplateError(value: string | null | undefined): string | null {
  const template = (value ?? '').trim();
  if (!template) return null;
  if (!template.includes('{token}')) return 'Must contain the {token} placeholder.';
  let url: URL;
  try {
    url = new URL(template.replaceAll('{token}', 'TOKEN'));
  } catch {
    return 'Must be an absolute URL.';
  }
  if (url.username || url.password || !url.hostname) return 'Must be an absolute URL.';
  const local = url.hostname === 'localhost' || url.hostname === '127.0.0.1' || url.hostname === '[::1]';
  if (url.protocol !== 'https:' && !(url.protocol === 'http:' && local)) {
    return 'Must use https (http is allowed only for localhost).';
  }
  return null;
}

/** Splits a comma/whitespace/newline separated list into normalised domains. */
export function parseDomains(text: string): string[] {
  return Array.from(new Set(
    text.split(/[\s,]+/).map(d => d.trim().replace(/^@/, '').toLowerCase()).filter(d => d.length > 0),
  ));
}
