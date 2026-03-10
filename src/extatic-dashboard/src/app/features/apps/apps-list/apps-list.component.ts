import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { App, CreateAppResponse } from '../../../core/models/app.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { CopyButtonComponent } from '../../../shared/copy-button/copy-button.component';

@Component({
  selector: 'app-apps-list',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    PageHeaderComponent,
    ButtonComponent,
    LoadingSpinnerComponent,
    EmptyStateComponent,
    CopyButtonComponent,
  ],
  template: `
    <app-page-header title="Apps" subtitle="Your registered applications.">
      <app-button (click)="showCreate = true">New App</app-button>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (apps().length === 0) {
      <app-empty-state message="No apps yet." description="Create your first app to get started.">
        <app-button (click)="showCreate = true">New App</app-button>
      </app-empty-state>
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Name</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Created</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (app of apps(); track app.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3">
                  <div class="text-text-primary font-medium">{{ app.name }}</div>
                  <div class="font-mono text-xs text-text-muted">{{ app.slug }}</div>
                </td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ formatDate(app.created_at) }}</td>
                <td class="px-4 py-3 text-right">
                  <a
                    [routerLink]="['/apps', app.slug, 'settings']"
                    class="text-xs text-text-muted hover:text-text-primary"
                  >Settings</a>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    <!-- Create App Modal -->
    @if (showCreate) {
      <div class="fixed inset-0 z-50 flex items-center justify-center">
        <div class="absolute inset-0 bg-black/60" (click)="closeCreate()"></div>
        <div class="relative bg-surface border border-border w-full max-w-md p-6 z-10">
          <h3 class="text-base font-semibold text-text-primary mb-4">New App</h3>
          <form [formGroup]="createForm" (ngSubmit)="onCreate()">
            <div class="mb-4">
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Name</label>
              <input
                formControlName="name"
                type="text"
                class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"
                placeholder="My App"
                (input)="autoSlug()"
              />
            </div>
            <div class="mb-6">
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Slug</label>
              <input
                formControlName="slug"
                type="text"
                class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none"
                placeholder="my-app"
              />
              @if (createForm.get('slug')?.invalid && createForm.get('slug')?.touched) {
                <p class="text-xs text-error mt-1">Slug is required (lowercase, hyphens only).</p>
              }
            </div>
            @if (createError()) {
              <p class="text-xs text-error mb-3">{{ createError() }}</p>
            }
            <div class="flex justify-end gap-2">
              <app-button variant="secondary" type="button" (click)="closeCreate()">Cancel</app-button>
              <app-button type="submit" [loading]="creating()">Create</app-button>
            </div>
          </form>
        </div>
      </div>
    }

    <!-- API Key Reveal Modal -->
    @if (newApiKey()) {
      <div class="fixed inset-0 z-50 flex items-center justify-center">
        <div class="absolute inset-0 bg-black/60"></div>
        <div class="relative bg-surface border border-border w-full max-w-lg p-6 z-10">
          <h3 class="text-base font-semibold text-text-primary mb-2">App Created</h3>
          <p class="text-sm text-text-muted mb-4">
            Copy your API key now. It will not be shown again.
          </p>
          <div class="flex items-center gap-2 bg-background border border-border px-3 py-2 mb-6">
            <code class="font-mono text-xs text-text-primary flex-1 break-all">{{ newApiKey() }}</code>
            <app-copy-button [value]="newApiKey()!" />
          </div>
          <div class="flex justify-end">
            <app-button (click)="closeApiKey()">Done</app-button>
          </div>
        </div>
      </div>
    }
  `,
})
export class AppsListComponent implements OnInit {
  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  apps = signal<App[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  showCreate = false;
  creating = signal(false);
  createError = signal<string | null>(null);
  newApiKey = signal<string | null>(null);
  private newAppSlug = '';

  createForm = this.fb.group({
    name: ['', Validators.required],
    slug: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]+$/)]],
  });

  ngOnInit() {
    this.api.get<App[]>('/apps').subscribe({
      next: apps => { this.apps.set(apps); this.loading.set(false); },
      error: () => { this.error.set('Failed to load apps.'); this.loading.set(false); },
    });
  }

  autoSlug() {
    const name = this.createForm.get('name')!.value ?? '';
    const slug = name.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, '');
    this.createForm.get('slug')!.setValue(slug, { emitEvent: false });
  }

  closeCreate() {
    this.showCreate = false;
    this.createForm.reset();
    this.createError.set(null);
  }

  onCreate() {
    if (this.createForm.invalid) { this.createForm.markAllAsTouched(); return; }
    this.creating.set(true);
    this.createError.set(null);
    const { name, slug } = this.createForm.value;
    this.api.post<CreateAppResponse>('/apps', { name, slug }).subscribe({
      next: res => {
        this.apps.update(a => [...a, res.app]);
        this.newAppSlug = res.app.slug;
        this.creating.set(false);
        this.showCreate = false;
        this.createForm.reset();
        this.newApiKey.set(res.api_key);
      },
      error: () => {
        this.createError.set('Failed to create app. Slug may be taken.');
        this.creating.set(false);
      },
    });
  }

  closeApiKey() {
    const slug = this.newAppSlug;
    this.newApiKey.set(null);
    this.router.navigate(['/apps', slug, 'collections']);
  }

  formatDate(d: string) {
    return new Date(d).toLocaleDateString();
  }
}
