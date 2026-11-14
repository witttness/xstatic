import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { SchedulingNavComponent } from '../scheduling-nav.component';
import { BookingCalendar } from '../scheduling.models';
import { WEEKDAYS } from '../scheduling.util';

@Component({
  selector: 'app-scheduling-calendars',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent, SchedulingNavComponent],
  template: `
    <app-page-header title="Scheduling" subtitle="Calendars are bookable people or resources with their own hours">
      <a [routerLink]="['/apps', appSlug, 'scheduling', 'calendars', 'new']"><app-button>New calendar</app-button></a>
    </app-page-header>
    <app-scheduling-nav [appSlug]="appSlug" />
    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (calendars().length === 0) {
      <app-empty-state message="No calendars yet." description="Create a calendar, set its weekly hours, then add appointment types." />
    } @else {
      <div class="border border-border divide-y divide-border">
        @for (c of calendars(); track c.id) {
          <a [routerLink]="['/apps', appSlug, 'scheduling', 'calendars', c.id]" class="flex items-center gap-4 px-4 py-3 hover:bg-surface">
            <div class="flex-1">
              <div class="text-sm text-text-primary">{{ c.name }} <span class="font-mono text-xs text-text-muted">{{ c.slug }}</span></div>
              <div class="text-xs text-text-muted">{{ c.timezone }} · {{ summary(c) }}</div>
            </div>
            @if (c.is_active) { <app-badge variant="success">Active</app-badge> } @else { <app-badge>Inactive</app-badge> }
          </a>
        }
      </div>
    }
  `,
})
export class CalendarsListComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  calendars = signal<BookingCalendar[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  ngOnInit() {
    this.api.get<BookingCalendar[]>(`/apps/${this.appSlug}/scheduling/calendars`).subscribe({
      next: c => { this.calendars.set(c); this.loading.set(false); },
      error: () => { this.error.set('Failed to load calendars.'); this.loading.set(false); },
    });
  }

  summary(c: BookingCalendar) {
    const days = [...new Set(c.rules.map(r => r.day_of_week))];
    return days.length ? days.map(d => WEEKDAYS[d].slice(0, 3)).join(', ') : 'No weekly hours';
  }
}
