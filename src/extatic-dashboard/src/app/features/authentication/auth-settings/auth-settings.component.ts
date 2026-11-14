import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../../core/services/api.service';
import { App } from '../../../core/models/app.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { CopyButtonComponent } from '../../../shared/copy-button/copy-button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { AuthSettings, UpdateAuthSettings, parseDomains, urlTemplateError } from '../auth-settings.model';
import { IntegrationSnippet, buildIntegrationSnippets } from '../integration-snippets';

function urlTemplateValidator(control: AbstractControl): ValidationErrors | null {
  const message = urlTemplateError(control.value);
  return message ? { urlTemplate: message } : null;
}

@Component({
  selector: 'app-auth-settings',
  standalone: true,
  imports: [ReactiveFormsModule, PageHeaderComponent, ButtonComponent, CopyButtonComponent, LoadingSpinnerComponent],
  template: `
    <app-page-header
      title="Authentication"
      subtitle="Email and password sign-in for the users of your static site."
    />

    @if (loading()) {
      <app-loading-spinner />
    } @else if (loadError()) {
      <p class="text-error text-sm">{{ loadError() }}</p>
    } @else {
      <form [formGroup]="form" (ngSubmit)="save()" class="max-w-2xl space-y-8">
        <section class="space-y-3">
          <h3 class="text-sm font-medium text-text-primary">Sign-up</h3>
          <label class="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" formControlName="registration_enabled" class="w-4 h-4 accent-accent" />
            <span class="text-sm text-text-primary">Allow new users to register</span>
          </label>
          <label class="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" formControlName="require_email_verification" class="w-4 h-4 accent-accent" />
            <span class="text-sm text-text-primary">Require email verification before sign-in</span>
          </label>
          <div>
            <label for="domains" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Allowed email domains</label>
            <textarea id="domains" formControlName="allowed_email_domains" rows="2"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none"
              placeholder="example.com, partner.org"></textarea>
            <p class="text-xs text-text-muted mt-1">Leave empty to allow any domain.</p>
            @if (serverError('allowed_email_domains'); as e) { <p class="text-xs text-error mt-1">{{ e }}</p> }
          </div>
        </section>

        <section class="space-y-3">
          <h3 class="text-sm font-medium text-text-primary">Passwords and sessions</h3>
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            @for (field of numberFields; track field.key) {
              <div>
                <label [for]="field.key" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">{{ field.label }}</label>
                <input [id]="field.key" type="number" [formControlName]="field.key" [min]="field.min" [max]="field.max"
                  class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
                @if (form.get(field.key)?.invalid) {
                  <p class="text-xs text-error mt-1">Must be between {{ field.min }} and {{ field.max }}.</p>
                } @else if (serverError(field.key); as e) {
                  <p class="text-xs text-error mt-1">{{ e }}</p>
                }
              </div>
            }
          </div>
          <p class="text-xs text-text-muted">
            Disabling a user revokes their refresh tokens, but an access token already issued stays valid
            until it expires (the access token lifetime above).
          </p>
        </section>

        <section class="space-y-3">
          <h3 class="text-sm font-medium text-text-primary">Email links</h3>
          <p class="text-xs text-text-muted">
            Pages on your site that receive the token. Use <code class="bg-background border border-border px-1">{{ '{' }}token{{ '}' }}</code>
            where the token goes. Without a template the email contains a code to paste instead.
          </p>
          @for (field of templateFields; track field.key) {
            <div>
              <label [for]="field.key" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">{{ field.label }}</label>
              <input [id]="field.key" type="text" [formControlName]="field.key" [placeholder]="field.placeholder"
                class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
              @if (form.get(field.key)?.errors?.['urlTemplate']; as e) {
                <p class="text-xs text-error mt-1">{{ e }}</p>
              } @else if (serverError(field.key); as e) {
                <p class="text-xs text-error mt-1">{{ e }}</p>
              }
            </div>
          }
        </section>

        @if (saveError()) { <p class="text-xs text-error">{{ saveError() }}</p> }
        @if (saved()) { <p class="text-xs text-success">Settings saved.</p> }
        <app-button type="submit" [loading]="saving()" [disabled]="form.invalid">Save Settings</app-button>
      </form>

      <div class="border-t border-border my-8"></div>

      <section class="max-w-3xl">
        <h3 class="text-sm font-medium text-text-primary mb-1">Integration</h3>
        <p class="text-xs text-text-muted mb-4">
          Copy these into your site. Keep the access token in memory; the refresh token is single-use and must be
          replaced after every refresh.
        </p>
        @for (snippet of snippets(); track snippet.title) {
          <div class="mb-4">
            <div class="flex items-center justify-between mb-1">
              <span class="text-xs font-medium text-text-muted uppercase tracking-wider">{{ snippet.title }}</span>
              <app-copy-button [value]="snippet.code" />
            </div>
            <pre class="bg-background border border-border p-3 text-xs font-mono text-text-primary overflow-x-auto">{{ snippet.code }}</pre>
          </div>
        }
      </section>
    }
  `,
})
export class AuthSettingsComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);
  private fb = inject(FormBuilder);

  loading = signal(true);
  loadError = signal<string | null>(null);
  saving = signal(false);
  saved = signal(false);
  saveError = signal<string | null>(null);
  serverErrors = signal<Record<string, string>>({});
  snippets = signal<IntegrationSnippet[]>([]);

  readonly numberFields = [
    { key: 'password_min_length', label: 'Minimum password length', min: 8, max: 128 },
    { key: 'access_token_minutes', label: 'Access token lifetime (minutes)', min: 5, max: 1440 },
    { key: 'refresh_token_days', label: 'Refresh token lifetime (days)', min: 1, max: 365 },
    { key: 'max_failed_attempts', label: 'Failed attempts before lockout', min: 1, max: 100 },
    { key: 'lockout_minutes', label: 'Lockout duration (minutes)', min: 1, max: 1440 },
  ] as const;

  readonly templateFields = [
    { key: 'verify_url_template', label: 'Verify email URL', placeholder: 'https://example.com/verify?token={token}' },
    { key: 'reset_url_template', label: 'Reset password URL', placeholder: 'https://example.com/reset?token={token}' },
  ] as const;

  form = this.fb.nonNullable.group({
    registration_enabled: true,
    require_email_verification: true,
    password_min_length: [10, [Validators.required, Validators.min(8), Validators.max(128)]],
    access_token_minutes: [60, [Validators.required, Validators.min(5), Validators.max(1440)]],
    refresh_token_days: [30, [Validators.required, Validators.min(1), Validators.max(365)]],
    max_failed_attempts: [5, [Validators.required, Validators.min(1), Validators.max(100)]],
    lockout_minutes: [15, [Validators.required, Validators.min(1), Validators.max(1440)]],
    verify_url_template: ['', urlTemplateValidator],
    reset_url_template: ['', urlTemplateValidator],
    allowed_email_domains: '',
  });

  ngOnInit() {
    this.api.get<AuthSettings>(`/apps/${this.appSlug}/auth/settings`).subscribe({
      next: settings => { this.patch(settings); this.loading.set(false); },
      error: (err: HttpErrorResponse) => {
        this.loadError.set(err.status === 403
          ? 'Only the owner or an admin can manage authentication settings.'
          : 'Failed to load authentication settings.');
        this.loading.set(false);
      },
    });
    this.api.get<App>(`/apps/${this.appSlug}`).subscribe({
      next: app => this.snippets.set(buildIntegrationSnippets(`${window.location.origin}/api`, app.public_id)),
      error: () => this.snippets.set([]),
    });
  }

  serverError(key: string): string | null {
    return this.serverErrors()[key] ?? null;
  }

  toRequest(): UpdateAuthSettings {
    const v = this.form.getRawValue();
    return {
      registration_enabled: v.registration_enabled,
      require_email_verification: v.require_email_verification,
      password_min_length: Number(v.password_min_length),
      access_token_minutes: Number(v.access_token_minutes),
      refresh_token_days: Number(v.refresh_token_days),
      max_failed_attempts: Number(v.max_failed_attempts),
      lockout_minutes: Number(v.lockout_minutes),
      verify_url_template: v.verify_url_template.trim() || null,
      reset_url_template: v.reset_url_template.trim() || null,
      allowed_email_domains: parseDomains(v.allowed_email_domains),
    };
  }

  save() {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.saving.set(true);
    this.saved.set(false);
    this.saveError.set(null);
    this.serverErrors.set({});
    this.api.put<AuthSettings>(`/apps/${this.appSlug}/auth/settings`, this.toRequest()).subscribe({
      next: settings => { this.patch(settings); this.saving.set(false); this.saved.set(true); },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        // GlobalExceptionHandler emits { errors: { errors: [{ pointer, message }] } }.
        const raw = err.status === 422 ? err.error?.errors?.errors ?? err.error?.errors : null;
        const errors: { pointer: string; message: string }[] = Array.isArray(raw) ? raw : [];
        if (errors.length > 0) {
          const byField: Record<string, string> = {};
          for (const e of errors) byField[e.pointer] ??= e.message;
          this.serverErrors.set(byField);
          this.saveError.set('Please fix the highlighted fields.');
        } else {
          this.saveError.set('Failed to save settings.');
        }
      },
    });
  }

  private patch(s: AuthSettings) {
    this.form.reset({
      registration_enabled: s.registration_enabled,
      require_email_verification: s.require_email_verification,
      password_min_length: s.password_min_length,
      access_token_minutes: s.access_token_minutes,
      refresh_token_days: s.refresh_token_days,
      max_failed_attempts: s.max_failed_attempts,
      lockout_minutes: s.lockout_minutes,
      verify_url_template: s.verify_url_template ?? '',
      reset_url_template: s.reset_url_template ?? '',
      allowed_email_domains: s.allowed_email_domains.join(', '),
    });
  }
}
