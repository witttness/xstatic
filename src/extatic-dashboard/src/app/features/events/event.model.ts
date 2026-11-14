export type EventStatus = 'draft' | 'published' | 'cancelled';
export type RegistrationStatus = 'confirmed' | 'waitlisted' | 'cancelled';

export interface EventStats {
  confirmed_registrations: number;
  confirmed_seats: number;
  waitlisted_registrations: number;
  waitlisted_seats: number;
  cancelled_registrations: number;
  checked_in_registrations: number;
  checked_in_seats: number;
  capacity: number | null;
  seats_remaining: number | null;
}

export interface ExtaticEvent {
  id: string;
  title: string;
  slug: string;
  description: string | null;
  location: string | null;
  online_url: string | null;
  starts_at: string;
  ends_at: string;
  timezone: string;
  capacity: number | null;
  waitlist_enabled: boolean;
  waitlist_capacity: number | null;
  max_seats_per_registration: number;
  registration_opens_at: string | null;
  registration_closes_at: string | null;
  registration_schema: unknown | null;
  require_app_user: boolean;
  manage_url_template: string | null;
  notify_emails: string[];
  status: EventStatus;
  stats: EventStats;
  registration_open: boolean;
  registration_closed_reason: string | null;
  created_at: string;
  updated_at: string;
}

export interface EventRequest {
  title: string;
  slug: string;
  description: string | null;
  location: string | null;
  online_url: string | null;
  starts_at: string;
  ends_at: string;
  timezone: string;
  capacity: number | null;
  waitlist_enabled: boolean;
  waitlist_capacity: number | null;
  max_seats_per_registration: number;
  registration_opens_at: string | null;
  registration_closes_at: string | null;
  registration_schema: unknown | null;
  require_app_user: boolean;
  manage_url_template: string | null;
  notify_emails: string[];
}

export interface EventRegistration {
  id: string;
  event_id: string;
  app_user_id: string | null;
  name: string;
  email: string;
  seats: number;
  data: Record<string, unknown>;
  status: RegistrationStatus;
  checked_in_at: string | null;
  created_at: string;
  confirmed_at: string | null;
  cancelled_at: string | null;
  promoted_at: string | null;
}

/** "42 / 50 seats · 6 waitlisted" (or "42 seats" when unlimited). */
export function seatSummary(e: Pick<ExtaticEvent, 'capacity' | 'stats'>): string {
  const taken = e.stats.confirmed_seats;
  let text = e.capacity === null ? `${taken} seats` : `${taken} / ${e.capacity} seats`;
  if (e.stats.waitlisted_registrations > 0) text += ` · ${e.stats.waitlisted_registrations} waitlisted`;
  return text;
}

export function statusVariant(status: EventStatus | RegistrationStatus): 'success' | 'warning' | 'error' | 'muted' {
  switch (status) {
    case 'published':
    case 'confirmed':
      return 'success';
    case 'waitlisted':
      return 'warning';
    case 'cancelled':
      return 'error';
    default:
      return 'muted';
  }
}
