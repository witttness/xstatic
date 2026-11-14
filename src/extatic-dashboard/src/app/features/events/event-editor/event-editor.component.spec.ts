import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Component } from '@angular/core';

@Component({ standalone: true, template: '' })
class BlankComponent {}
import { EventEditorComponent, fromLocalInput, toLocalInput } from './event-editor.component';

describe('EventEditorComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventEditorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', component: BlankComponent }])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open(id?: string) {
    const fixture = TestBed.createComponent(EventEditorComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    if (id) fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
    return fixture;
  }

  function fill(editor: EventEditorComponent) {
    editor.form.patchValue({
      title: 'Summer Party',
      starts_at: '2026-07-01T18:00',
      ends_at: '2026-07-01T22:00',
      timezone: 'Europe/London',
      capacity: 50,
      waitlist_enabled: true,
      notify_emails: 'a@example.com, b@example.com',
      registration_schema: '{"type":"object"}',
    });
    editor.suggestSlug();
  }

  it('creates an event with a suggested slug and converted dates', () => {
    const fixture = open();
    const editor = fixture.componentInstance;
    fill(editor);
    expect(editor.form.value.slug).toBe('summer-party');
    editor.save();
    const req = http.expectOne('/api/apps/app/events');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.starts_at).toBe(new Date('2026-07-01T18:00').toISOString());
    expect(req.request.body.capacity).toBe(50);
    expect(req.request.body.notify_emails).toEqual(['a@example.com', 'b@example.com']);
    expect(req.request.body.registration_schema).toEqual({ type: 'object' });
    expect(req.request.body.online_url).toBeNull();
    req.flush({ id: 'new-id' });
  });

  it('rejects invalid schema JSON without calling the API', () => {
    const editor = open().componentInstance;
    fill(editor);
    editor.form.patchValue({ registration_schema: '{nope' });
    editor.save();
    expect(editor.schemaError()).toContain('not valid JSON');
    http.expectNone('/api/apps/app/events');
  });

  it('maps 422 field errors from the API', () => {
    const fixture = open();
    const editor = fixture.componentInstance;
    fill(editor);
    editor.save();
    http.expectOne('/api/apps/app/events').flush(
      { errors: [{ pointer: 'timezone', message: 'Must be a valid IANA time zone id' }, { pointer: 'notify_emails/1', message: 'Bad email' }] },
      { status: 422, statusText: 'Unprocessable' },
    );
    fixture.detectChanges();
    expect(editor.fieldError('timezone')).toContain('IANA');
    expect(editor.notifyError()).toBe('Bad email');
    expect(fixture.nativeElement.textContent).toContain('Please fix the highlighted fields.');
  });

  it('loads an existing event and saves with PUT', () => {
    const fixture = open('e1');
    http.expectOne('/api/apps/app/events/e1').flush({
      id: 'e1', title: 'Old', slug: 'old', description: null, location: 'Hall', online_url: null,
      starts_at: '2026-07-01T18:00:00Z', ends_at: '2026-07-01T20:00:00Z', timezone: 'UTC',
      capacity: null, waitlist_enabled: false, waitlist_capacity: null, max_seats_per_registration: 2,
      registration_opens_at: null, registration_closes_at: null, registration_schema: { type: 'object' },
      require_app_user: true, manage_url_template: 'https://s.example/m?t={token}', notify_emails: ['x@example.com'],
      status: 'draft',
    });
    const editor = fixture.componentInstance;
    expect(editor.form.value.slug).toBe('old');
    expect(editor.form.value.notify_emails).toBe('x@example.com');
    editor.form.patchValue({ title: 'New title' });
    editor.suggestSlug();
    expect(editor.form.value.slug).toBe('old'); // never auto-renamed when editing
    editor.save();
    const req = http.expectOne('/api/apps/app/events/e1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.capacity).toBeNull();
    expect(req.request.body.max_seats_per_registration).toBe(2);
    expect(req.request.body.require_app_user).toBe(true);
    expect(req.request.body.starts_at).toBe('2026-07-01T18:00:00.000Z');
    req.flush({ id: 'e1' });
  });

  it('round-trips local datetime values', () => {
    expect(fromLocalInput(toLocalInput('2026-03-04T05:06:00Z'))).toBe('2026-03-04T05:06:00.000Z');
    expect(fromLocalInput('')).toBeNull();
    expect(toLocalInput(null)).toBe('');
  });
});
