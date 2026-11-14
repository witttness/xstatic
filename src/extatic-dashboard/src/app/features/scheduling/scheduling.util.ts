import { Appointment, AppointmentAction, AvailabilityRule } from './scheduling.models';

export const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
/** Display order Monday → Sunday. */
export const WEEKDAY_ORDER = [1, 2, 3, 4, 5, 6, 0];

export function minutes(time: string): number {
  const [h, m] = time.split(':').map(Number);
  return h * 60 + (m || 0);
}

/** End-of-range minutes; "00:00" and "24:00" both mean midnight at the end of the day. */
export function endMinutes(time: string): number {
  const value = minutes(time);
  return value === 0 ? 24 * 60 : value;
}

export function sortRules(rules: AvailabilityRule[]): AvailabilityRule[] {
  return [...rules].sort((a, b) => a.day_of_week - b.day_of_week || minutes(a.start_time) - minutes(b.start_time));
}

/** Suggests the next range for a day: one hour after the last range, else 09:00–17:00. */
export function addRange(rules: AvailabilityRule[], day: number): AvailabilityRule[] {
  const sameDay = sortRules(rules.filter(r => r.day_of_week === day));
  const last = sameDay[sameDay.length - 1];
  let range: AvailabilityRule = { day_of_week: day, start_time: '09:00', end_time: '17:00' };
  if (last) {
    const start = Math.min(endMinutes(last.end_time) + 60, 23 * 60);
    const end = Math.min(start + 60, 24 * 60);
    range = { day_of_week: day, start_time: toTime(start), end_time: toTime(end) };
  }
  return sortRules([...rules, range]);
}

export function removeRange(rules: AvailabilityRule[], index: number): AvailabilityRule[] {
  return rules.filter((_, i) => i !== index);
}

export function toTime(totalMinutes: number): string {
  const h = Math.floor(totalMinutes / 60);
  const m = totalMinutes % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

/** Same rules as the API: start before end, no overlaps on a day (adjacent is fine). */
export function validateRules(rules: AvailabilityRule[]): string[] {
  const errors: string[] = [];
  for (const day of WEEKDAY_ORDER) {
    const sameDay = sortRules(rules.filter(r => r.day_of_week === day));
    for (const r of sameDay) {
      if (!/^\d{1,2}:\d{2}$/.test(r.start_time) || !/^\d{1,2}:\d{2}$/.test(r.end_time)) {
        errors.push(`${WEEKDAYS[day]}: times must be HH:mm`);
      } else if (minutes(r.start_time) >= endMinutes(r.end_time)) {
        errors.push(`${WEEKDAYS[day]}: ${r.start_time}–${r.end_time} ends before it starts`);
      }
    }
    for (let i = 1; i < sameDay.length; i++) {
      if (minutes(sameDay[i].start_time) < endMinutes(sameDay[i - 1].end_time)) {
        errors.push(`${WEEKDAYS[day]}: ${sameDay[i - 1].start_time}–${sameDay[i - 1].end_time} overlaps ${sameDay[i].start_time}–${sameDay[i].end_time}`);
      }
    }
  }
  return errors;
}

/** "YYYY-MM-DD" of an instant in a time zone. */
export function localDate(iso: string, timeZone: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(new Date(iso));
  const get = (t: string) => parts.find(p => p.type === t)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}

export function formatTime(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-GB', { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(iso));
}

export function formatDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
    .format(new Date(Date.UTC(y, m - 1, d)));
}

/** Offset (minutes) of a zone at an instant: local wall time − UTC. */
export function zoneOffsetMinutes(instant: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(instant);
  const get = (t: string) => Number(parts.find(p => p.type === t)?.value);
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour') % 24, get('minute'), get('second'));
  return Math.round((asUtc - instant.getTime()) / 60000);
}

/** UTC ISO instant of local midnight starting `date` (YYYY-MM-DD) in `timeZone`. */
export function startOfLocalDayUtc(date: string, timeZone: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const guess = Date.UTC(y, m - 1, d);
  let instant = guess - zoneOffsetMinutes(new Date(guess), timeZone) * 60000;
  instant = guess - zoneOffsetMinutes(new Date(instant), timeZone) * 60000;
  return new Date(instant).toISOString();
}

export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

export interface AgendaDay {
  date: string;
  label: string;
  appointments: Appointment[];
}

/** Groups appointments by local date in each appointment's calendar zone, chronologically. */
export function groupAgenda(appointments: Appointment[]): AgendaDay[] {
  const map = new Map<string, Appointment[]>();
  for (const a of [...appointments].sort((x, y) => x.starts_at.localeCompare(y.starts_at))) {
    const key = localDate(a.starts_at, a.calendar_timezone);
    map.set(key, [...(map.get(key) ?? []), a]);
  }
  return [...map.entries()].sort(([a], [b]) => a.localeCompare(b))
    .map(([date, list]) => ({ date, label: formatDay(date), appointments: list }));
}

/** Actions the API allows from each status. */
export function actionsFor(status: Appointment['status']): AppointmentAction[] {
  switch (status) {
    case 'Pending': return ['confirm', 'decline', 'reschedule'];
    case 'Confirmed': return ['complete', 'no-show', 'reschedule', 'cancel'];
    default: return [];
  }
}

export const ACTION_LABELS: Record<AppointmentAction, string> = {
  confirm: 'Confirm',
  decline: 'Decline',
  cancel: 'Cancel',
  complete: 'Mark completed',
  'no-show': 'No-show',
  reschedule: 'Reschedule',
};

/** Browser's zone, falling back to UTC. */
export function browserTimeZone(): string {
  try { return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'; } catch { return 'UTC'; }
}

export function todayIn(timeZone: string): string {
  return localDate(new Date().toISOString(), timeZone);
}

/** Message from an API ProblemDetails error (422 lists field errors). */
export function problem(err: unknown, fallback: string): string {
  type FieldError = { pointer: string; message: string };
  const e = (err as { error?: { detail?: string; errors?: FieldError[] | { errors?: FieldError[] } } })?.error;
  // GlobalExceptionHandler nests field errors as { errors: { errors: [...] } }.
  const list = Array.isArray(e?.errors) ? e.errors : e?.errors?.errors;
  if (list?.length) return list.map(x => `${x.pointer}: ${x.message}`).join('; ');
  return e?.detail ?? fallback;
}
