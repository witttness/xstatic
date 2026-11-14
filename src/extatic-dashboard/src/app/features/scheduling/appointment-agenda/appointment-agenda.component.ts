import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { Appointment, AppointmentAction } from '../scheduling.models';
import { ACTION_LABELS, actionsFor, formatTime, groupAgenda } from '../scheduling.util';

export interface AgendaActionEvent {
  appointment: Appointment;
  action: AppointmentAction;
}

/** Agenda list grouped by local day; times shown in each appointment's calendar zone, which is labelled. */
@Component({
  selector: 'app-appointment-agenda',
  standalone: true,
  imports: [BadgeComponent],
  template: `
    @for (day of days(); track day.date) {
      <section class="mb-6" [attr.data-date]="day.date">
        <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider mb-2">{{ day.label }}</h3>
        <div class="border border-border divide-y divide-border">
          @for (a of day.appointments; track a.id) {
            <div class="px-4 py-3 flex flex-wrap items-start gap-4" data-appointment>
              <div class="w-40 shrink-0">
                <div class="text-sm text-text-primary font-mono">{{ time(a.starts_at, a) }}–{{ time(a.ends_at, a) }}</div>
                <div class="text-xs text-text-muted" data-zone>{{ a.calendar_timezone }}</div>
              </div>
              <div class="flex-1 min-w-48">
                <div class="text-sm text-text-primary">{{ a.customer_name }}
                  <span class="text-text-muted">· {{ a.appointment_type_name }} · {{ a.calendar_name }}</span></div>
                <div class="text-xs text-text-muted">{{ a.customer_email }}@if (a.customer_phone) { · {{ a.customer_phone }} }</div>
                @if (a.notes) { <div class="text-xs text-text-muted mt-1 italic">{{ a.notes }}</div> }
                @if (a.cancel_reason) { <div class="text-xs text-text-muted mt-1">Reason: {{ a.cancel_reason }}</div> }
              </div>
              <app-badge [variant]="badge(a.status)">{{ a.status === 'NoShow' ? 'No-show' : a.status }}</app-badge>
              @if (canAct) {
                <div class="flex flex-wrap gap-3 w-full sm:w-auto justify-end">
                  @for (action of actions(a); track action) {
                    <button type="button" (click)="act.emit({ appointment: a, action })"
                      [class]="action === 'cancel' || action === 'decline' ? 'text-xs text-error hover:text-red-400' : 'text-xs text-text-muted hover:text-text-primary'">
                      {{ labels[action] }}</button>
                  }
                </div>
              }
            </div>
          }
        </div>
      </section>
    } @empty {
      <p class="text-sm text-text-muted py-8 text-center">No appointments in this range.</p>
    }
  `,
})
export class AppointmentAgendaComponent {
  @Input() set appointments(value: Appointment[] | null) { this.items.set(value ?? []); }
  @Input() canAct = true;
  @Output() act = new EventEmitter<AgendaActionEvent>();

  readonly items = signal<Appointment[]>([]);
  readonly days = computed(() => groupAgenda(this.items()));
  readonly labels = ACTION_LABELS;

  time(iso: string, a: Appointment) { return formatTime(iso, a.calendar_timezone); }
  actions(a: Appointment) { return actionsFor(a.status); }

  badge(status: Appointment['status']): 'success' | 'warning' | 'muted' | 'info' | 'error' {
    switch (status) {
      case 'Confirmed': return 'success';
      case 'Pending': return 'warning';
      case 'Completed': return 'info';
      case 'NoShow': return 'error';
      default: return 'muted';
    }
  }
}
