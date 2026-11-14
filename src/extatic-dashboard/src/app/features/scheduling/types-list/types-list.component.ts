import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { SchedulingNavComponent } from '../scheduling-nav.component';
import { AppointmentType, BookingCalendar } from '../scheduling.models';

@Component({
  selector: 'app-scheduling-types',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent, SchedulingNavComponent],
  template: `
    <app-page-header title="Scheduling" subtitle="What customers can book, and on which calendar">
      <a [routerLink]="['/apps', appSlug, 'scheduling', 'types', 'new']"><app-button>New appointment type</app-button></a>
    </app-page-header>
    <app-scheduling-nav [appSlug]="appSlug" />
    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (types().length === 0) {
      <app-empty-state message="No appointment types yet." description="Appointment types define duration, buffers, notice and approval." />
    } @else {
      <div class="border border-border divide-y divide-border">
        @for (t of types(); track t.id) {
          <a [routerLink]="['/apps', appSlug, 'scheduling', 'types', t.id]" class="flex items-center gap-4 px-4 py-3 hover:bg-surface">
            <div class="flex-1">
              <div class="text-sm text-text-primary">{{ t.name }} <span class="font-mono text-xs text-text-muted">{{ t.slug }}</span></div>
              <div class="text-xs text-text-muted">{{ t.duration_minutes }} min · {{ calendarName(t.calendar_id) }}@if (t.requires_approval) { · needs approval }</div>
            </div>
            @if (t.is_active) { <app-badge variant="success">Active</app-badge> } @else { <app-badge>Inactive</app-badge> }
          </a>
        }
      </div>
    }
  `,
})
export class TypesListComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  types = signal<AppointmentType[]>([]);
  calendars = signal<BookingCalendar[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  ngOnInit() {
    forkJoin({
      types: this.api.get<AppointmentType[]>(`/apps/${this.appSlug}/scheduling/types`),
      calendars: this.api.get<BookingCalendar[]>(`/apps/${this.appSlug}/scheduling/calendars`),
    }).subscribe({
      next: r => { this.types.set(r.types); this.calendars.set(r.calendars); this.loading.set(false); },
      error: () => { this.error.set('Failed to load appointment types.'); this.loading.set(false); },
    });
  }

  calendarName(id: string) { return this.calendars().find(c => c.id === id)?.name ?? '—'; }
}
