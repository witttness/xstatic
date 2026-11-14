import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  CollectionConfigEditorComponent,
  isValidPointer,
  isValidUrlTemplate,
  parsePointerList,
  toConfigRequest,
} from './collection-config-editor.component';

const baseValue = {
  enabled: true,
  title_pointer: '/title',
  body_pointers: '/body\n/paragraphs, /extra',
  tags_pointer: '',
  url_template: '/blog/{slug}',
  section: ' blog ',
  language: '',
};

describe('config helpers', () => {
  it('parses pointer lists', () => {
    expect(parsePointerList(' /a \n\n/b,/c ,')).toEqual(['/a', '/b', '/c']);
    expect(parsePointerList(null)).toEqual([]);
  });

  it('validates pointers and url templates', () => {
    expect(isValidPointer('/title')).toBe(true);
    expect(isValidPointer('')).toBe(true);
    expect(isValidPointer('title')).toBe(false);
    expect(isValidUrlTemplate('/blog/{slug}')).toBe(true);
    expect(isValidUrlTemplate('https://example.com/{id}')).toBe(true);
    expect(isValidUrlTemplate('javascript:alert({id})')).toBe(false);
    expect(isValidUrlTemplate('//evil.example/{id}')).toBe(false);
    expect(isValidUrlTemplate('/blog/{sl ug}')).toBe(false);
  });

  it('builds the request body', () => {
    expect(toConfigRequest(baseValue)).toEqual({
      request: {
        enabled: true,
        title_pointer: '/title',
        body_pointers: ['/body', '/paragraphs', '/extra'],
        tags_pointer: null,
        url_template: '/blog/{slug}',
        section: 'blog',
        language: null,
      },
    });
  });

  it('reports field errors', () => {
    const result = toConfigRequest({ ...baseValue, title_pointer: 'title', body_pointers: 'body', url_template: 'ftp://x' });
    expect(result).toEqual({
      errors: {
        title_pointer: expect.any(String),
        body_pointers: expect.any(String),
        url_template: expect.any(String),
      },
    });
  });
});

describe('CollectionConfigEditorComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      imports: [CollectionConfigEditorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function open() {
    const fixture = TestBed.createComponent(CollectionConfigEditorComponent);
    fixture.componentRef.setInput('appSlug', 'site');
    fixture.detectChanges();
    http.expectOne('/api/apps/site/collections').flush([
      { id: 'c1', slug: 'posts', name: 'Posts' },
      { id: 'c2', slug: 'pages', name: 'Pages' },
    ]);
    http.expectOne('/api/apps/site/search/collections').flush([
      {
        collection_id: 'c1', collection_slug: 'posts', collection_name: 'Posts', enabled: true,
        title_pointer: '/headline', body_pointers: ['/body', '/summary'], tags_pointer: '/tags',
        url_template: '/p/{slug}', section: 'blog', language: 'english', document_count: 4, updated_at: '',
      },
    ]);
    fixture.detectChanges();
    return fixture;
  }

  it('loads an existing config into the form', () => {
    const fixture = open();
    expect(fixture.nativeElement.textContent).toContain('Indexed · 4 docs');
    expect(fixture.nativeElement.textContent).toContain('Not indexed');
    fixture.componentInstance.select('posts');
    fixture.detectChanges();
    const form = fixture.componentInstance.form.getRawValue();
    expect(form.title_pointer).toBe('/headline');
    expect(form.body_pointers).toBe('/body\n/summary');
    expect(form.url_template).toBe('/p/{slug}');
  });

  it('does not submit invalid pointers', () => {
    const fixture = open();
    fixture.componentInstance.select('pages');
    fixture.componentInstance.form.patchValue({ title_pointer: 'oops' });
    fixture.componentInstance.save();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('JSON pointer such as /title');
    http.expectNone('/api/apps/site/search/collections/pages');
  });

  it('saves, then polls the reindex job until it completes', () => {
    const fixture = open();
    const component = fixture.componentInstance;
    component.select('pages');
    component.form.patchValue({ body_pointers: '/content', section: 'docs' });
    component.save();

    const put = http.expectOne('/api/apps/site/search/collections/pages');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({
      enabled: true, title_pointer: '/title', body_pointers: ['/content'], tags_pointer: null,
      url_template: null, section: 'docs', language: null,
    });
    put.flush({
      config: {
        collection_id: 'c2', collection_slug: 'pages', collection_name: 'Pages', enabled: true,
        title_pointer: '/title', body_pointers: ['/content'], tags_pointer: null, url_template: null,
        section: 'docs', language: 'english', document_count: 0, updated_at: '',
      },
      reindex_job: { id: 'j1', collection_id: 'c2', status: 'pending', processed: 0, total: 0, error: null, created_at: '', started_at: null, completed_at: null },
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('pending');

    vi.advanceTimersByTime(1500);
    http.expectOne('/api/apps/site/search/reindex/j1').flush({
      id: 'j1', collection_id: 'c2', status: 'running', processed: 1, total: 3, error: null, created_at: '', started_at: '', completed_at: null,
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('1 / 3');

    vi.advanceTimersByTime(1500);
    http.expectOne('/api/apps/site/search/reindex/j1').flush({
      id: 'j1', collection_id: 'c2', status: 'completed', processed: 3, total: 3, error: null, created_at: '', started_at: '', completed_at: '',
    });
    http.expectOne('/api/apps/site/search/collections').flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('completed');
    vi.advanceTimersByTime(5000);
    http.expectNone('/api/apps/site/search/reindex/j1');
  });

  it('explains 403 to viewers', () => {
    const fixture = TestBed.createComponent(CollectionConfigEditorComponent);
    fixture.componentRef.setInput('appSlug', 'site');
    fixture.detectChanges();
    http.expectOne('/api/apps/site/collections').flush([]);
    http.expectOne('/api/apps/site/search/collections').flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Only owners and admins');
  });
});
