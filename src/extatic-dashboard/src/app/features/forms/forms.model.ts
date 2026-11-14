export type SubmissionStatus = 'new' | 'read' | 'spam' | 'archived';

export interface Form {
  id: string;
  name: string;
  slug: string;
  schema: Record<string, unknown> | boolean | null;
  notification_emails: string[];
  success_redirect_url: string | null;
  honeypot_field: string | null;
  is_active: boolean;
  store_submissions: boolean;
  require_app_user: boolean;
  submission_count: number;
  unread_count: number;
  spam_count: number;
  created_at: string;
  updated_at: string;
}

export interface SaveFormRequest {
  name: string;
  slug: string;
  schema: unknown | null;
  notification_emails: string[];
  success_redirect_url: string | null;
  honeypot_field: string | null;
  is_active: boolean;
  store_submissions: boolean;
  require_app_user: boolean;
}

export interface FormSubmission {
  id: string;
  form_id: string;
  data: Record<string, unknown>;
  app_user_id: string | null;
  user_agent: string | null;
  referrer: string | null;
  status: SubmissionStatus;
  created_at: string;
}

/** Display a submitted value: strings as-is, arrays of strings comma-joined, anything else as JSON. */
export function displayValue(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value;
  if (Array.isArray(value) && value.every(v => typeof v === 'string')) return value.join(', ');
  return JSON.stringify(value);
}

/** Copy-paste snippets for a site. `apiBase` is e.g. "https://api.example.com/api". */
export function embedSnippets(apiBase: string, publicId: string, form: Pick<Form, 'slug' | 'honeypot_field'>) {
  const action = `${apiBase}/${publicId}/forms/${form.slug}/submissions`;
  const honeypot = form.honeypot_field
    ? `\n  <!-- Spam trap: keep hidden and empty -->\n  <input type="text" name="${form.honeypot_field}" tabindex="-1" autocomplete="off" style="position:absolute;left:-9999px" aria-hidden="true">`
    : '';
  const html = `<form action="${action}" method="POST">
  <label>Name <input type="text" name="name" required></label>
  <label>Email <input type="email" name="email" required></label>
  <label>Message <textarea name="message"></textarea></label>${honeypot}
  <button type="submit">Send</button>
</form>`;
  const js = `const res = await fetch('${action}', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ name: 'Ada', email: 'ada@example.com', message: 'Hello!' }),
});
if (res.status === 201) {
  const { id, created_at } = await res.json();
} else if (res.status === 422) {
  const { errors } = await res.json(); // [{ pointer, message }]
}`;
  return { action, html, js };
}
