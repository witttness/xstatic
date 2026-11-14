import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CommentsSettingsComponent } from './comments-settings.component';
import { CommentSettings } from '../comments.model';

const defaults: CommentSettings = {
  enabled: false, moderation_mode: 'all', allow_anonymous: true, require_email_for_anonymous: true,
  max_length: 5000, max_depth: 3, auto_close_after_days: null, blocked_terms: ['casino'],
  blocked_term_action: 'pending', max_links: 2, notify_emails: [], expose_avatar_hash: false, updated_at: null,
};

describe('CommentsSettingsComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CommentsSettingsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open() {
    const fixture = TestBed.createComponent(CommentsSettingsComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    http.expectOne('/api/apps/app/comments/settings').flush(defaults);
    fixture.detectChanges();
    return fixture;
  }

  it('loads settings and saves the full object with parsed lists', () => {
    const fixture = open();
    const c = fixture.componentInstance;
    expect(c.blockedTermsText).toBe('casino');
    c.model!.enabled = true;
    c.model!.moderation_mode = 'anonymous_only';
    c.notifyEmailsText = 'a@example.com\n b@example.com ,\n';
    c.blockedTermsText = 'casino\n\nviagra';
    c.save();
    const req = http.expectOne('/api/apps/app/comments/settings');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.enabled).toBe(true);
    expect(req.request.body.moderation_mode).toBe('anonymous_only');
    expect(req.request.body.notify_emails).toEqual(['a@example.com', 'b@example.com']);
    expect(req.request.body.blocked_terms).toEqual(['casino', 'viagra']);
    expect(req.request.body.auto_close_after_days).toBeNull();
    expect('updated_at' in req.request.body).toBe(false);
    req.flush({ ...req.request.body, updated_at: '2026-01-01T00:00:00Z' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Settings saved');
  });

  it('shows field errors from a 422', () => {
    const fixture = open();
    fixture.componentInstance.notifyEmailsText = 'nope';
    fixture.componentInstance.save();
    http.expectOne('/api/apps/app/comments/settings').flush(
      { errors: { errors: [{ pointer: 'notify_emails/0', message: 'Must be a valid email address' }] } },
      { status: 422, statusText: 'Unprocessable Entity' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('notify_emails/0: Must be a valid email address');
  });

  it('explains a 403', () => {
    const fixture = TestBed.createComponent(CommentsSettingsComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    http.expectOne('/api/apps/app/comments/settings').flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Only owners and admins');
  });
});
