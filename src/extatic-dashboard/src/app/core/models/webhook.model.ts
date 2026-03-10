export type WebhookEvent =
  | 'item.created'
  | 'item.updated'
  | 'item.deleted'
  | 'appuser.created'
  | 'appuser.updated'
  | 'attachment.created'
  | 'attachment.deleted';

export interface Webhook {
  id: string;
  app_id: string;
  url: string;
  events: WebhookEvent[];
  is_active: boolean;
  created_at: string;
  updated_at: string;
}

export interface WebhookDeliveryLog {
  id: string;
  webhook_id: string;
  event_type: WebhookEvent;
  payload: unknown;
  status_code: number | null;
  response_body: string | null;
  attempt_number: number;
  next_retry_at: string | null;
  succeeded_at: string | null;
  created_at: string;
}

export interface CreateWebhookRequest {
  url: string;
  events: WebhookEvent[];
}

export interface UpdateWebhookRequest {
  url?: string;
  events?: WebhookEvent[];
  is_active?: boolean;
}
