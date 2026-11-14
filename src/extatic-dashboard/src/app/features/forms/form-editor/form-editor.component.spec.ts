import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { FormEditorComponent } from './form-editor.component';
import { Form, embedSnippets } from '../forms.model';

const existing: Form = {
  id: 'f1', name: 'Contact', slug: 'contact', schema: { type: 'object' }, notification_emails: ['a@example.com'],
  success_redirect_url: 'https://site.example.com/thanks', honeypot_field: 'website', is_active: true,
  store_submissions: true, require_app_user: false, submission_count: 0, unread_count: 0, spam_count: 0,
  created_at: '', updated_at: '',
};

describe('FormEditorComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FormEditorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open(id?: string) {
    const fixture = TestBed.createComponent(FormEditorComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    if (id) fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
    return fixture;
  }

  it('creates a form with parsed schema and emails', () => {
    const fixture = open();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const c = fixture.componentInstance;
    c.form.patchValue({ name: 'Contact Us', schema: '{"type":"object"}', notification_emails: 'a@example.com, b@example.com' });
    c.autoSlug();
    c.onSubmit();

    const req = http.expectOne('/api/apps/app/forms');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'Contact Us', slug: 'contact-us', schema: { type: 'object' },
      notification_emails: ['a@example.com', 'b@example.com'], success_redirect_url: null, honeypot_field: null,
      is_active: true, store_submissions: true, require_app_user: false,
    });
    req.flush(existing);
    expect(navigate).toHaveBeenCalledWith(['/apps', 'app', 'forms']);
  });

  it('blocks invalid schema JSON and invalid emails client-side', () => {
    const fixture = open();
    const c = fixture.componentInstance;
    c.form.patchValue({ name: 'X', slug: 'x', schema: '{nope', notification_emails: 'not-an-email' });
    c.onSubmit();
    http.expectNone('/api/apps/app/forms');
    expect(c.schemaError()).toBe('Invalid JSON.');
    expect(c.emailsError()).toContain('not-an-email');

    c.form.patchValue({ schema: '[1]', notification_emails: '' });
    c.onSubmit();
    http.expectNone('/api/apps/app/forms');
    expect(c.schemaError()).toBe('Schema must be a JSON object.');
  });

  it('shows server field errors on 422', () => {
    const fixture = open();
    const c = fixture.componentInstance;
    c.form.patchValue({ name: 'X', slug: 'x', success_redirect_url: 'http://evil.example' });
    c.onSubmit();
    http.expectOne('/api/apps/app/forms').flush(
      { errors: [{ pointer: 'success_redirect_url', message: 'Must be an absolute https:// URL' }] },
      { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('success_redirect_url: Must be an absolute https:// URL');
    expect(c.saving()).toBe(false);
  });

  it('loads an existing form, shows the embed panel and saves with PUT', () => {
    const fixture = open('f1');
    http.expectOne('/api/apps/app').flush({ public_id: 'app_pub123' });
    http.expectOne('/api/apps/app/forms/f1').flush(existing);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.form.getRawValue().notification_emails).toBe('a@example.com');
    expect(JSON.parse(c.form.getRawValue().schema!)).toEqual({ type: 'object' });
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('/api/app_pub123/forms/contact/submissions');
    expect(text).toContain('name="website"');

    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    c.onSubmit();
    const req = http.expectOne('/api/apps/app/forms/f1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.honeypot_field).toBe('website');
    req.flush(existing);
  });
});

describe('embedSnippets', () => {
  it('builds the action URL, HTML form and fetch snippet', () => {
    const s = embedSnippets('https://api.example.com/api', 'app_x', { slug: 'contact', honeypot_field: null });
    expect(s.action).toBe('https://api.example.com/api/app_x/forms/contact/submissions');
    expect(s.html).toContain(`action="${s.action}"`);
    expect(s.html).not.toContain('Spam trap');
    expect(s.js).toContain(`fetch('${s.action}'`);
  });
});
