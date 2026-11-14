import { describe, expect, it } from 'vitest';
import { groupWebhookEvents } from './webhook-events.service';

describe('groupWebhookEvents', () => {
  it('groups by prefix in catalog order', () => {
    expect(groupWebhookEvents(['item.created', 'item.deleted', 'form.submitted'])).toEqual([
      { label: 'Items', events: ['item.created', 'item.deleted'] },
      { label: 'Forms', events: ['form.submitted'] },
    ]);
  });
});
