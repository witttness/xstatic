import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { CopyButtonComponent } from '../../../shared/copy-button/copy-button.component';
import { SchedulingNavComponent } from '../scheduling-nav.component';
import { AppointmentType } from '../scheduling.models';

/** Embed/integration panel: endpoints and a copy-paste fetch() example for the static site. */
@Component({
  selector: 'app-scheduling-integration',
  standalone: true,
  imports: [PageHeaderComponent, CopyButtonComponent, SchedulingNavComponent],
  template: `
    <app-page-header title="Scheduling" subtitle="Add booking to your static site" />
    <app-scheduling-nav [appSlug]="appSlug" />

    <div class="max-w-3xl space-y-6 text-sm text-text-primary">
      <div>
        <h3 class="font-semibold mb-2">Endpoints</h3>
        <table class="w-full text-xs font-mono border border-border">
          <tbody>
            @for (e of endpoints(); track e[1]) {
              <tr class="border-b border-border"><td class="px-3 py-1.5 text-accent w-16">{{ e[0] }}</td><td class="px-3 py-1.5">{{ e[1] }}</td><td class="px-3 py-1.5 font-sans text-text-muted">{{ e[2] }}</td></tr>
            }
          </tbody>
        </table>
        <p class="text-xs text-text-muted mt-2">Your site's origin must be listed in the App's allowed origins. The manage token is returned once at booking and in the booking email (when a manage page URL is set on the type); store it client-side only.</p>
      </div>
      <div>
        <div class="flex items-center justify-between mb-2">
          <h3 class="font-semibold">fetch() example</h3>
          <app-copy-button [value]="example()" />
        </div>
        <pre class="bg-surface border border-border p-4 text-xs overflow-x-auto"><code>{{ example() }}</code></pre>
      </div>
    </div>
  `,
})
export class IntegrationComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  publicId = signal('app_…');
  typeSlug = signal('consultation');

  base = computed(() => `${location.origin}/api/${this.publicId()}/scheduling`);
  endpoints = computed(() => [
    ['GET', '/types', 'Active appointment types'],
    ['GET', '/types/{slug}/slots?from=YYYY-MM-DD&to=YYYY-MM-DD&timezone=IANA', 'Free slots grouped by local date (max 62 days)'],
    ['POST', '/types/{slug}/appointments', 'Book: { starts_at, name, email, phone?, timezone?, data }'],
    ['GET', '/appointments/{token}', 'View a booking'],
    ['GET', '/appointments/{token}/calendar.ics', 'Add to calendar'],
    ['POST', '/appointments/{token}/cancel', 'Cancel: { reason? }'],
    ['POST', '/appointments/{token}/reschedule', 'Move: { starts_at }'],
    ['GET', '/me/appointments', 'Bookings of the signed-in AppUser (Bearer token)'],
  ]);

  example = computed(() => `const API = '${this.base()}';
const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;

// 1. Free slots for the next 7 days, grouped by the visitor's local date
const from = new Date().toISOString().slice(0, 10);
const to = new Date(Date.now() + 7 * 864e5).toISOString().slice(0, 10);
const { days } = await fetch(\`\${API}/types/${this.typeSlug()}/slots?from=\${from}&to=\${to}&timezone=\${tz}\`).then(r => r.json());

// 2. Book the first slot (starts_at is the UTC instant from the slots response)
const res = await fetch(\`\${API}/types/${this.typeSlug()}/appointments\`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    starts_at: days[0].slots[0].starts_at,
    name: 'Casey Customer',
    email: 'casey@example.com',
    timezone: tz,
    data: {},            // intake answers, validated against the type's schema (422 on error)
  }),
});
if (res.status === 409) alert('That slot was just taken — please pick another.');
const { appointment, manage_token } = await res.json();   // status: "Confirmed" or "Pending"

// 3. Later: cancel with the manage token
await fetch(\`\${API}/appointments/\${manage_token}/cancel\`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });`);

  ngOnInit() {
    this.api.get<{ public_id: string }>(`/apps/${this.appSlug}`).subscribe({ next: a => this.publicId.set(a.public_id), error: () => {} });
    this.api.get<AppointmentType[]>(`/apps/${this.appSlug}/scheduling/types`).subscribe({
      next: t => { const first = t.find(x => x.is_active); if (first) this.typeSlug.set(first.slug); },
      error: () => {},
    });
  }
}
