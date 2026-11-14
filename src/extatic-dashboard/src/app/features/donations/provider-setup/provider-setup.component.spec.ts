import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ProviderSetupComponent } from './provider-setup.component';
import { ProviderConfig } from '../donations.models';

const URL = '/api/apps/app/donations/providers/stripe';
const SECRET = 'sk_test_FAKE_never_render_me_1234';
const HOOK = 'whsec_FAKE_never_render_me_5678';

function config(over: Partial<ProviderConfig> = {}): ProviderConfig {
  return {
    provider: 'stripe', configured: true, publishable_key: 'pk_test_FAKE', has_secret_key: true,
    secret_key_last4: '1234', has_webhook_secret: true, webhook_secret_last4: '5678', is_live_mode: false,
    is_enabled: true, webhook_url: 'https://api.example.test/api/app_x/donations/webhooks/stripe',
    webhook_events: ['checkout.session.completed', 'charge.refunded'], updated_at: null, ...over,
  };
}

describe('ProviderSetupComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProviderSetupComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open(c: ProviderConfig) {
    const fixture = TestBed.createComponent(ProviderSetupComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.detectChanges();
    http.expectOne(URL).flush(c);
    fixture.detectChanges();
    return fixture;
  }

  function inputs(el: HTMLElement) {
    return {
      secret: el.querySelector<HTMLInputElement>('#secret_key')!,
      hook: el.querySelector<HTMLInputElement>('#webhook_secret')!,
    };
  }

  it('shows status, last-4, test badge and webhook URL but leaves secret inputs empty', () => {
    const fixture = open(config());
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Test mode');
    expect(el.textContent).toContain('•••• 1234');
    expect(el.querySelector('[data-testid="webhook-url"]')!.textContent).toContain('/donations/webhooks/stripe');
    const { secret, hook } = inputs(el);
    expect(secret.type).toBe('password');
    expect(hook.type).toBe('password');
    expect(secret.value).toBe('');
    expect(hook.value).toBe('');
  });

  it('shows the live badge for live keys', () => {
    const fixture = open(config({ is_live_mode: true }));
    expect(fixture.nativeElement.textContent).toContain('Live mode');
  });

  it('sends entered secrets once, then clears them and never renders them', () => {
    const fixture = open(config({ configured: false, has_secret_key: false, has_webhook_secret: false, secret_key_last4: null, webhook_secret_last4: null, publishable_key: null }));
    const el: HTMLElement = fixture.nativeElement;
    const { secret, hook } = inputs(el);
    secret.value = SECRET; secret.dispatchEvent(new Event('input'));
    hook.value = HOOK; hook.dispatchEvent(new Event('input'));
    fixture.componentInstance.save();

    const req = http.expectOne(URL);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.secret_key).toBe(SECRET);
    expect(req.request.body.webhook_secret).toBe(HOOK);
    req.flush(config());
    fixture.detectChanges();

    expect(inputs(el).secret.value).toBe('');
    expect(inputs(el).hook.value).toBe('');
    expect(el.innerHTML).not.toContain(SECRET);
    expect(el.innerHTML).not.toContain(HOOK);
    expect(el.textContent).toContain('Saved.');
  });

  it('omits secrets that were left empty (keeps the stored ones) and clears them after an error', () => {
    const fixture = open(config());
    const el: HTMLElement = fixture.nativeElement;
    fixture.componentInstance.save();
    const req = http.expectOne(URL);
    expect(req.request.body.secret_key).toBeNull();
    expect(req.request.body.webhook_secret).toBeNull();
    req.flush({}, { status: 500, statusText: 'Error' });

    const { secret } = inputs(el);
    secret.value = SECRET; secret.dispatchEvent(new Event('input'));
    fixture.componentInstance.save();
    http.expectOne(URL).flush({ errors: [{ pointer: 'secret_key', message: 'bad' }] }, { status: 422, statusText: 'Unprocessable' });
    fixture.detectChanges();
    expect(inputs(el).secret.value).toBe('');
    expect(el.innerHTML).not.toContain(SECRET);
    expect(el.textContent).toContain('secret_key: bad');
  });
});
