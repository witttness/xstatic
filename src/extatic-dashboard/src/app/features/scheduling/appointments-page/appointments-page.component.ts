import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { SchedulingNavComponent } from '../scheduling-nav.component';
import { AgendaActionEvent, AppointmentAgendaComponent } from '../appointment-agenda/appointment-agenda.component';
import { SlotPickerComponent } from '../slot-picker/slot-picker.component';
import { Appointment, AppointmentType, BookingCalendar, Slot } from '../scheduling.models';
import { ACTION_LABELS, addDays, browserTimeZone, startOfLocalDayUtc, todayIn } from '../scheduling.util';

@Component({
  selector: 'app-scheduling-appointments',
  standalone: true,
  imports: [FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent, ConfirmModalComponent,
    SchedulingNavComponent, AppointmentAgendaComponent, SlotPickerComponent],
  template: `
    <app-page-header title="Scheduling" subtitle="Appointments booked through your site">
      @if (canManage()) {
        <app-button variant="secondary" (click)="exportCsv()">Export CSV</app-button>
        <a [routerLink]="['/apps', appSlug, 'scheduling', 'book']"><app-button>New booking</app-button></a>
      }
    </app-page-header>
    <app-scheduling-nav [appSlug]="appSlug" />

    <div class="flex flex-wrap items-end gap-3 mb-6">
      <label class="text-xs text-text-muted">From
        <input type="date" [(ngModel)]="from" (change)="load()" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary" />
      </label>
      <label class="text-xs text-text-muted">To (exclusive)
        <input type="date" [(ngModel)]="to" (change)="load()" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary" />
      </label>
      <label class="text-xs text-text-muted">Calendar
        <select [(ngModel)]="calendarId" (change)="load()" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary">
          <option value="">All calendars</option>
          @for (c of calendars(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
        </select>
      </label>
      <label class="text-xs text-text-muted">Type
        <select [(ngModel)]="typeId" (change)="load()" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary">
          <option value="">All types</option>
          @for (t of types(); track t.id) { <option [value]="t.id">{{ t.name }}</option> }
        </select>
      </label>
      <label class="text-xs text-text-muted">Status
        <select [(ngModel)]="status" (change)="load()" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary">
          <option value="">Any</option>
          <option value="Pending,Confirmed">Upcoming (pending + confirmed)</option>
          <option value="Pending">Pending approval</option>
          <option value="Confirmed">Confirmed</option>
          <option value="Cancelled">Cancelled</option>
          <option value="Completed">Completed</option>
          <option value="NoShow">No-show</option>
        </select>
      </label>
      <span class="text-xs text-text-muted pb-2">Dates in {{ rangeZone() }}</span>
    </div>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else {
      <app-appointment-agenda [appointments]="appointments()" [canAct]="canManage()" (act)="onAct($event)" />
      @if (total() > appointments().length) {
        <p class="text-xs text-text-muted">Showing {{ appointments().length }} of {{ total() }}. Narrow the date range to see more.</p>
      }
    }
    @if (actionError()) { <p class="text-error text-sm mt-2">{{ actionError() }}</p> }

    @if (pending(); as p) {
      @if (p.action === 'reschedule') {
        <div class="fixed inset-0 z-50 flex items-center justify-center">
          <div class="absolute inset-0 bg-black/60" (click)="pending.set(null)"></div>
          <div class="relative bg-surface border border-border w-full max-w-lg p-6 z-10">
            <h3 class="text-base font-semibold text-text-primary mb-2">Reschedule {{ p.appointment.customer_name }}</h3>
            <app-slot-picker [appSlug]="appSlug" [calendarId]="p.appointment.calendar_id" [typeId]="p.appointment.appointment_type_id"
              [timezone]="p.appointment.calendar_timezone" (picked)="newSlot.set($event)" />
            <div class="flex justify-end gap-2 mt-4">
              <app-button variant="secondary" (click)="pending.set(null)">Cancel</app-button>
              <app-button [disabled]="!newSlot()" (click)="runAction()">Move appointment</app-button>
            </div>
          </div>
        </div>
      } @else {
        <app-confirm-modal [title]="labels[p.action]"
          [message]="labels[p.action] + ' the appointment for ' + p.appointment.customer_name + '? The customer is notified by email where applicable.'"
          [confirmLabel]="labels[p.action]" (confirmed)="runAction()" (cancelled)="pending.set(null)" />
      }
    }
  `,
})
export class AppointmentsPageComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);

  calendars = signal<BookingCalendar[]>([]);
  types = signal<AppointmentType[]>([]);
  appointments = signal<Appointment[]>([]);
  total = signal(0);
  loading = signal(true);
  error = signal<string | null>(null);
  actionError = signal<string | null>(null);
  canManage = signal(false);
  pending = signal<AgendaActionEvent | null>(null);
  newSlot = signal<Slot | null>(null);
  readonly labels = ACTION_LABELS;

  from = todayIn(browserTimeZone());
  to = addDays(this.from, 14);
  calendarId = '';
  typeId = '';
  status = '';

  rangeZone = computed(() => this.calendars().find(c => c.id === this.calendarIdSignal())?.timezone ?? browserTimeZone());
  private calendarIdSignal = signal('');

  ngOnInit() {
    this.api.get<{ can_manage_items: boolean }>(`/apps/${this.appSlug}`).subscribe({ next: a => this.canManage.set(!!a.can_manage_items), error: () => {} });
    this.api.get<BookingCalendar[]>(`/apps/${this.appSlug}/scheduling/calendars`).subscribe({ next: c => this.calendars.set(c), error: () => {} });
    this.api.get<AppointmentType[]>(`/apps/${this.appSlug}/scheduling/types`).subscribe({ next: t => this.types.set(t), error: () => {} });
    this.load();
  }

  private query() {
    this.calendarIdSignal.set(this.calendarId);
    const zone = this.rangeZone();
    return {
      from: this.from ? startOfLocalDayUtc(this.from, zone) : null,
      to: this.to ? startOfLocalDayUtc(this.to, zone) : null,
      calendar_id: this.calendarId || null,
      type_id: this.typeId || null,
      status: this.status || null,
    };
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<PagedResult<Appointment>>(`/apps/${this.appSlug}/scheduling/appointments`, { ...this.query(), page: 1, page_size: 100 })
      .subscribe({
        next: r => { this.appointments.set(r.items); this.total.set(r.total); this.loading.set(false); },
        error: () => { this.error.set('Failed to load appointments.'); this.loading.set(false); },
      });
  }

  onAct(event: AgendaActionEvent) {
    this.actionError.set(null);
    this.newSlot.set(null);
    this.pending.set(event);
  }

  runAction() {
    const p = this.pending();
    if (!p) return;
    const body = p.action === 'reschedule' ? { starts_at: this.newSlot()?.starts_at } : {};
    this.pending.set(null);
    this.api.post<Appointment>(`/apps/${this.appSlug}/scheduling/appointments/${p.appointment.id}/${p.action}`, body).subscribe({
      next: updated => this.appointments.update(list => list.map(a => a.id === updated.id ? updated : a)),
      error: err => this.actionError.set(err?.error?.detail ?? 'Action failed.'),
    });
  }

  exportCsv() {
    this.api.getBlob(`/apps/${this.appSlug}/scheduling/appointments/export.csv`, this.query()).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'appointments.csv';
      link.click();
      URL.revokeObjectURL(url);
    });
  }
}
