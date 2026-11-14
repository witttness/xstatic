import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SchemaEditorComponent } from './schema-editor.component';

const SCHEMA = {
  id: 's1', scope: 'global', app_id: null, name: 'Post', slug: 'post', description: null, category: 'Content',
  tags: ['blog'], visibility: 'admin_only', version: 2, is_archived: false, created_at: '2026-01-01', updated_at: '2026-01-02',
  schema: { type: 'object', title: 'b' }, created_by_user_id: 'u', updated_by_user_id: 'u',
  versions: [
    { version: 2, change_note: 'retitle', created_by_user_id: 'u', created_at: '2026-01-02' },
    { version: 1, change_note: null, created_by_user_id: 'u', created_at: '2026-01-01' },
  ],
};

describe('SchemaEditorComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SchemaEditorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function create(inputs: Record<string, string> = {}) {
    const fixture = TestBed.createComponent(SchemaEditorComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture;
  }

  it('rejects invalid JSON locally without calling the API', () => {
    const fixture = create();
    const c = fixture.componentInstance;
    c.form.controls.schema.setValue('{ nope');
    c.validate();
    expect(c.jsonError()).toBe('Schema is not valid JSON.');
    c.form.controls.schema.setValue('[1, 2]');
    c.validate();
    expect(c.jsonError()).toBe('Schema must be a JSON object.');
    c.form.controls.schema.setValue('{}');
    c.form.controls.sample.setValue('{ bad');
    c.validate();
    expect(c.jsonError()).toBe('Sample data is not valid JSON.');
    http.expectNone(() => true);
  });

  it('shows schema and sample errors from the validate endpoint', () => {
    const fixture = create({ appSlug: 'shop' });
    const c = fixture.componentInstance;
    c.form.controls.schema.setValue('{"type":"object","properties":{"title":{"type":"string"}}}');
    c.form.controls.sample.setValue('{"title": 5}');
    c.validate();
    const req = http.expectOne('/api/apps/shop/schemas/validate');
    expect(req.request.body).toEqual({ schema: { type: 'object', properties: { title: { type: 'string' } } }, sample: { title: 5 } });
    req.flush({ valid: false, schema_errors: [], sample_errors: [{ pointer: '/title', message: 'Value is "integer" but should be "string"' }] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="validation"]').textContent).toContain('Sample /title');
  });

  it('creates a global schema with visibility and surfaces API field errors', () => {
    const fixture = create();
    const c = fixture.componentInstance;
    c.form.patchValue({ name: 'Post', slug: 'post', tags: 'a, b', visibility: 'shared', schema: '{"$ref":"http://x"}' });
    c.save();
    const req = http.expectOne('/api/admin/schemas');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({ name: 'Post', slug: 'post', tags: ['a', 'b'], visibility: 'shared', schema: { $ref: 'http://x' } });
    req.flush({ title: 'Validation Failed', errors: [{ pointer: 'schema/$ref', message: 'External references are not allowed' }] },
      { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="save-error"]').textContent).toContain('schema/$ref: External references');
  });

  it('loads history, diffs a version and changes visibility on save', () => {
    const fixture = create({ id: 's1' });
    http.expectOne('/api/admin/schemas/s1').flush(SCHEMA);
    http.expectOne('/api/admin/schemas/s1/usage').flush([
      { app_id: 'a', app_slug: 'shop', app_name: 'Shop', collection_id: 'c', collection_slug: 'posts', collection_name: 'Posts', source_schema_version: 1 },
    ]);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(fixture.nativeElement.textContent).toContain('shop/posts');
    expect(fixture.nativeElement.textContent).toContain('retitle');

    c.compare(1);
    http.expectOne('/api/admin/schemas/s1/versions/1').flush({ library_schema_id: 's1', version: 1, schema: { type: 'object', title: 'a' } });
    expect(c.diff()!.filter(l => l.type !== 'same').map(l => l.type)).toEqual(['del', 'add']);

    c.form.controls.visibility.setValue('shared');
    c.save();
    const put = http.expectOne('/api/admin/schemas/s1');
    expect(put.request.method).toBe('PUT');
    put.flush(SCHEMA);
    http.expectOne('/api/admin/schemas/s1/visibility').flush({ ...SCHEMA, visibility: 'shared' });
    http.expectOne('/api/admin/schemas/s1').flush({ ...SCHEMA, visibility: 'shared' });
    http.expectOne('/api/admin/schemas/s1/usage').flush([]);
    expect(c.current()!.visibility).toBe('shared');
  });
});
