import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { Webhook, WebhookEvent } from '../../../core/models/webhook.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';

const EVENT_GROUPS: { label: string; events: WebhookEvent[] }[] = [
  { label: 'Items', events: ['item.created', 'item.updated', 'item.deleted'] },
  { label: 'AppUsers', events: ['appuser.created', 'appuser.updated'] },
  { label: 'Attachments', events: ['attachment.created', 'attachment.deleted'] },
];

@Component({
  selector: 'app-webhook-detail',
  standalone: true,
  imports: [ReactiveFormsModule, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent],
  template: `
    <app-page-header title="Edit Webhook" />

    @if (loading()) {
      <app-loading-spinner />
    } @else if (webhook()) {
      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="max-w-lg space-y-5">
        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">URL</label>
          <input formControlName="url" type="url"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Events</label>
          @for (group of eventGroups; track group.label) {
            <div class="mb-3">
              <div class="text-xs text-text-muted mb-1">{{ group.label }}</div>
              @for (event of group.events; track event) {
                <label class="flex items-center gap-2 mb-1 cursor-pointer">
                  <input type="checkbox" [value]="event" [checked]="selectedEvents.has(event)"
                    (change)="toggleEvent(event, $any($event.target).checked)"
                    class="w-4 h-4 border border-border bg-background accent-accent" />
                  <span class="font-mono text-xs text-text-primary">{{ event }}</span>
                </label>
              }
            </div>
          }
        </div>

        <div>
          <label class="flex items-center gap-2 cursor-pointer">
            <input formControlName="is_active" type="checkbox"
              class="w-4 h-4 border border-border bg-background accent-accent" />
            <span class="text-sm text-text-primary">Active</span>
          </label>
        </div>

        <div class="bg-surface border border-border px-3 py-2">
          <p class="text-xs text-text-muted">
            The signing secret is not displayed after creation. Create a new webhook if you need a new secret.
          </p>
        </div>

        @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }
        @if (saved()) { <p class="text-xs text-success">Saved.</p> }

        <div class="flex gap-3 pt-2">
          <app-button type="submit" [loading]="saving()">Save Changes</app-button>
          <app-button variant="secondary" type="button" (click)="cancel()">Cancel</app-button>
        </div>
      </form>
    }
  `,
})
export class WebhookDetailComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id!: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  eventGroups = EVENT_GROUPS;
  webhook = signal<Webhook | null>(null);
  loading = signal(true);
  saving = signal(false);
  saved = signal(false);
  error = signal<string | null>(null);
  selectedEvents = new Set<WebhookEvent>();

  form = this.fb.group({
    url: ['', [Validators.required, Validators.pattern(/^https?:\/\/.+/)]],
    is_active: [true],
  });

  ngOnInit() {
    this.api.get<Webhook>(`/apps/${this.appSlug}/webhooks/${this.id}`).subscribe({
      next: w => {
        this.webhook.set(w);
        this.selectedEvents = new Set(w.events);
        this.form.patchValue({ url: w.url, is_active: w.is_active });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  toggleEvent(event: WebhookEvent, checked: boolean) {
    if (checked) this.selectedEvents.add(event);
    else this.selectedEvents.delete(event);
  }

  onSubmit() {
    if (this.form.invalid) return;
    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);
    const { url, is_active } = this.form.value;
    this.api.put<Webhook>(`/apps/${this.appSlug}/webhooks/${this.id}`, {
      url,
      events: Array.from(this.selectedEvents),
      is_active,
    }).subscribe({
      next: w => {
        this.webhook.set(w);
        this.saving.set(false);
        this.saved.set(true);
      },
      error: () => { this.error.set('Failed to save.'); this.saving.set(false); },
    });
  }

  cancel() {
    this.router.navigate(['/apps', this.appSlug, 'webhooks']);
  }
}
