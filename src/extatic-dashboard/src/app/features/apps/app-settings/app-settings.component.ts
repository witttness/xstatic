import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { App, RegenerateApiKeyResponse } from '../../../core/models/app.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { CopyButtonComponent } from '../../../shared/copy-button/copy-button.component';

@Component({
  selector: 'app-app-settings',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    ButtonComponent,
    LoadingSpinnerComponent,
    ConfirmModalComponent,
    CopyButtonComponent,
  ],
  template: `
    <app-page-header title="Settings" />

    @if (loading()) {
      <app-loading-spinner />
    } @else if (app()) {
      <!-- General -->
      <section class="mb-8">
        <h3 class="text-sm font-medium text-text-primary mb-4">General</h3>
        <form [formGroup]="generalForm" (ngSubmit)="saveGeneral()" class="max-w-lg space-y-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Name</label>
            <input formControlName="name" type="text"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">
              Allowed Origins <span class="normal-case text-text-muted font-normal">(comma-separated)</span>
            </label>
            <input formControlName="allowed_origins" type="text"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"
              placeholder="https://example.com, https://other.com" />
          </div>
          @if (generalError()) { <p class="text-xs text-error">{{ generalError() }}</p> }
          @if (generalSuccess()) { <p class="text-xs text-success">Saved.</p> }
          <app-button type="submit" [loading]="savingGeneral()">Save Changes</app-button>
        </form>
      </section>

      <div class="border-t border-border my-6"></div>

      <!-- Limits -->
      <section class="mb-8">
        <h3 class="text-sm font-medium text-text-primary mb-4">Limits</h3>
        <form [formGroup]="limitsForm" (ngSubmit)="saveLimits()" class="max-w-lg space-y-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Max File Size (MB)</label>
            <input formControlName="max_file_size_mb" type="number"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Max Attachments per Item</label>
            <input formControlName="max_attachments_per_item" type="number"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Storage Quota (GB)</label>
            <input formControlName="storage_quota_gb" type="number"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          </div>
          @if (limitsError()) { <p class="text-xs text-error">{{ limitsError() }}</p> }
          @if (limitsSuccess()) { <p class="text-xs text-success">Saved.</p> }
          <app-button type="submit" [loading]="savingLimits()">Save Changes</app-button>
        </form>
      </section>

      <div class="border-t border-border my-6"></div>

      <!-- Public App ID -->
      <section class="mb-8">
        <h3 class="text-sm font-medium text-text-primary mb-1">App ID</h3>
        <p class="text-xs text-text-muted mb-3">
          Use this in your static site's JavaScript as the <code class="bg-background border border-border px-1">X-App-Id</code> header.
          It is a public identifier — safe to commit and embed in client code.
        </p>
        <div class="flex items-center gap-2 bg-background border border-border px-3 py-2 max-w-lg">
          <code class="font-mono text-xs text-text-primary flex-1 break-all">{{ app()!.public_id }}</code>
          <app-copy-button [value]="app()!.public_id" />
        </div>
      </section>

      <div class="border-t border-border my-6"></div>

      <!-- Secret API Key -->
      <section class="mb-8">
        <h3 class="text-sm font-medium text-text-primary mb-1">Secret API Key</h3>
        <p class="text-xs text-text-muted mb-3">
          For server-side use only (webhooks, admin scripts). Never embed this in client-side code.
        </p>
        <div class="flex items-center gap-3 mb-4">
          <code class="font-mono text-xs text-text-muted bg-background border border-border px-3 py-2">
            ••••••••••••••••••••••••••••••••
          </code>
          <app-button variant="secondary" size="sm" (click)="showRegenerateConfirm = true">Regenerate</app-button>
        </div>
        @if (newApiKey()) {
          <div class="flex items-center gap-2 bg-background border border-border px-3 py-2 max-w-lg">
            <code class="font-mono text-xs text-text-primary flex-1 break-all">{{ newApiKey() }}</code>
            <app-copy-button [value]="newApiKey()!" />
          </div>
          <p class="text-xs text-warning mt-1">Copy this key now — it won't be shown again.</p>
        }
      </section>

      <div class="border-t border-border my-6"></div>

      <!-- Danger Zone -->
      <section class="border border-error/40 p-4 max-w-lg">
        <h3 class="text-sm font-medium text-error mb-2">Danger Zone</h3>
        <p class="text-xs text-text-muted mb-3">
          Permanently delete this app and all its data. This cannot be undone.
        </p>
        <app-button variant="danger" (click)="showDeleteConfirm = true">Delete App</app-button>
      </section>
    }

    @if (showRegenerateConfirm) {
      <app-confirm-modal
        title="Regenerate API Key"
        message="The current key will be immediately invalidated. Any clients using it will lose access."
        confirmLabel="Regenerate"
        (confirmed)="onRegenerate()"
        (cancelled)="showRegenerateConfirm = false"
      />
    }

    @if (showDeleteConfirm) {
      <app-confirm-modal
        title="Delete App"
        [message]="'This will permanently delete ' + app()!.name + ' and all its data.'"
        [confirmWord]="app()!.slug"
        confirmLabel="Delete"
        (confirmed)="onDelete()"
        (cancelled)="showDeleteConfirm = false"
      />
    }
  `,
})
export class AppSettingsComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  app = signal<App | null>(null);
  loading = signal(true);

  generalForm = this.fb.group({ name: ['', Validators.required], allowed_origins: [''] });
  limitsForm = this.fb.group({
    max_file_size_mb: [10, [Validators.required, Validators.min(1)]],
    max_attachments_per_item: [10, [Validators.required, Validators.min(1)]],
    storage_quota_gb: [1, [Validators.required, Validators.min(1)]],
  });

  savingGeneral = signal(false);
  generalError = signal<string | null>(null);
  generalSuccess = signal(false);

  savingLimits = signal(false);
  limitsError = signal<string | null>(null);
  limitsSuccess = signal(false);

  showRegenerateConfirm = false;
  showDeleteConfirm = false;
  newApiKey = signal<string | null>(null);

  ngOnInit() {
    this.api.get<App>(`/apps/${this.appSlug}`).subscribe({
      next: app => {
        this.app.set(app);
        this.generalForm.patchValue({ name: app.name, allowed_origins: app.allowed_origins.join(', ') });
        this.limitsForm.patchValue({
          max_file_size_mb: app.max_file_size_mb,
          max_attachments_per_item: app.max_attachments_per_item,
          storage_quota_gb: app.storage_quota_gb,
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  saveGeneral() {
    if (this.generalForm.invalid) return;
    this.savingGeneral.set(true);
    this.generalError.set(null);
    this.generalSuccess.set(false);
    const { name, allowed_origins } = this.generalForm.value;
    const origins = (allowed_origins ?? '').split(',').map(s => s.trim()).filter(Boolean);
    this.api.put<App>(`/apps/${this.appSlug}`, { name, allowed_origins: origins }).subscribe({
      next: app => { this.app.set(app); this.savingGeneral.set(false); this.generalSuccess.set(true); },
      error: () => { this.generalError.set('Failed to save.'); this.savingGeneral.set(false); },
    });
  }

  saveLimits() {
    if (this.limitsForm.invalid) return;
    this.savingLimits.set(true);
    this.limitsError.set(null);
    this.limitsSuccess.set(false);
    const { max_file_size_mb, max_attachments_per_item, storage_quota_gb } = this.limitsForm.value;
    this.api.put<App>(`/apps/${this.appSlug}`, { max_file_size_mb, max_attachments_per_item, storage_quota_gb }).subscribe({
      next: app => { this.app.set(app); this.savingLimits.set(false); this.limitsSuccess.set(true); },
      error: () => { this.limitsError.set('Failed to save.'); this.savingLimits.set(false); },
    });
  }

  onRegenerate() {
    this.showRegenerateConfirm = false;
    this.api.post<RegenerateApiKeyResponse>(`/apps/${this.appSlug}/api-key/regenerate`).subscribe({
      next: res => this.newApiKey.set(res.api_key),
      error: () => {},
    });
  }

  onDelete() {
    this.showDeleteConfirm = false;
    this.api.delete<void>(`/apps/${this.appSlug}`).subscribe({
      next: () => this.router.navigate(['/apps']),
      error: () => {},
    });
  }
}
