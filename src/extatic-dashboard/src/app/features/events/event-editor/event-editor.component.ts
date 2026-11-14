import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EventRequest, ExtaticEvent } from '../event.model';

/** ISO (UTC) → value for <input type="datetime-local"> in the browser's local time. */
export function toLocalInput(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** <input type="datetime-local"> value (browser local time) → ISO UTC, or null when empty. */
export function fromLocalInput(value: string | null | undefined): string | null {
  return value ? new Date(value).toISOString() : null;
}

function optionalNumber(value: unknown): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

const inputClass =
  'w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';

@Component({
  selector: 'app-event-editor',
  standalone: true,
  imports: [ReactiveFormsModule, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent],
  template: `
    <app-page-header [title]="id ? 'Edit Event' : 'New Event'" />

    @if (loading()) {
      <app-loading-spinner />
    } @else {
      <form [formGroup]="form" (ngSubmit)="save()" class="max-w-2xl space-y-5">
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Title</label>
            <input formControlName="title" (input)="suggestSlug()" class="${inputClass}" />
            @if (fieldError('title')) { <p class="text-xs text-error mt-1">{{ fieldError('title') }}</p> }
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Slug</label>
            <input formControlName="slug" class="${inputClass} font-mono" />
            @if (fieldError('slug')) { <p class="text-xs text-error mt-1">{{ fieldError('slug') }}</p> }
          </div>
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Description</label>
          <textarea formControlName="description" rows="4" class="${inputClass}"></textarea>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Location</label>
            <input formControlName="location" class="${inputClass}" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Online URL</label>
            <input formControlName="online_url" type="url" class="${inputClass}" placeholder="https://" />
            <p class="text-xs text-text-muted mt-1">Shown only to confirmed registrants.</p>
            @if (fieldError('online_url')) { <p class="text-xs text-error mt-1">{{ fieldError('online_url') }}</p> }
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Starts (local time)</label>
            <input formControlName="starts_at" type="datetime-local" class="${inputClass}" />
            @if (fieldError('starts_at')) { <p class="text-xs text-error mt-1">{{ fieldError('starts_at') }}</p> }
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Ends (local time)</label>
            <input formControlName="ends_at" type="datetime-local" class="${inputClass}" />
            @if (fieldError('ends_at')) { <p class="text-xs text-error mt-1">{{ fieldError('ends_at') }}</p> }
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Time zone (IANA)</label>
            <input formControlName="timezone" class="${inputClass} font-mono" placeholder="Europe/London" />
            @if (fieldError('timezone')) { <p class="text-xs text-error mt-1">{{ fieldError('timezone') }}</p> }
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Capacity (seats)</label>
            <input formControlName="capacity" type="number" min="1" class="${inputClass}" placeholder="Unlimited" />
            @if (fieldError('capacity')) { <p class="text-xs text-error mt-1">{{ fieldError('capacity') }}</p> }
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Max seats per registration</label>
            <input formControlName="max_seats_per_registration" type="number" min="1" max="100" class="${inputClass}" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Waitlist capacity</label>
            <input formControlName="waitlist_capacity" type="number" min="1" class="${inputClass}" placeholder="Unlimited" />
          </div>
        </div>

        <div class="flex flex-wrap gap-6">
          <label class="flex items-center gap-2 text-sm text-text-primary">
            <input type="checkbox" formControlName="waitlist_enabled" class="accent-accent" /> Waitlist when full
          </label>
          <label class="flex items-center gap-2 text-sm text-text-primary">
            <input type="checkbox" formControlName="require_app_user" class="accent-accent" /> Require signed-in AppUser
          </label>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Registration opens</label>
            <input formControlName="registration_opens_at" type="datetime-local" class="${inputClass}" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Registration closes</label>
            <input formControlName="registration_closes_at" type="datetime-local" class="${inputClass}" />
            @if (fieldError('registration_closes_at')) { <p class="text-xs text-error mt-1">{{ fieldError('registration_closes_at') }}</p> }
          </div>
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Manage URL template</label>
          <input formControlName="manage_url_template" class="${inputClass} font-mono" placeholder="https://your-site.example/events/manage?token={token}" />
          <p class="text-xs text-text-muted mt-1">Included in emails so registrants can view or cancel. Must contain {{ '{token}' }}.</p>
          @if (fieldError('manage_url_template')) { <p class="text-xs text-error mt-1">{{ fieldError('manage_url_template') }}</p> }
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Notify emails (comma separated)</label>
          <input formControlName="notify_emails" class="${inputClass}" placeholder="organiser@example.com" />
          @if (notifyError()) { <p class="text-xs text-error mt-1">{{ notifyError() }}</p> }
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Registration schema (JSON Schema, optional)</label>
          <textarea formControlName="registration_schema" rows="6" class="${inputClass} font-mono text-xs"
            placeholder='{"type":"object","properties":{"diet":{"type":"string"}}}'></textarea>
          @if (schemaError()) { <p class="text-xs text-error mt-1">{{ schemaError() }}</p> }
        </div>

        @if (error()) { <p class="text-sm text-error">{{ error() }}</p> }

        <div class="flex gap-3 pt-2">
          <app-button type="submit" [loading]="saving()">{{ id ? 'Save Changes' : 'Create Event' }}</app-button>
          <app-button variant="secondary" type="button" (click)="cancel()">Cancel</app-button>
        </div>
      </form>
    }
  `,
})
export class EventEditorComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id?: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  loading = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);
  schemaError = signal<string | null>(null);
  serverErrors = signal<Record<string, string>>({});
  private slugTouched = false;

  form = this.fb.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    slug: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]{1,100}$/)]],
    description: [''],
    location: [''],
    online_url: [''],
    starts_at: ['', Validators.required],
    ends_at: ['', Validators.required],
    timezone: [Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC', Validators.required],
    capacity: [null as number | null],
    waitlist_enabled: [false],
    waitlist_capacity: [null as number | null],
    max_seats_per_registration: [1 as number | null],
    registration_opens_at: [''],
    registration_closes_at: [''],
    require_app_user: [false],
    manage_url_template: [''],
    notify_emails: [''],
    registration_schema: [''],
  });

  ngOnInit() {
    if (!this.id) return;
    this.slugTouched = true;
    this.loading.set(true);
    this.api.get<ExtaticEvent>(`/apps/${this.appSlug}/events/${this.id}`).subscribe({
      next: ev => { this.patch(ev); this.loading.set(false); },
      error: () => { this.error.set('Failed to load event.'); this.loading.set(false); },
    });
  }

  patch(ev: ExtaticEvent) {
    this.form.patchValue({
      title: ev.title,
      slug: ev.slug,
      description: ev.description ?? '',
      location: ev.location ?? '',
      online_url: ev.online_url ?? '',
      starts_at: toLocalInput(ev.starts_at),
      ends_at: toLocalInput(ev.ends_at),
      timezone: ev.timezone,
      capacity: ev.capacity,
      waitlist_enabled: ev.waitlist_enabled,
      waitlist_capacity: ev.waitlist_capacity,
      max_seats_per_registration: ev.max_seats_per_registration,
      registration_opens_at: toLocalInput(ev.registration_opens_at),
      registration_closes_at: toLocalInput(ev.registration_closes_at),
      require_app_user: ev.require_app_user,
      manage_url_template: ev.manage_url_template ?? '',
      notify_emails: ev.notify_emails.join(', '),
      registration_schema: ev.registration_schema ? JSON.stringify(ev.registration_schema, null, 2) : '',
    });
  }

  suggestSlug() {
    if (this.id || this.form.controls.slug.dirty) return;
    const slug = (this.form.value.title ?? '')
      .toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 100);
    this.form.controls.slug.setValue(slug);
  }

  fieldError(field: string): string | null {
    const server = this.serverErrors()[field];
    if (server) return server;
    const control = this.form.get(field);
    if (!control || !control.invalid || !control.touched) return null;
    if (field === 'slug') return 'Lowercase letters, digits and hyphens only.';
    return 'Required.';
  }

  notifyError() {
    return Object.entries(this.serverErrors()).find(([k]) => k.startsWith('notify_emails'))?.[1] ?? null;
  }

  /** Builds the API request, or null (with schemaError set) when the schema JSON is invalid. */
  buildRequest(): EventRequest | null {
    const v = this.form.getRawValue();
    let schema: unknown = null;
    this.schemaError.set(null);
    if (v.registration_schema?.trim()) {
      try {
        schema = JSON.parse(v.registration_schema);
      } catch {
        this.schemaError.set('Registration schema is not valid JSON.');
        return null;
      }
    }
    const blank = (s: string | null | undefined) => (s && s.trim() ? s.trim() : null);
    return {
      title: (v.title ?? '').trim(),
      slug: (v.slug ?? '').trim(),
      description: blank(v.description),
      location: blank(v.location),
      online_url: blank(v.online_url),
      starts_at: fromLocalInput(v.starts_at)!,
      ends_at: fromLocalInput(v.ends_at)!,
      timezone: (v.timezone ?? '').trim(),
      capacity: optionalNumber(v.capacity),
      waitlist_enabled: !!v.waitlist_enabled,
      waitlist_capacity: optionalNumber(v.waitlist_capacity),
      max_seats_per_registration: optionalNumber(v.max_seats_per_registration) ?? 1,
      registration_opens_at: fromLocalInput(v.registration_opens_at),
      registration_closes_at: fromLocalInput(v.registration_closes_at),
      registration_schema: schema,
      require_app_user: !!v.require_app_user,
      manage_url_template: blank(v.manage_url_template),
      notify_emails: (v.notify_emails ?? '').split(',').map(s => s.trim()).filter(Boolean),
    };
  }

  save() {
    this.form.markAllAsTouched();
    this.serverErrors.set({});
    this.error.set(null);
    if (this.form.invalid) return;
    const body = this.buildRequest();
    if (!body) return;
    this.saving.set(true);
    const request = this.id
      ? this.api.put<ExtaticEvent>(`/apps/${this.appSlug}/events/${this.id}`, body)
      : this.api.post<ExtaticEvent>(`/apps/${this.appSlug}/events`, body);
    request.subscribe({
      next: ev => { this.saving.set(false); this.router.navigate(['/apps', this.appSlug, 'events', ev.id]); },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const errors = err.error?.errors as { pointer: string; message: string }[] | undefined;
        if (err.status === 422 && Array.isArray(errors)) {
          const map: Record<string, string> = {};
          for (const e of errors) map[e.pointer.replace(/^\//, '')] = e.message;
          this.serverErrors.set(map);
          if (map['registration_schema']) this.schemaError.set(map['registration_schema']);
          this.error.set('Please fix the highlighted fields.');
        } else {
          this.error.set(err.error?.detail ?? 'Failed to save event.');
        }
      },
    });
  }

  cancel() {
    this.router.navigate(this.id ? ['/apps', this.appSlug, 'events', this.id] : ['/apps', this.appSlug, 'events']);
  }
}
