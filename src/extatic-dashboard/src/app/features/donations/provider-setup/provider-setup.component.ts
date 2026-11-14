import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { CopyButtonComponent } from '../../../shared/copy-button/copy-button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { ProviderConfig } from '../donations.models';

/**
 * Stripe connection. Secret inputs are write-only: they always start empty, are never filled from
 * the API (which only returns has_* flags and last-4), and are cleared after every save.
 */
@Component({
  selector: 'app-donations-provider-setup',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent,
    CopyButtonComponent, LoadingSpinnerComponent, ConfirmModalComponent],
  template: `
    <app-page-header title="Payment provider" subtitle="Connect your own Stripe account. Extatic never holds funds or card data.">
      <a [routerLink]="['/apps', appSlug, 'donations']"><app-button variant="secondary">Back to donations</app-button></a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (loadError()) {
      <p class="text-error text-sm">{{ loadError() }}</p>
    } @else if (config(); as c) {
      <div class="max-w-2xl space-y-8">
        <section class="border border-border p-4 space-y-2">
          <div class="flex items-center gap-2">
            <span class="text-sm font-medium text-text-primary">Stripe</span>
            @if (c.configured) {
              @if (c.is_live_mode) { <app-badge variant="warning">Live mode</app-badge> }
              @else { <app-badge variant="info">Test mode</app-badge> }
              @if (c.is_enabled) { <app-badge variant="success">Enabled</app-badge> }
              @else { <app-badge variant="muted">Disabled</app-badge> }
            } @else {
              <app-badge variant="muted">Not connected</app-badge>
            }
          </div>
          @if (c.configured) {
            <dl class="text-xs text-text-muted grid grid-cols-2 gap-1" data-testid="key-status">
              <dt>Secret key</dt><dd class="font-mono">{{ c.has_secret_key ? '•••• ' + c.secret_key_last4 : 'not set' }}</dd>
              <dt>Webhook signing secret</dt><dd class="font-mono">{{ c.has_webhook_secret ? '•••• ' + c.webhook_secret_last4 : 'not set' }}</dd>
              <dt>Publishable key</dt><dd class="font-mono break-all">{{ c.publishable_key || 'not set' }}</dd>
            </dl>
          }
        </section>

        <form [formGroup]="form" (ngSubmit)="save()" class="space-y-4" autocomplete="off">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="secret_key">
              Secret key {{ c.has_secret_key ? '(leave empty to keep the current key)' : '' }}
            </label>
            <input id="secret_key" formControlName="secret_key" type="password" autocomplete="new-password" spellcheck="false"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none"
              placeholder="sk_test_… or rk_test_…" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="webhook_secret">
              Webhook signing secret {{ c.has_webhook_secret ? '(leave empty to keep)' : '' }}
            </label>
            <input id="webhook_secret" formControlName="webhook_secret" type="password" autocomplete="new-password" spellcheck="false"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none"
              placeholder="whsec_…" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="publishable_key">
              Publishable key (optional)
            </label>
            <input id="publishable_key" formControlName="publishable_key" type="text" spellcheck="false"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none"
              placeholder="pk_test_…" />
          </div>
          <label class="flex items-center gap-2 text-sm text-text-primary">
            <input type="checkbox" formControlName="is_enabled" class="w-4 h-4 accent-accent" /> Accept donations
          </label>
          @if (saveError()) { <p class="text-xs text-error">{{ saveError() }}</p> }
          @if (saved()) { <p class="text-xs text-success">Saved.</p> }
          <div class="flex gap-3">
            <app-button type="submit" [loading]="saving()">{{ c.configured ? 'Save changes' : 'Connect Stripe' }}</app-button>
            @if (c.configured) {
              <app-button variant="danger" (click)="confirmDisconnect.set(true)">Disconnect</app-button>
            }
          </div>
        </form>

        <section class="space-y-2">
          <h3 class="text-sm font-semibold text-text-primary">Webhook endpoint</h3>
          <div class="flex items-center gap-2">
            <code class="font-mono text-xs text-text-primary bg-surface border border-border px-2 py-1 break-all" data-testid="webhook-url">{{ c.webhook_url }}</code>
            <app-copy-button [value]="c.webhook_url" />
          </div>
          <ol class="list-decimal list-inside text-xs text-text-muted space-y-1">
            <li>In the Stripe Dashboard open Developers → Webhooks and add an endpoint.</li>
            <li>Paste the URL above as the endpoint URL.</li>
            <li>Select these events:
              @for (e of c.webhook_events; track e) { <code class="font-mono text-text-primary mx-1">{{ e }}</code> }
            </li>
            <li>Reveal the endpoint's signing secret (whsec_…) and paste it above.</li>
            <li>Use test keys first; switch to live keys when you're ready.</li>
          </ol>
        </section>
      </div>
    }

    @if (confirmDisconnect()) {
      <app-confirm-modal title="Disconnect Stripe" confirmLabel="Disconnect"
        message="New checkouts will stop working and the stored keys are deleted. Existing donations are kept."
        (confirmed)="disconnect()" (cancelled)="confirmDisconnect.set(false)" />
    }
  `,
})
export class ProviderSetupComponent implements OnInit {
  @Input() appSlug!: string;
  readonly provider = 'stripe';

  private api = inject(ApiService);
  private fb = inject(FormBuilder);

  config = signal<ProviderConfig | null>(null);
  loading = signal(true);
  loadError = signal<string | null>(null);
  saving = signal(false);
  saved = signal(false);
  saveError = signal<string | null>(null);
  confirmDisconnect = signal(false);

  form = this.fb.nonNullable.group({
    secret_key: '',
    webhook_secret: '',
    publishable_key: '',
    is_enabled: true,
  });

  private get path() { return `/apps/${this.appSlug}/donations/providers/${this.provider}`; }

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.api.get<ProviderConfig>(this.path).subscribe({
      next: c => { this.apply(c); this.loading.set(false); },
      error: () => { this.loadError.set('Failed to load the payment provider settings.'); this.loading.set(false); },
    });
  }

  private apply(c: ProviderConfig) {
    this.config.set(c);
    // Secrets are never written into the form: only non-secret fields come from the API.
    this.form.reset({
      secret_key: '',
      webhook_secret: '',
      publishable_key: c.publishable_key ?? '',
      is_enabled: c.configured ? c.is_enabled : true,
    });
  }

  save() {
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.saved.set(false);
    this.saveError.set(null);
    this.api.put<ProviderConfig>(this.path, {
      secret_key: v.secret_key.trim() || null,
      webhook_secret: v.webhook_secret.trim() || null,
      publishable_key: v.publishable_key.trim(),
      is_enabled: v.is_enabled,
    }).subscribe({
      next: c => { this.apply(c); this.saving.set(false); this.saved.set(true); },
      error: (e: HttpErrorResponse) => {
        this.saving.set(false);
        // Clear secrets on failure too, so they don't linger in the page.
        this.form.patchValue({ secret_key: '', webhook_secret: '' });
        const errors = e.error?.errors as { pointer: string; message: string }[] | undefined;
        this.saveError.set(errors?.length ? errors.map(x => `${x.pointer}: ${x.message}`).join(' · ')
          : e.status === 403 ? 'Only the app owner can change payment settings.' : 'Failed to save.');
      },
    });
  }

  disconnect() {
    this.confirmDisconnect.set(false);
    this.api.delete<void>(this.path).subscribe({ next: () => this.load(), error: () => this.saveError.set('Failed to disconnect.') });
  }
}
