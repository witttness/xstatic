/** Mirrors Features/Members/MembersDtos.cs (snake_case JSON). */

export type MembershipStatus = 'pending' | 'active' | 'suspended' | 'expired' | 'cancelled' | 'rejected';

export const MEMBERSHIP_STATUSES: MembershipStatus[] = ['pending', 'active', 'suspended', 'expired', 'cancelled', 'rejected'];

export type MembershipAction = 'approve' | 'reject' | 'suspend' | 'reinstate' | 'renew' | 'cancel';

/** Mirrors the server's allowed transitions (MembershipService.Transitions), minus the worker-only "expire". */
export const ACTIONS_BY_STATUS: Record<MembershipStatus, MembershipAction[]> = {
  pending: ['approve', 'reject', 'cancel'],
  active: ['suspend', 'renew', 'cancel'],
  suspended: ['reinstate', 'cancel'],
  expired: ['renew'],
  cancelled: [],
  rejected: [],
};

export interface MembershipPlan {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  price_label: string | null;
  duration_days: number | null;
  requires_approval: boolean;
  self_signup_enabled: boolean;
  max_members: number | null;
  benefits: string[];
  is_active: boolean;
  sort_order: number;
  active_members: number;
  total_memberships: number;
  created_at: string;
  updated_at: string;
}

export interface MembershipPlanRequest {
  name: string;
  slug: string;
  description: string | null;
  price_label: string | null;
  duration_days: number | null;
  requires_approval: boolean;
  self_signup_enabled: boolean;
  max_members: number | null;
  benefits: string[];
  is_active: boolean;
  sort_order: number;
}

export interface MembershipSummary {
  id: string;
  app_user_id: string;
  display_name: string | null;
  email: string | null;
  plan_id: string;
  plan_slug: string;
  plan_name: string;
  status: MembershipStatus;
  member_number: string | null;
  starts_at: string | null;
  expires_at: string | null;
  approved_at: string | null;
  cancelled_at: string | null;
  show_in_directory: boolean;
  created_at: string;
  updated_at: string;
}

export interface MembershipEvent {
  id: string;
  type: string;
  from_status: MembershipStatus | null;
  to_status: MembershipStatus | null;
  actor_user_id: string | null;
  actor_app_user_id: string | null;
  detail: string | null;
  created_at: string;
}

export interface MembershipDetail {
  membership: MembershipSummary;
  profile: Record<string, unknown>;
  notes: string | null;
  approved_by_user_id: string | null;
  reminders_sent: number[];
  events: MembershipEvent[];
}

export interface MemberSettings {
  profile_schema: Record<string, unknown> | null;
  directory_enabled: boolean;
  directory_fields: string[];
  member_number_prefix: string;
  next_member_number: number;
  notify_emails: string[];
  renewal_reminder_days: number[];
}

export interface MembersStats {
  total: number;
  by_status: Record<MembershipStatus, number>;
  by_plan: { plan_id: string; slug: string; name: string; by_status: Record<MembershipStatus, number>; total: number }[];
  expiring_soon: number;
}

export function statusVariant(status: MembershipStatus): 'success' | 'error' | 'warning' | 'muted' | 'info' {
  switch (status) {
    case 'active': return 'success';
    case 'pending': return 'info';
    case 'suspended': return 'warning';
    case 'rejected': return 'error';
    default: return 'muted';
  }
}

export function actionLabel(action: MembershipAction): string {
  return action.charAt(0).toUpperCase() + action.slice(1);
}

/** Saves a Blob as a file via a temporary object URL. */
export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
