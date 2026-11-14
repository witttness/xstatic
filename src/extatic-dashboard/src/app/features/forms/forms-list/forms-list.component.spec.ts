import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { FormsListComponent } from './forms-list.component';
import { Form } from '../forms.model';

const form = (over: Partial<Form> = {}): Form => ({
  id: 'f1', name: 'Contact', slug: 'contact', schema: null, notification_emails: [],
  success_redirect_url: null, honeypot_field: null, is_active: true, store_submissions: true,
  require_app_user: false, submission_count: 12, unread_count: 3, spam_count: 0,
  created_at: '', updated_at: '', ...over,
});

describe('FormsListComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FormsListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open() {
    const fixture = TestBed.createComponent(FormsListComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    return fixture;
  }

  it('lists forms with submission counts, unread badge and active state', () => {
    const fixture = open();
    http.expectOne('/api/apps/app/forms').flush([
      form(),
      form({ id: 'f2', name: 'Signup', slug: 'signup', is_active: false, submission_count: 0, unread_count: 0 }),
    ]);
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Contact');
    expect(text).toContain('signup');
    expect(text).toContain('3 unread');
    expect(text).toContain('Active');
    expect(text).toContain('Inactive');
    expect(text.match(/unread/g)?.length).toBe(1);
  });

  it('shows empty and error states', () => {
    const fixture = open();
    http.expectOne('/api/apps/app/forms').flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No forms yet.');
    fixture.componentInstance.load();
    http.expectOne('/api/apps/app/forms').flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Failed to load forms.');
  });

  it('deletes a form after confirmation', () => {
    const fixture = open();
    http.expectOne('/api/apps/app/forms').flush([form()]);
    fixture.componentInstance.deleting.set(fixture.componentInstance.forms()[0]);
    fixture.componentInstance.onDelete();
    const req = http.expectOne('/api/apps/app/forms/f1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    expect(fixture.componentInstance.forms()).toEqual([]);
  });
});
