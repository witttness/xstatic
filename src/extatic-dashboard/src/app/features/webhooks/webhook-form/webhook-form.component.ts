import { Component, Input, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { Webhook, WebhookEvent } from '../../../core/models/webhook.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';

const EVENT_GROUPS: { label: string; events: WebhookEvent[] }[] = [
  { label: 'Items', events: ['item.created', 'item.updated', 'item.deleted'] },
  { label: 'AppUsers', events: ['appuser.created', 'appuser.updated'] },
  { label: 'Attachments', events: ['attachment.created', 'attachment.deleted'] },
];

@Component({
  selector: 'app-webhook-form',
  standalone: true,
  imports: [ReactiveFormsModule, PageHeaderComponent, ButtonComponent],
  template: `
    <app-page-header title="New Webhook" />

    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="max-w-lg space-y-5">
      <div>
        <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">URL</label>
        <input formControlName="url" type="url"
          class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none"
          placeholder="https://example.com/webhook" />
        @if (form.get('url')?.invalid && form.get('url')?.touched) {
          <p class="text-xs text-error mt-1">A valid URL is required.</p>
        }
      </div>

      <div>
        <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Events</label>
        @for (group of eventGroups; track group.label) {
          <div class="mb-3">
            <div class="text-xs text-text-muted mb-1">{{ group.label }}</div>
            @for (event of group.events; track event) {
              <label class="flex items-center gap-2 mb-1 cursor-pointer">
                <input type="checkbox" [value]="event" (change)="toggleEvent(event, $any($event.target).checked)"
                  class="w-4 h-4 border border-border bg-background accent-accent" />
                <span class="font-mono text-xs text-text-primary">{{ event }}</span>
              </label>
            }
          </div>
        }
        @if (eventsError()) {
          <p class="text-xs text-error">Select at least one event.</p>
        }
      </div>

      @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }

      <div class="flex gap-3 pt-2">
        <app-button type="submit" [loading]="saving()">Create Webhook</app-button>
        <app-button variant="secondary" type="button" (click)="cancel()">Cancel</app-button>
      </div>
    </form>
  `,
})
export class WebhookFormComponent {
  @Input() appSlug!: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  eventGroups = EVENT_GROUPS;
  selectedEvents = new Set<WebhookEvent>();
  eventsError = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);

  form = this.fb.group({
    url: ['', [Validators.required, Validators.pattern(/^https?:\/\/.+/)]],
  });

  toggleEvent(event: WebhookEvent, checked: boolean) {
    if (checked) this.selectedEvents.add(event);
    else this.selectedEvents.delete(event);
  }

  onSubmit() {
    this.form.markAllAsTouched();
    if (this.selectedEvents.size === 0) { this.eventsError.set(true); return; }
    this.eventsError.set(false);
    if (this.form.invalid) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.post<Webhook>(`/apps/${this.appSlug}/webhooks`, {
      url: this.form.value.url,
      events: Array.from(this.selectedEvents),
    }).subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'webhooks']),
      error: () => { this.error.set('Failed to create webhook.'); this.saving.set(false); },
    });
  }

  cancel() {
    this.router.navigate(['/apps', this.appSlug, 'webhooks']);
  }
}
