import { describe, expect, it } from 'vitest';
import { extractAppSlug, FEATURE_APP_LINKS } from './sidebar.component';

describe('extractAppSlug', () => {
  it('extracts the slug from app-scoped urls', () => {
    expect(extractAppSlug('/apps/my-site/forms')).toBe('my-site');
    expect(extractAppSlug('/apps/my-site')).toBe('my-site');
    expect(extractAppSlug('/apps/my-site?tab=1')).toBe('my-site');
  });

  it('does not treat non-app urls as apps', () => {
    expect(extractAppSlug('/apps')).toBeNull();
    expect(extractAppSlug('/admin/schemas')).toBeNull();
    expect(extractAppSlug('/admin/schemas/abc')).toBeNull();
    expect(extractAppSlug('/unauthorized')).toBeNull();
  });

  it('lists every feature module link', () => {
    expect(FEATURE_APP_LINKS.map(l => l.path)).toEqual([
      'forms', 'events', 'comments', 'authentication', 'members', 'donations', 'search', 'scheduling',
    ]);
  });
});
