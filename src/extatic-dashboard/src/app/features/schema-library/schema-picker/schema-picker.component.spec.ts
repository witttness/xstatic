import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PickedSchema, SchemaPickerComponent } from './schema-picker.component';
import { lineDiff } from '../line-diff';
import { canonicalJson, sameJson } from '../schema-library.models';

const AVAILABLE = [
  { id: 'g1', scope: 'global', name: 'Blog Post', slug: 'post', description: 'Posts', category: null, tags: [], version: 3, admin_only: false },
  { id: 'g2', scope: 'global', name: 'Draft', slug: 'draft', description: null, category: null, tags: [], version: 1, admin_only: true },
  { id: 'a1', scope: 'app', name: 'Product', slug: 'product', description: null, category: null, tags: [], version: 2, admin_only: false },
];

describe('SchemaPickerComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SchemaPickerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open() {
    const fixture = TestBed.createComponent(SchemaPickerComponent);
    fixture.componentRef.setInput('appSlug', 'shop');
    fixture.detectChanges();
    http.expectOne('/api/apps/shop/schemas/available').flush({ items: AVAILABLE, total: 3, page: 1, page_size: 100 });
    fixture.detectChanges();
    return fixture;
  }

  it('groups shared and app schemas and flags admin-only ones', () => {
    const fixture = open();
    const text: string = fixture.nativeElement.textContent;
    expect(text.indexOf('Shared')).toBeLessThan(text.indexOf('Blog Post'));
    expect(text.indexOf('This app')).toBeLessThan(text.indexOf('Product'));
    expect(text).toContain('Admin only');
    expect(fixture.componentInstance.groups()[0].items.map(s => s.id)).toEqual(['g1', 'g2']);
    expect(fixture.componentInstance.groups()[1].items.map(s => s.id)).toEqual(['a1']);
  });

  it('searches with the query', () => {
    const fixture = open();
    fixture.componentInstance.q = ' blog ';
    fixture.componentInstance.search();
    http.expectOne('/api/apps/shop/schemas/available?q=blog').flush({ items: [AVAILABLE[0]], total: 1, page: 1, page_size: 100 });
    expect(fixture.componentInstance.schemas().length).toBe(1);
  });

  it('previews and emits the chosen schema as pretty JSON', () => {
    const fixture = open();
    let picked: PickedSchema | undefined;
    fixture.componentInstance.picked.subscribe(p => (picked = p));
    (fixture.nativeElement.querySelector('[data-schema-id="g1"]') as HTMLButtonElement).click();
    http.expectOne('/api/apps/shop/schemas/available/g1').flush({ ...AVAILABLE[0], schema: { type: 'object' } });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="preview"]').textContent).toContain('"type": "object"');
    fixture.componentInstance.use();
    expect(picked).toEqual({ id: 'g1', name: 'Blog Post', version: 3, schemaText: '{\n  "type": "object"\n}' });
  });
});

describe('schema library helpers', () => {
  it('lineDiff marks added and removed lines', () => {
    expect(lineDiff('a\nb\nc', 'a\nc\nd')).toEqual([
      { type: 'same', text: 'a' }, { type: 'del', text: 'b' }, { type: 'same', text: 'c' }, { type: 'add', text: 'd' },
    ]);
  });

  it('compares JSON ignoring key order and formatting', () => {
    expect(canonicalJson({ b: 1, a: { d: 2, c: 3 } })).toBe('{"a":{"c":3,"d":2},"b":1}');
    expect(sameJson('{ "b": 1, "a": 2 }', { a: 2, b: 1 })).toBe(true);
    expect(sameJson('{"a": 3}', { a: 2 })).toBe(false);
    expect(sameJson('not json', {})).toBe(false);
  });
});
