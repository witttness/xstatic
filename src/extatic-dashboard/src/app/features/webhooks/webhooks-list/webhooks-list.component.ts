import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { Webhook } from '../../../core/models/webhook.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';

@Component({
  selector: 'app-webhooks-list',
  standalone: true,
  imports: [
    RouterLink,
    PageHeaderComponent,
    ButtonComponent,
    BadgeComponent,
    LoadingSpinnerComponent,
    EmptyStateComponent,
    ConfirmModalComponent,
  ],
  template: `
    <app-page-header title="Webhooks">
      <a [routerLink]="['/apps', appSlug, 'webhooks', 'new']">
        <app-button>New Webhook</app-button>
      </a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (webhooks().length === 0) {
      <app-empty-state message="No webhooks yet." description="Set up webhooks to receive real-time notifications.">
        <a [routerLink]="['/apps', appSlug, 'webhooks', 'new']">
          <app-button>New Webhook</app-button>
        </a>
      </app-empty-state>
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">URL</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Events</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (w of webhooks(); track w.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3">
                  <span class="font-mono text-xs text-text-primary truncate max-w-xs block">{{ w.url }}</span>
                </td>
                <td class="px-4 py-3">
                  <div class="flex flex-wrap gap-1">
                    @for (e of w.events; track e) {
                      <app-badge variant="info">{{ e }}</app-badge>
                    }
                  </div>
                </td>
                <td class="px-4 py-3">
                  @if (w.is_active) {
                    <app-badge variant="success">Active</app-badge>
                  } @else {
                    <app-badge variant="muted">Inactive</app-badge>
                  }
                </td>
                <td class="px-4 py-3 text-right">
                  <div class="flex items-center justify-end gap-3">
                    <a [routerLink]="['/apps', appSlug, 'webhooks', w.id]"
                      class="text-xs text-text-muted hover:text-text-primary">Edit</a>
                    <a [routerLink]="['/apps', appSlug, 'webhooks', w.id, 'logs']"
                      class="text-xs text-text-muted hover:text-text-primary">Logs</a>
                    <button type="button" (click)="deletingWebhook.set(w)"
                      class="text-xs text-error hover:text-red-400">Delete</button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (deletingWebhook()) {
      <app-confirm-modal
        title="Delete Webhook"
        [message]="'Delete webhook for ' + deletingWebhook()!.url + '?'"
        confirmLabel="Delete"
        (confirmed)="onDelete()"
        (cancelled)="deletingWebhook.set(null)"
      />
    }
  `,
})
export class WebhooksListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  webhooks = signal<Webhook[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  deletingWebhook = signal<Webhook | null>(null);

  ngOnInit() {
    this.api.get<Webhook[]>(`/apps/${this.appSlug}/webhooks`).subscribe({
      next: ws => { this.webhooks.set(ws); this.loading.set(false); },
      error: () => { this.error.set('Failed to load webhooks.'); this.loading.set(false); },
    });
  }

  onDelete() {
    const w = this.deletingWebhook()!;
    this.deletingWebhook.set(null);
    this.api.delete<void>(`/apps/${this.appSlug}/webhooks/${w.id}`).subscribe({
      next: () => this.webhooks.update(ws => ws.filter(x => x.id !== w.id)),
      error: () => {},
    });
  }
}
