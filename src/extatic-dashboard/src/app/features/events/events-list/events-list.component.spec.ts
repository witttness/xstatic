import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { EventsListComponent } from './events-list.component';
import { ExtaticEvent, seatSummary } from '../event.model';

function makeEvent(overrides: Partial<ExtaticEvent> = {}): ExtaticEvent {
  return {
    id: 'e1', title: 'Meetup', slug: 'meetup', description: null, location: null, online_url: null,
    starts_at: '2026-12-01T18:00:00Z', ends_at: '2026-12-01T20:00:00Z', timezone: 'UTC',
    capacity: 50, waitlist_enabled: true, waitlist_capacity: null, max_seats_per_registration: 1,
    registration_opens_at: null, registration_closes_at: null, registration_schema: null,
    require_app_user: false, manage_url_template: null, notify_emails: [], status: 'published',
    stats: {
      confirmed_registrations: 40, confirmed_seats: 42, waitlisted_registrations: 6, waitlisted_seats: 6,
      cancelled_registrations: 1, checked_in_registrations: 0, checked_in_seats: 0, capacity: 50, seats_remaining: 8,
    },
    registration_open: true, registration_closed_reason: null, created_at: '', updated_at: '',
    ...overrides,
  };
}

describe('EventsListComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventsListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open() {
    const fixture = TestBed.createComponent(EventsListComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    return fixture;
  }

  it('shows status and seat summary for each event', () => {
    const fixture = open();
    http.expectOne('/api/apps/app/events?page=1&page_size=20')
      .flush({ items: [makeEvent()], total: 1, page: 1, page_size: 20 });
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Meetup');
    expect(text).toContain('published');
    expect(text).toContain('42 / 50 seats · 6 waitlisted');
  });

  it('shows empty and error states', () => {
    const fixture = open();
    http.expectOne('/api/apps/app/events?page=1&page_size=20').flush({ items: [], total: 0, page: 1, page_size: 20 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No events yet.');
    fixture.componentInstance.load();
    http.expectOne('/api/apps/app/events?page=1&page_size=20').flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Failed to load events.');
  });

  it('filters by status and resets to page 1', () => {
    const fixture = open();
    http.expectOne('/api/apps/app/events?page=1&page_size=20').flush({ items: [], total: 45, page: 1, page_size: 20 });
    fixture.componentInstance.go(2);
    http.expectOne('/api/apps/app/events?page=2&page_size=20').flush({ items: [], total: 45, page: 2, page_size: 20 });
    fixture.componentInstance.setStatus('draft');
    http.expectOne('/api/apps/app/events?status=draft&page=1&page_size=20').flush({ items: [], total: 0, page: 1, page_size: 20 });
    expect(fixture.componentInstance.page()).toBe(1);
  });

  it('formats unlimited capacity without a waitlist suffix', () => {
    const ev = makeEvent({ capacity: null });
    ev.stats.waitlisted_registrations = 0;
    expect(seatSummary(ev)).toBe('42 seats');
  });
});
