import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { AgendaActionEvent, AppointmentAgendaComponent } from './appointment-agenda.component';
import { Appointment } from '../scheduling.models';
import { actionsFor, groupAgenda, startOfLocalDayUtc } from '../scheduling.util';

function appt(id: string, startsAt: string, endsAt: string, overrides: Partial<Appointment> = {}): Appointment {
  return {
    id, calendar_id: 'c1', calendar_name: 'Dr. Smith', calendar_timezone: 'America/New_York',
    appointment_type_id: 't1', appointment_type_name: 'Consultation', appointment_type_slug: 'consult', app_user_id: null,
    customer_name: `Customer ${id}`, customer_email: `${id}@example.com`, customer_phone: null, customer_timezone: null,
    starts_at: startsAt, ends_at: endsAt, status: 'Confirmed', data: {}, notes: null, cancelled_at: null, cancel_reason: null,
    reminder_sent_at: null, created_at: startsAt, updated_at: startsAt, ...overrides,
  };
}

describe('agenda helpers', () => {
  it('groups by local date in the calendar zone, sorted chronologically', () => {
    // 2026-03-03T02:00Z is still 2 March in New York (21:00 EST).
    const days = groupAgenda([
      appt('b', '2026-03-03T15:00:00Z', '2026-03-03T15:30:00Z'),
      appt('a', '2026-03-03T02:00:00Z', '2026-03-03T02:30:00Z'),
      appt('c', '2026-03-02T14:00:00Z', '2026-03-02T14:30:00Z'),
    ]);
    expect(days.map(d => d.date)).toEqual(['2026-03-02', '2026-03-03']);
    expect(days[0].appointments.map(a => a.id)).toEqual(['c', 'a']);
    expect(days[0].label).toMatch(/^Monday,? 2 March 2026$/); // ICU versions differ on the comma
  });

  it('offers only the transitions the API allows', () => {
    expect(actionsFor('Pending')).toEqual(['confirm', 'decline', 'reschedule']);
    expect(actionsFor('Confirmed')).toEqual(['complete', 'no-show', 'reschedule', 'cancel']);
    expect(actionsFor('Cancelled')).toEqual([]);
    expect(actionsFor('Completed')).toEqual([]);
  });

  it('converts a local date to the UTC instant of its midnight across DST', () => {
    expect(startOfLocalDayUtc('2026-03-08', 'America/New_York')).toBe('2026-03-08T05:00:00.000Z');
    expect(startOfLocalDayUtc('2026-03-09', 'America/New_York')).toBe('2026-03-09T04:00:00.000Z');
    expect(startOfLocalDayUtc('2026-07-01', 'Europe/Berlin')).toBe('2026-06-30T22:00:00.000Z');
  });
});

describe('AppointmentAgendaComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [AppointmentAgendaComponent] }));

  function render(list: Appointment[], canAct = true) {
    const fixture = TestBed.createComponent(AppointmentAgendaComponent);
    fixture.componentRef.setInput('appointments', list);
    fixture.componentRef.setInput('canAct', canAct);
    const events: AgendaActionEvent[] = [];
    fixture.componentInstance.act.subscribe(e => events.push(e));
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, events };
  }

  it('shows times in the calendar zone and labels the zone', () => {
    const { el } = render([appt('a', '2026-03-02T14:00:00Z', '2026-03-02T14:30:00Z')]);
    expect(el.querySelector('[data-date="2026-03-02"]')).not.toBeNull();
    expect(el.textContent).toContain('09:00–09:30');
    expect(el.querySelector('[data-zone]')!.textContent).toContain('America/New_York');
    expect(el.textContent).toContain('Customer a');
  });

  it('emits actions for the row and hides them for read-only users', () => {
    const pending = appt('p', '2026-03-02T15:00:00Z', '2026-03-02T15:30:00Z', { status: 'Pending' });
    const { el, events } = render([pending]);
    const confirm = Array.from(el.querySelectorAll('button')).find(b => b.textContent!.trim() === 'Confirm')!;
    confirm.click();
    expect(events).toEqual([{ appointment: pending, action: 'confirm' }]);
    expect(el.textContent).toContain('Pending');

    const readOnly = render([pending], false);
    expect(readOnly.el.querySelectorAll('button').length).toBe(0);
  });

  it('shows an empty state', () => {
    expect(render([]).el.textContent).toContain('No appointments in this range.');
  });
});
