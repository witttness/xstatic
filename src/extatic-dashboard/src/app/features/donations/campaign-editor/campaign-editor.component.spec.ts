import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CampaignEditorComponent } from './campaign-editor.component';
import { Campaign } from '../donations.models';

describe('CampaignEditorComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CampaignEditorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function create(id?: string) {
    const fixture = TestBed.createComponent(CampaignEditorComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    if (id) fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
    return fixture;
  }

  function fill(c: CampaignEditorComponent, values: Partial<ReturnType<typeof c.form.getRawValue>>) {
    c.form.patchValue({
      name: 'General Fund', slug: 'general', success_url: 'https://example.org/thanks',
      cancel_url: 'https://example.org/donate', ...values,
    });
  }

  it('converts two-decimal major amounts to exact minor units', () => {
    const c = create().componentInstance;
    fill(c, { currency: 'usd', goal: '1000.50', suggested: '5, 19.99, 0.29', min: '1', max: '250.5' });
    const req = c.buildRequest()!;
    expect(req.goal_amount_minor).toBe(100050);
    expect(req.suggested_amounts_minor).toEqual([500, 1999, 29]);
    expect(req.min_amount_minor).toBe(100);
    expect(req.max_amount_minor).toBe(25050);
    expect(req.currency).toBe('usd');
  });

  it('treats zero-decimal currencies as whole units and rejects decimals', () => {
    const c = create().componentInstance;
    fill(c, { currency: 'jpy', goal: '100000', suggested: '500, 1000', min: '100', max: '50000' });
    const req = c.buildRequest()!;
    expect(req.goal_amount_minor).toBe(100000);
    expect(req.suggested_amounts_minor).toEqual([500, 1000]);
    expect(req.min_amount_minor).toBe(100);

    fill(c, { currency: 'jpy', suggested: '500.5' });
    expect(c.buildRequest()).toBeNull();
    expect(c.errors().join(' ')).toContain('whole number');
  });

  it('rejects malformed amounts and missing required fields', () => {
    const c = create().componentInstance;
    fill(c, { min: '', max: '1.234' });
    expect(c.buildRequest()).toBeNull();
    expect(c.errors()).toContain('Minimum is required.');
    expect(c.errors().join(' ')).toContain('Maximum must be');
  });

  it('loads an existing campaign converting minor units back to major units', () => {
    const fixture = create('c1');
    const campaign: Campaign = {
      id: 'c1', name: 'Yen drive', slug: 'yen', description: null, currency: 'jpy', goal_amount_minor: 100000,
      suggested_amounts_minor: [500, 1000], min_amount_minor: 100, max_amount_minor: 99999, allow_custom_amount: true,
      allow_recurring: false, success_url: 'https://e.org/t', cancel_url: 'https://e.org/c', show_donor_list: false,
      notify_emails: ['a@example.org'], is_active: true, starts_at: null, ends_at: null, raised_minor: 0, donor_count: 0,
      created_at: '', updated_at: '',
    };
    http.expectOne('/api/apps/app/donations/campaigns/c1').flush(campaign);
    const c = fixture.componentInstance;
    expect(c.form.controls.goal.value).toBe('100000');
    expect(c.form.controls.suggested.value).toBe('500, 1000');

    c.load({ ...campaign, currency: 'usd', goal_amount_minor: 1999, suggested_amounts_minor: [500, 1050] });
    expect(c.form.controls.goal.value).toBe('19.99');
    expect(c.form.controls.suggested.value).toBe('5.00, 10.50');
    // Round-trip is lossless.
    expect(c.buildRequest()!.suggested_amounts_minor).toEqual([500, 1050]);
  });

  it('posts the converted request', () => {
    const fixture = create();
    const c = fixture.componentInstance;
    fill(c, { currency: 'eur', suggested: '10', min: '2', max: '500', notify_emails: 'a@example.org, b@example.org' });
    c.save();
    const req = http.expectOne('/api/apps/app/donations/campaigns');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.suggested_amounts_minor).toEqual([1000]);
    expect(req.request.body.min_amount_minor).toBe(200);
    expect(req.request.body.notify_emails).toEqual(['a@example.org', 'b@example.org']);
  });
});
