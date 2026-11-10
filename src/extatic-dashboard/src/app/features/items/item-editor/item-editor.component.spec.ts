import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ItemEditorComponent } from './item-editor.component';
import { App } from '../../../core/models/app.model';
import { Collection } from '../../../core/models/collection.model';
import { Item } from '../../../core/models/item.model';

const app: App = {
  id: 'a', slug: 'my-app', name: 'App', public_id: 'public', allowed_origins: [],
  max_file_size_mb: 10, max_attachments_per_item: 10, storage_quota_gb: 1,
  created_at: '', updated_at: '', can_manage_items: true,
};
const collection: Collection = {
  id: 'c', app_id: 'a', name: 'Posts', slug: 'posts',
  schema: JSON.stringify({ type: 'object', properties: { title: { type: 'string' }, tags: { type: 'array', items: { type: 'string' } } } }),
  attachments_enabled: false, allowed_attachment_types: [], created_at: '', updated_at: '',
};
const item: Item = { id: 'i', collection_id: 'c', app_user_id: null, data: { title: 'Original', extra: 7 }, version: 2, created_at: '', updated_at: '', attachments: [] };

describe('ItemEditorComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ItemEditorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open(existing = false, member = app, selectedCollection = collection) {
    const fixture = TestBed.createComponent(ItemEditorComponent);
    fixture.componentRef.setInput('appSlug', 'my-app');
    fixture.componentRef.setInput('collectionSlug', 'posts');
    if (existing) fixture.componentRef.setInput('itemId', 'i');
    fixture.detectChanges();
    http.expectOne('/api/apps/my-app').flush(member);
    http.expectOne('/api/apps/my-app/collections/posts').flush(selectedCollection);
    if (existing) http.expectOne('/api/apps/my-app/collections/posts/items/i').flush(item);
    fixture.detectChanges();
    return fixture;
  }

  it('keeps unmodeled data while changing modes and saves with the current version', () => {
    const fixture = open(true);
    const editor = fixture.componentInstance;
    expect(editor.mode()).toBe('form');
    editor.switchMode('raw');
    expect(editor.raw()).toContain('"extra": 7');
    editor.switchMode('form');
    expect(editor.draft()['extra']).toBe(7);
    editor.save();
    const request = http.expectOne('/api/apps/my-app/collections/posts/items/i');
    expect(request.request.body.version).toBe(2);
    request.flush({ ...item, version: 3 });
    expect(editor.saved()?.version).toBe(3);
  });

  it('accepts changes from JSON Forms and retains extra properties in raw mode', () => {
    const fixture = open(true);
    const editor = fixture.componentInstance;
    const title = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    title.value = 'Edited in form';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(editor.draft()['title']).toBe('Edited in form');
    expect(editor.draft()['extra']).toBe(7);
    editor.switchMode('raw');
    expect(editor.raw()).toContain('Edited in form');
    expect(editor.raw()).toContain('"extra": 7');
  });

  it('rejects malformed raw JSON and keeps a draft after a conflict', () => {
    const fixture = open(true);
    const editor = fixture.componentInstance;
    editor.switchMode('raw');
    editor.raw.set('{bad');
    editor.save();
    expect(editor.inputError()).toBe('Enter valid JSON.');
    http.expectNone('/api/apps/my-app/collections/posts/items/i');
    editor.raw.set('{"title":"Changed","extra":7}');
    editor.save();
    http.expectOne('/api/apps/my-app/collections/posts/items/i').flush({ title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
    expect(editor.conflict()).toBe(true);
    expect(editor.raw()).toContain('Changed');
  });

  it('shows a viewer the Item without a save action', () => {
    const fixture = open(true, { ...app, can_manage_items: false });
    expect(fixture.componentInstance.mode()).toBe('raw');
    expect(fixture.nativeElement.textContent).toContain('cannot change it');
    expect(fixture.nativeElement.textContent).not.toContain('Save Item');
  });

  it('shows schema pointers and keeps the draft after validation and network failures', () => {
    const fixture = open();
    const editor = fixture.componentInstance;
    editor.onFormChange({ title: 'Draft' });
    editor.save();
    http.expectOne('/api/apps/my-app/collections/posts/items').flush({
      detail: 'Invalid', errors: { errors: [{ pointer: '/title', message: 'Too short' }] },
    }, { status: 422, statusText: 'Validation Failed' });
    expect(editor.fieldErrors()).toEqual([{ pointer: '/title', message: 'Too short' }]);
    expect(editor.draft()['title']).toBe('Draft');
    editor.save();
    http.expectOne('/api/apps/my-app/collections/posts/items').flush({}, { status: 500, statusText: 'Error' });
    expect(editor.saved()).toBeNull();
    expect(editor.draft()['title']).toBe('Draft');
  });

  it('offers only raw JSON without a schema and rejects non-object input', () => {
    const fixture = open(false, app, { ...collection, schema: null });
    const editor = fixture.componentInstance;
    expect(editor.mode()).toBe('raw');
    expect(fixture.nativeElement.textContent).toContain('no JSON Schema');
    editor.raw.set('[]');
    editor.save();
    expect(editor.inputError()).toContain('JSON object');
    http.expectNone('/api/apps/my-app/collections/posts/items');
  });

  it('shows creation feedback and opens the saved Item view', () => {
    const fixture = open();
    const editor = fixture.componentInstance;
    editor.onFormChange({ title: 'Created' });
    editor.save();
    const request = http.expectOne('/api/apps/my-app/collections/posts/items');
    expect(request.request.body.data).toEqual({ title: 'Created' });
    request.flush({ ...item, id: 'created-1', data: { title: 'Created' }, version: 1 });
    expect(editor.saved()?.id).toBe('created-1');
    expect(editor.itemId).toBe('created-1');
  });

  it('keeps a draft when write permission changes after loading', () => {
    const fixture = open(true);
    const editor = fixture.componentInstance;
    editor.onFormChange({ title: 'Unsaved' });
    editor.save();
    http.expectOne('/api/apps/my-app/collections/posts/items/i').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(editor.saveError()).toContain('no longer allows');
    expect(editor.draft()['title']).toBe('Unsaved');
  });

  it('loads a draft 2020-12 Collection schema in form mode', () => {
    const fixture = open(false, app, {
      ...collection,
      schema: JSON.stringify({
        $schema: 'https://json-schema.org/draft/2020-12/schema',
        type: 'object',
        properties: { title: { type: 'string', minLength: 3 } },
        required: ['title'],
      }),
    });
    expect(fixture.componentInstance.mode()).toBe('form');
    expect(fixture.nativeElement.querySelector('input[type="text"]')).not.toBeNull();
  });
});
