import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CommentsQueueComponent } from './comments-queue.component';
import { AdminComment } from '../comments.model';

const comment = (id: string, body: string, status: AdminComment['status'] = 'pending'): AdminComment => ({
  id, body, status, page_key: '/blog/hello', page_url: null, page_title: 'Hello', parent_id: null, depth: 1,
  app_user_id: null, author_name: 'Reader', author_email: 'reader@example.com', is_deleted: false,
  user_agent: null, created_at: '2026-01-01T00:00:00Z', edited_at: null, moderated_at: null, moderated_by_user_id: null,
});

const stats = { pending: 2, approved: 1, rejected: 0, spam: 0 };

describe('CommentsQueueComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CommentsQueueComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open(items: AdminComment[]) {
    const fixture = TestBed.createComponent(CommentsQueueComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    http.expectOne('/api/apps/app/comments?status=pending&page=1&page_size=20')
      .flush({ items, total: items.length, page: 1, page_size: 20 });
    http.expectOne('/api/apps/app/comments/stats').flush(stats);
    fixture.detectChanges();
    return fixture;
  }

  it('loads the pending tab and renders bodies as text, never HTML', () => {
    const fixture = open([comment('1', '<img src=x onerror=alert(1)><b>bold</b>')]);
    const el: HTMLElement = fixture.nativeElement;
    const body = el.querySelector('[data-testid="comment-body"]')!;
    expect(body.textContent).toContain('<b>bold</b>');
    expect(body.querySelector('img')).toBeNull();
    expect(body.querySelector('b')).toBeNull();
    expect(el.textContent).toContain('reader@example.com');
    expect(el.textContent).toContain('Hello');
  });

  it('switches tabs', () => {
    const fixture = open([]);
    expect(fixture.nativeElement.textContent).toContain('No pending comments');
    fixture.componentInstance.setStatus('spam');
    http.expectOne('/api/apps/app/comments?status=spam&page=1&page_size=20').flush({ items: [], total: 0, page: 1, page_size: 20 });
    http.expectOne('/api/apps/app/comments/stats').flush(stats);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No spam comments');
  });

  it('bulk approves the selected comments', () => {
    const fixture = open([comment('1', 'one'), comment('2', 'two')]);
    fixture.componentInstance.toggleAll(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="bulk-bar"]').textContent).toContain('2 selected');
    fixture.componentInstance.bulk('approve');
    const req = http.expectOne('/api/apps/app/comments/bulk');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ ids: ['1', '2'], action: 'approve' });
    req.flush({ updated: 2, not_found: 0 });
    http.expectOne('/api/apps/app/comments?status=pending&page=1&page_size=20').flush({ items: [], total: 0, page: 1, page_size: 20 });
    http.expectOne('/api/apps/app/comments/stats').flush(stats);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('2 comment(s) approved');
  });

  it('moderates a single comment', () => {
    const fixture = open([comment('1', 'one')]);
    fixture.componentInstance.moderate(fixture.componentInstance.comments()[0], 'spam');
    const req = http.expectOne('/api/apps/app/comments/1/spam');
    expect(req.request.method).toBe('POST');
    req.flush(comment('1', 'one', 'spam'));
    http.expectOne('/api/apps/app/comments?status=pending&page=1&page_size=20').flush({ items: [], total: 0, page: 1, page_size: 20 });
    http.expectOne('/api/apps/app/comments/stats').flush(stats);
  });

  it('shows a permission error', () => {
    const fixture = TestBed.createComponent(CommentsQueueComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    http.expectOne('/api/apps/app/comments?status=pending&page=1&page_size=20').flush({}, { status: 403, statusText: 'Forbidden' });
    http.expectOne('/api/apps/app/comments/stats').flush(stats);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('do not have permission');
  });
});
