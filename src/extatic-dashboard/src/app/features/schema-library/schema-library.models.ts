import { HttpErrorResponse } from '@angular/common/http';

export type SchemaScope = 'global' | 'app';
export type SchemaVisibility = 'admin_only' | 'shared';

export interface LibrarySchemaSummary {
  id: string;
  scope: SchemaScope;
  app_id: string | null;
  name: string;
  slug: string;
  description: string | null;
  category: string | null;
  tags: string[];
  visibility: SchemaVisibility | null;
  version: number;
  is_archived: boolean;
  created_at: string;
  updated_at: string;
}

export interface LibrarySchemaVersionSummary {
  version: number;
  change_note: string | null;
  created_by_user_id: string;
  created_at: string;
}

export interface LibrarySchema extends LibrarySchemaSummary {
  schema: unknown;
  created_by_user_id: string;
  updated_by_user_id: string;
  versions: LibrarySchemaVersionSummary[];
}

export interface LibrarySchemaVersion {
  library_schema_id: string;
  version: number;
  schema: unknown;
  change_note: string | null;
  created_by_user_id: string;
  created_at: string;
}

export interface AvailableSchema {
  id: string;
  scope: SchemaScope;
  name: string;
  slug: string;
  description: string | null;
  category: string | null;
  tags: string[];
  version: number;
  admin_only: boolean;
}

export interface AvailableSchemaDetail extends AvailableSchema {
  schema: unknown;
}

export interface SchemaUsage {
  app_id: string;
  app_slug: string;
  app_name: string;
  collection_id: string;
  collection_slug: string;
  collection_name: string;
  source_schema_version: number | null;
}

export interface FieldError {
  pointer: string;
  message: string;
}

export interface ValidateSchemaResponse {
  valid: boolean;
  schema_errors: FieldError[];
  sample_errors: FieldError[] | null;
}

/** API base for a library: the global one (platform admins) or one App's. */
export function libraryBase(appSlug?: string | null): string {
  return appSlug ? `/apps/${appSlug}/schemas` : '/admin/schemas';
}

/** Pretty-printed JSON for textareas and diffs. */
export function prettyJson(value: unknown): string {
  return JSON.stringify(value, null, 2);
}

/** Key-order-insensitive JSON text, used to decide whether a schema was modified. */
export function canonicalJson(value: unknown): string {
  const sort = (v: unknown): unknown => {
    if (Array.isArray(v)) return v.map(sort);
    if (v && typeof v === 'object') {
      return Object.keys(v as Record<string, unknown>).sort()
        .reduce((acc, k) => { acc[k] = sort((v as Record<string, unknown>)[k]); return acc; }, {} as Record<string, unknown>);
    }
    return v;
  };
  return JSON.stringify(sort(value));
}

/** Same JSON content (ignoring formatting and key order)? Unparseable text is never equal. */
export function sameJson(text: string, value: unknown): boolean {
  try {
    return canonicalJson(JSON.parse(text)) === canonicalJson(value);
  } catch {
    return false;
  }
}

/** Human-readable message from an API error (ProblemDetails with optional field errors). */
export function apiErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof HttpErrorResponse && err.error && typeof err.error === 'object') {
    const body = err.error as { detail?: string; errors?: FieldError[] };
    const fields = Array.isArray(body.errors) ? body.errors.map(e => `${e.pointer}: ${e.message}`) : [];
    if (fields.length) return fields.join('; ');
    if (body.detail) return body.detail;
  }
  return fallback;
}
