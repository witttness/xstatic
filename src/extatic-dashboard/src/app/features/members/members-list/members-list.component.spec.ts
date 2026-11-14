import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MembersListComponent } from './members-list.component';
import { MembershipSummary } from '../members.model';

const member = (overrides: Partial<MembershipSummary> = {}): MembershipSummary => ({
  id: 'm1', app_user_id: 'u1', display_name: 'Ada Lovelace', email: 'ada@example.com', plan_id: 'p1',
  plan_slug: 'standard', plan_name: 'Standard', status: 'pending', member_number: null, starts_at: null,
  expires_at: null, approved_at: null, cancelled_at: null, show_in_directory: false,
  created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-01T00:00:00Z', ...overrides,
});

const stats = {
  total: 1, by_status: { pending: 1, active: 0, suspended: 0, expired: 0, cancelled: 0, rejected: 0 },
  by_plan: [], expiring_soon: 0,
};

describe('MembersListComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MembersListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function expectList(params: Record<string, string>): TestRequest {
    return http.expectOne(r => {
      if (r.url !== '/api/apps/app/members') return false;
      const all = { page: '1', page_size: '20', ...params };
      return Object.entries(all).every(([k, v]) => r.params.get(k) === v)
        && r.params.keys().length === Object.keys(all).length;
    });
  }

  function open() {
    const fixture = TestBed.createComponent(MembersListComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    http.expectOne('/api/apps/app/members/plans').flush([{ id: 'p1', name: 'Standard' }]);
    http.expectOne('/api/apps/app/members/stats').flush(stats);
    return fixture;
  }

  it('renders members with status-specific actions and stats', () => {
    const fixture = open();
    expectList({}).flush({ items: [member(), member({ id: 'm2', status: 'active', display_name: 'Grace', member_number: 'M-00001' })], total: 2, page: 1, page_size: 20 });
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('M-00001');
    expect(text).toContain('Approve');
    expect(text).toContain('Suspend');
    expect(text).toContain('Expiring in 30 days');
  });

  it('filters by status tab, plan and search', () => {
    const fixture = open();
    expectList({}).flush({ items: [], total: 0, page: 1, page_size: 20 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No members found');

    const tabs = Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]')) as HTMLButtonElement[];
    tabs.find(t => t.textContent?.trim() === 'Pending')!.click();
    expectList({ status: 'pending' }).flush({ items: [member()], total: 1, page: 1, page_size: 20 });

    const list = fixture.componentInstance;
    list.planId.set('p1');
    list.search.set(' ada ');
    list.go(1);
    expectList({ status: 'pending', plan_id: 'p1', q: 'ada' }).flush({ items: [], total: 0, page: 1, page_size: 20 });
  });

  it('runs a row action then reloads the list and stats', () => {
    const fixture = open();
    expectList({}).flush({ items: [member()], total: 1, page: 1, page_size: 20 });
    fixture.detectChanges();

    const approve = (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
      .find(b => b.textContent?.trim() === 'Approve')!;
    approve.click();
    const req = http.expectOne('/api/apps/app/members/m1/approve');
    expect(req.request.method).toBe('POST');
    req.flush({});
    expectList({}).flush({ items: [member({ status: 'active' })], total: 1, page: 1, page_size: 20 });
    http.expectOne('/api/apps/app/members/stats').flush(stats);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Suspend');
  });

  it('shows the API error when an action is refused', () => {
    const fixture = open();
    expectList({}).flush({ items: [member({ status: 'active' })], total: 1, page: 1, page_size: 20 });
    fixture.componentInstance.act(member({ status: 'active' }), 'renew');
    http.expectOne('/api/apps/app/members/m1/renew')
      .flush({ detail: 'This plan does not expire' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('This plan does not expire');
  });
});
