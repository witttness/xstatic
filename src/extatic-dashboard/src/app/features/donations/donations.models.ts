export type DonationStatus = 'pending' | 'succeeded' | 'failed' | 'expired' | 'refunded';
export type DonationInterval = 'one_time' | 'monthly' | 'yearly';

/** Never contains a secret: only presence flags and the last 4 characters. */
export interface ProviderConfig {
  provider: string;
  configured: boolean;
  publishable_key: string | null;
  has_secret_key: boolean;
  secret_key_last4: string | null;
  has_webhook_secret: boolean;
  webhook_secret_last4: string | null;
  is_live_mode: boolean;
  is_enabled: boolean;
  webhook_url: string;
  webhook_events: string[];
  updated_at: string | null;
}

export interface Campaign {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  currency: string;
  goal_amount_minor: number | null;
  suggested_amounts_minor: number[];
  min_amount_minor: number;
  max_amount_minor: number;
  allow_custom_amount: boolean;
  allow_recurring: boolean;
  success_url: string;
  cancel_url: string;
  show_donor_list: boolean;
  notify_emails: string[];
  is_active: boolean;
  starts_at: string | null;
  ends_at: string | null;
  raised_minor: number;
  donor_count: number;
  created_at: string;
  updated_at: string;
}

export type CampaignRequest = Omit<Campaign, 'id' | 'raised_minor' | 'donor_count' | 'created_at' | 'updated_at'>;

export interface Donation {
  id: string;
  campaign_id: string | null;
  campaign_slug: string | null;
  campaign_name: string | null;
  app_user_id: string | null;
  amount_minor: number;
  refunded_amount_minor: number;
  currency: string;
  interval: DonationInterval;
  status: DonationStatus;
  donor_name: string | null;
  donor_email: string | null;
  is_anonymous: boolean;
  message: string | null;
  provider: string;
  provider_session_id: string | null;
  provider_payment_id: string | null;
  provider_subscription_id: string | null;
  completed_at: string | null;
  created_at: string;
}

export interface CurrencyAmount {
  currency: string;
  amount_minor: number;
  donation_count: number;
}

export interface DonationSummary {
  total_raised: CurrencyAmount[];
  last_30_days: CurrencyAmount[];
  recurring_count: number;
  pending_count: number;
}
