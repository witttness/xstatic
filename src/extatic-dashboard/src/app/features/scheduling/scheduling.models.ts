/** Mirrors Features/Scheduling/SchedulingDtos.cs (snake_case JSON). */

/** day_of_week: 0 = Sunday … 6 = Saturday. Times "HH:mm" in the calendar's zone; end "24:00" = midnight. */
export interface AvailabilityRule {
  day_of_week: number;
  start_time: string;
  end_time: string;
}

export interface BookingCalendar {
  id: string;
  name: string;
  slug: string;
  timezone: string;
  notify_emails: string[];
  is_active: boolean;
  rules: AvailabilityRule[];
  created_at: string;
  updated_at: string;
}

export interface AvailabilityOverride {
  id: string;
  date: string;
  is_unavailable: boolean;
  start_time: string | null;
  end_time: string | null;
  reason: string | null;
}

export interface AppointmentType {
  id: string;
  calendar_id: string;
  name: string;
  slug: string;
  description: string | null;
  duration_minutes: number;
  buffer_before_minutes: number;
  buffer_after_minutes: number;
  slot_interval_minutes: number;
  min_notice_minutes: number;
  max_advance_days: number;
  max_per_day: number | null;
  requires_approval: boolean;
  intake_schema: unknown | null;
  location: string | null;
  meeting_url: string | null;
  manage_url_template: string | null;
  is_active: boolean;
  created_at: string;
  updated_at: string;
}

export type AppointmentStatus = 'Pending' | 'Confirmed' | 'Cancelled' | 'Completed' | 'NoShow';

export interface Appointment {
  id: string;
  calendar_id: string;
  calendar_name: string;
  calendar_timezone: string;
  appointment_type_id: string;
  appointment_type_name: string;
  appointment_type_slug: string;
  app_user_id: string | null;
  customer_name: string;
  customer_email: string;
  customer_phone: string | null;
  customer_timezone: string | null;
  starts_at: string;
  ends_at: string;
  status: AppointmentStatus;
  data: Record<string, unknown>;
  notes: string | null;
  cancelled_at: string | null;
  cancel_reason: string | null;
  reminder_sent_at: string | null;
  created_at: string;
  updated_at: string;
}

export interface Slot {
  starts_at: string;
  ends_at: string;
  local_time: string;
}

export interface SlotDay {
  date: string;
  slots: Slot[];
}

export interface SlotsResponse {
  timezone: string;
  days: SlotDay[];
}

export type AppointmentAction = 'confirm' | 'decline' | 'cancel' | 'complete' | 'no-show' | 'reschedule';
