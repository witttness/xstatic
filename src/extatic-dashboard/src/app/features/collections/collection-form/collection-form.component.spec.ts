import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CollectionFormComponent } from './collection-form.component';

const COLLECTION = {
  id: 'c1', app_id: 'a1', name: 'Posts', slug: 'posts', schema: '{"type": "object", "title": "a"}',
  attachments_enabled: false, allowed_attachment_types: [], created_at: '2026-01-01', updated_at: '2026-01-01',
  source_schema_id: 'g1', source_schema_version: 1, source_schema_name: 'Blog Post', source_schema_latest_version: 2,
};

describe('CollectionFormComponent (schema library)', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CollectionFormComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function create(slug?: string) {
    const fixture = TestBed.createComponent(CollectionFormComponent);
    fixture.componentRef.setInput('appSlug', 'shop');
    if (slug) fixture.componentRef.setInput('slug', slug);
    fixture.detectChanges();
    return fixture;
  }

  function openEdit(collection: Record<string, unknown> = COLLECTION, sourceVersion: unknown = { type: 'object', title: 'a' }) {
    const fixture = create('posts');
    http.expectOne('/api/apps/shop/collections/posts').flush(collection);
    if (collection['source_schema_name']) {
      http.expectOne('/api/apps/shop/schemas/available/g1/versions/1')
        .flush({ library_schema_id: 'g1', version: 1, schema: sourceVersion });
    }
    fixture.detectChanges();
    return fixture;
  }

  it('creates a plain collection exactly as before', () => {
    const fixture = create();
    const c = fixture.componentInstance;
    c.form.patchValue({ name: 'Posts', slug: 'posts', schema: '{}' });
    c.onSubmit();
    const req = http.expectOne('/api/apps/shop/collections');
    expect(req.request.body).toEqual({ name: 'Posts', slug: 'posts', schema: '{}', attachments_enabled: false, allowed_attachment_types: [] });
  });

  it('fills the schema from the picker and sends provenance', () => {
    const fixture = create();
    const c = fixture.componentInstance;
    (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
      .find(b => b.textContent?.includes('Choose from library'))!.click();
    fixture.detectChanges();
    http.expectOne('/api/apps/shop/schemas/available').flush({ items: [], total: 0, page: 1, page_size: 100 });
    expect(fixture.nativeElement.textContent).toContain('Use this schema');

    c.usePicked({ id: 'g1', name: 'Blog Post', version: 3, schemaText: '{\n  "type": "object"\n}' });
    fixture.detectChanges();
    expect(c.pickerOpen()).toBe(false);
    expect(c.form.get('schema')!.value).toBe('{\n  "type": "object"\n}');
    expect(fixture.nativeElement.querySelector('[data-testid="schema-source"]').textContent).toContain('Based on Blog Post v3');

    c.form.patchValue({ name: 'Posts', slug: 'posts' });
    c.onSubmit();
    expect(http.expectOne('/api/apps/shop/collections').request.body)
      .toMatchObject({ source_schema_id: 'g1', source_schema_version: 3, schema: '{\n  "type": "object"\n}' });
  });

  it('shows provenance, marks manual edits as modified and offers the update', () => {
    const fixture = openEdit();
    const c = fixture.componentInstance;
    const banner = () => fixture.nativeElement.querySelector('[data-testid="schema-source"]').textContent as string;
    expect(banner()).toContain('Based on Blog Post v1');
    expect(banner()).toContain('Update available (v2)');
    expect(banner()).not.toContain('modified');

    const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = '{"type": "object", "title": "mine"}';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(banner()).toContain('modified');

    c.reviewUpdate();
    http.expectOne('/api/apps/shop/schemas/available/g1').flush({
      id: 'g1', scope: 'global', name: 'Blog Post', slug: 'post', description: null, category: null, tags: [],
      version: 2, admin_only: false, schema: { type: 'object', title: 'b' },
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Update to Blog Post v2');
    expect(c.updateDiff().some(l => l.type === 'add' && l.text.includes('"title": "b"'))).toBe(true);
    c.applyUpdate();
    fixture.detectChanges();
    expect(c.source()!.version).toBe(2);
    expect(c.sourceModified()).toBe(false);
    expect(JSON.parse(c.form.get('schema')!.value!)).toEqual({ type: 'object', title: 'b' });

    c.onSubmit();
    const put = http.expectOne('/api/apps/shop/collections/posts');
    expect(put.request.body).toMatchObject({ source_schema_id: 'g1', source_schema_version: 2 });
  });

  it('detects a stored schema that differs from its source version', () => {
    const fixture = openEdit(COLLECTION, { type: 'object', title: 'something else' });
    expect(fixture.componentInstance.sourceModified()).toBe(true);
  });

  it('detaches provenance explicitly on save', () => {
    const fixture = openEdit();
    const c = fixture.componentInstance;
    c.detach();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="schema-source"]')).toBeNull();
    c.onSubmit();
    const body = http.expectOne('/api/apps/shop/collections/posts').request.body;
    expect(body.detach_source_schema).toBe(true);
    expect(body.source_schema_id).toBeUndefined();
  });

  it('handles a source that is no longer visible', () => {
    const fixture = openEdit({ ...COLLECTION, source_schema_name: null, source_schema_latest_version: null });
    const text = fixture.nativeElement.querySelector('[data-testid="schema-source"]').textContent as string;
    expect(text).toContain('no longer available');
    expect(text).not.toContain('Update available');
  });
});
