import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { ApiService } from './api.service';

export interface WebhookEventGroup {
  label: string;
  events: string[];
}

/**
 * Loads the webhook event catalog (GET /api/webhook-events): core item events plus every event
 * declared by a backend feature module. Grouped by the prefix before the first '.'.
 */
@Injectable({ providedIn: 'root' })
export class WebhookEventsService {
  private api = inject(ApiService);

  readonly events$: Observable<string[]> = this.api.get<string[]>('/webhook-events').pipe(
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  readonly groups$: Observable<WebhookEventGroup[]> = this.events$.pipe(map(groupWebhookEvents));
}

export function groupWebhookEvents(events: string[]): WebhookEventGroup[] {
  const groups = new Map<string, string[]>();
  for (const event of events) {
    const prefix = event.split('.')[0];
    groups.set(prefix, [...(groups.get(prefix) ?? []), event]);
  }
  return [...groups.entries()].map(([prefix, list]) => ({ label: groupLabel(prefix), events: list }));
}

function groupLabel(prefix: string): string {
  const words = prefix.split(/[_-]/).filter(Boolean);
  const label = words.map(w => w[0].toUpperCase() + w.slice(1)).join(' ');
  return label.endsWith('s') ? label : `${label}s`;
}
