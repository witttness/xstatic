import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ExtaticEvent, EventStatus, seatSummary, statusVariant } from '../event.model';

@Component({
  selector: 'app-events-list',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent],
  template: `
    <app-page-header title="Events" subtitle="Registration with capacity limits and waitlists.">
      <a [routerLink]="['/apps', appSlug, 'events', 'new']"><app-button>New Event</app-button></a>
    </app-page-header>

    <div class="flex gap-2 mb-4">
      @for (option of statusOptions; track option.value) {
        <button type="button" (click)="setStatus(option.value)"
          class="px-3 py-1 text-xs border"
          [class]="status() === option.value ? 'border-accent text-accent' : 'border-border text-text-muted hover:text-text-primary'">
          {{ option.label }}
        </button>
      }
    </div>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (events().length === 0) {
      <app-empty-state message="No events yet." description="Create an event to start taking registrations.">
        <a [routerLink]="['/apps', appSlug, 'events', 'new']"><app-button>New Event</app-button></a>
      </app-empty-state>
    } @else {
      <div class="border border-border overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Event</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Starts</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Seats</th>
            </tr>
          </thead>
          <tbody>
            @for (ev of events(); track ev.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3">
                  <a [routerLink]="['/apps', appSlug, 'events', ev.id]" class="text-text-primary font-medium hover:underline">{{ ev.title }}</a>
                  <div class="font-mono text-xs text-text-muted">{{ ev.slug }}</div>
                </td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ formatDate(ev.starts_at) }}</td>
                <td class="px-4 py-3"><app-badge [variant]="variant(ev.status)">{{ ev.status }}</app-badge></td>
                <td class="px-4 py-3 text-xs text-text-primary">{{ seats(ev) }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (total() > pageSize) {
        <div class="flex items-center justify-between mt-3 text-xs text-text-muted">
          <span>Page {{ page() }} of {{ pages() }}</span>
          <div class="flex gap-2">
            <app-button variant="secondary" size="sm" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</app-button>
            <app-button variant="secondary" size="sm" [disabled]="page() >= pages()" (click)="go(page() + 1)">Next</app-button>
          </div>
        </div>
      }
    }
  `,
})
export class EventsListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  readonly pageSize = 20;
  readonly statusOptions: { label: string; value: EventStatus | null }[] = [
    { label: 'All', value: null },
    { label: 'Draft', value: 'draft' },
    { label: 'Published', value: 'published' },
    { label: 'Cancelled', value: 'cancelled' },
  ];

  events = signal<ExtaticEvent[]>([]);
  total = signal(0);
  page = signal(1);
  status = signal<EventStatus | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<PagedResult<ExtaticEvent>>(`/apps/${this.appSlug}/events`, {
      status: this.status(), page: this.page(), page_size: this.pageSize,
    }).subscribe({
      next: res => { this.events.set(res.items); this.total.set(res.total); this.loading.set(false); },
      error: () => { this.error.set('Failed to load events.'); this.loading.set(false); },
    });
  }

  setStatus(value: EventStatus | null) {
    this.status.set(value);
    this.page.set(1);
    this.load();
  }

  go(page: number) {
    this.page.set(page);
    this.load();
  }

  pages() {
    return Math.max(1, Math.ceil(this.total() / this.pageSize));
  }

  seats(ev: ExtaticEvent) {
    return seatSummary(ev);
  }

  variant(status: EventStatus) {
    return statusVariant(status);
  }

  formatDate(d: string) {
    return new Date(d).toLocaleString();
  }
}
