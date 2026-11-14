import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { AuthSettingsComponent } from './auth-settings.component';
import { AuthSettings, parseDomains, urlTemplateError } from '../auth-settings.model';
import { buildIntegrationSnippets } from '../integration-snippets';

const settings: AuthSettings = {
  registration_enabled: true,
  require_email_verification: true,
  password_min_length: 10,
  access_token_minutes: 60,
  refresh_token_days: 30,
  max_failed_attempts: 5,
  lockout_minutes: 15,
  verify_url_template: null,
  reset_url_template: 'https://site.example.com/reset?token={token}',
  allowed_email_domains: ['example.com'],
  updated_at: null,
};

describe('AuthSettingsComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [AuthSettingsComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  function open() {
    const fixture = TestBed.createComponent(AuthSettingsComponent);
    fixture.componentRef.setInput('appSlug', 'blog');
    fixture.detectChanges();
    http.expectOne('/api/apps/blog/auth/settings').flush(settings);
    http.expectOne('/api/apps/blog').flush({ public_id: 'app_abc123' });
    fixture.detectChanges();
    return fixture;
  }

  it('loads settings and renders integration snippets with the public id', () => {
    const fixture = open();
    const c = fixture.componentInstance;
    expect(c.form.value.allowed_email_domains).toBe('example.com');
    expect(c.form.value.reset_url_template).toBe('https://site.example.com/reset?token={token}');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Integration');
    expect(text).toContain('/api/app_abc123/auth/login');
    expect(text).toContain('/api/app_abc123/auth/refresh');
  });

  it('blocks saving an invalid URL template or out-of-range number', () => {
    const fixture = open();
    const c = fixture.componentInstance;
    c.form.controls.verify_url_template.setValue('http://site.example.com/verify?t={token}');
    c.save();
    http.expectNone('/api/apps/blog/auth/settings');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('https');

    c.form.controls.verify_url_template.setValue('');
    c.form.controls.password_min_length.setValue(4);
    c.save();
    http.expectNone('/api/apps/blog/auth/settings');
  });

  it('PUTs the normalised request and shows server validation errors by field', () => {
    const fixture = open();
    const c = fixture.componentInstance;
    c.form.controls.allowed_email_domains.setValue('@Example.com,  partner.org\nexample.com');
    c.form.controls.registration_enabled.setValue(false);
    c.save();
    const req = http.expectOne('/api/apps/blog/auth/settings');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      ...settings,
      updated_at: undefined,
      registration_enabled: false,
      allowed_email_domains: ['example.com', 'partner.org'],
    });
    req.flush(
      { errors: { errors: [{ pointer: 'reset_url_template', message: "Must be on one of the App's allowed origins" }] } },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    fixture.detectChanges();
    expect(c.serverError('reset_url_template')).toContain('allowed origins');
    expect(fixture.nativeElement.textContent).toContain('allowed origins');
  });

  it('explains a 403 for non-admins', () => {
    const fixture = TestBed.createComponent(AuthSettingsComponent);
    fixture.componentRef.setInput('appSlug', 'blog');
    fixture.detectChanges();
    http.expectOne('/api/apps/blog/auth/settings').flush({}, { status: 403, statusText: 'Forbidden' });
    http.expectOne('/api/apps/blog').flush({ public_id: 'app_x' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('owner or an admin');
  });
});

describe('auth settings helpers', () => {
  it('validates URL templates like the API', () => {
    expect(urlTemplateError('')).toBeNull();
    expect(urlTemplateError('https://a.example.com/v?t={token}')).toBeNull();
    expect(urlTemplateError('http://localhost:4200/v#{token}')).toBeNull();
    expect(urlTemplateError('https://a.example.com/v')).toContain('{token}');
    expect(urlTemplateError('http://a.example.com/{token}')).toContain('https');
    expect(urlTemplateError('/relative/{token}')).toContain('absolute');
    expect(urlTemplateError('https://u:p@a.example.com/{token}')).toContain('absolute');
  });

  it('parses domain lists', () => {
    expect(parseDomains(' A.com, @b.org\n a.com ')).toEqual(['a.com', 'b.org']);
    expect(parseDomains('')).toEqual([]);
  });

  it('builds snippets against the given base', () => {
    const snippets = buildIntegrationSnippets('https://api.example.com/api/', 'app_1');
    expect(snippets.map(s => s.title)).toContain('Log in');
    expect(snippets.every(s => s.code.includes('https://api.example.com/api/app_1/auth/'))).toBe(true);
  });
});
