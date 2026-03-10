import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { WebhookDeliveryLog } from '../../../core/models/webhook.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';

@Component({
  selector: 'app-webhook-logs',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent],
  template: `
    <app-page-header title="Webhook Logs">
      <a [routerLink]="['/apps', appSlug, 'webhooks', id]">
        <app-button variant="secondary" size="sm">← Back</app-button>
      </a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (logs().length === 0) {
      <app-empty-state message="No delivery logs yet." description="Logs are retained for 7 days." />
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Event</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Attempt</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Next Retry</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Created</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (log of logs(); track log.id) {
              <tr class="border-b border-border hover:bg-surface cursor-pointer" (click)="toggle(log.id)">
                <td class="px-4 py-3 font-mono text-xs text-text-primary">{{ log.event_type }}</td>
                <td class="px-4 py-3">
                  <app-badge [variant]="statusVariant(log.status_code)">
                    {{ log.status_code ?? '—' }}
                  </app-badge>
                </td>
                <td class="px-4 py-3 text-text-muted text-xs">#{{ log.attempt_number }}</td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ log.next_retry_at ? formatDate(log.next_retry_at) : '—' }}</td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ formatDate(log.created_at) }}</td>
                <td class="px-4 py-3 text-right">
                  <button type="button" (click)="retrigger(log, $event)"
                    class="text-xs text-accent hover:underline">Retrigger</button>
                </td>
              </tr>
              @if (expandedId() === log.id) {
                <tr class="border-b border-border bg-surface">
                  <td colspan="6" class="px-4 py-3">
                    <div class="mb-2">
                      <div class="text-xs text-text-muted mb-1 uppercase tracking-wider font-medium">Payload</div>
                      <pre class="font-mono text-xs text-text-primary bg-background border border-border p-3 overflow-x-auto">{{ formatJson(log.payload) }}</pre>
                    </div>
                    @if (log.response_body) {
                      <div>
                        <div class="text-xs text-text-muted mb-1 uppercase tracking-wider font-medium">Response Body</div>
                        <pre class="font-mono text-xs text-text-primary bg-background border border-border p-3 overflow-x-auto">{{ log.response_body }}</pre>
                      </div>
                    }
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      </div>
      <p class="text-xs text-text-muted mt-3">Delivery logs are retained for 7 days.</p>
    }
  `,
})
export class WebhookLogsComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id!: string;

  private api = inject(ApiService);

  logs = signal<WebhookDeliveryLog[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  expandedId = signal<string | null>(null);

  ngOnInit() {
    this.api.get<WebhookDeliveryLog[]>(`/apps/${this.appSlug}/webhooks/${this.id}/logs`).subscribe({
      next: logs => { this.logs.set(logs); this.loading.set(false); },
      error: () => { this.error.set('Failed to load logs.'); this.loading.set(false); },
    });
  }

  toggle(id: string) {
    this.expandedId.set(this.expandedId() === id ? null : id);
  }

  retrigger(log: WebhookDeliveryLog, e: Event) {
    e.stopPropagation();
    this.api.post<void>(`/apps/${this.appSlug}/webhooks/${this.id}/logs/${log.id}/retrigger`).subscribe();
  }

  statusVariant(code: number | null): 'success' | 'error' | 'warning' | 'muted' {
    if (code === null) return 'muted';
    if (code >= 200 && code < 300) return 'success';
    if (code >= 400) return 'error';
    return 'warning';
  }

  formatDate(d: string) {
    return new Date(d).toLocaleString();
  }

  formatJson(payload: unknown): string {
    try { return JSON.stringify(payload, null, 2); }
    catch { return String(payload); }
  }
}
