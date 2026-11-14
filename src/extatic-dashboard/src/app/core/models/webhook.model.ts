/**
 * A webhook event type, e.g. 'item.created' or a feature event such as 'form.submitted'.
 * The valid set comes from GET /api/webhook-events (see WebhookEventsService).
 */
export type WebhookEvent = string;

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
