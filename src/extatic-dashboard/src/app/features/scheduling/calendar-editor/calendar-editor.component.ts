import { Component, Input, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { SchedulingNavComponent } from '../scheduling-nav.component';
import { WeeklyHoursEditorComponent } from '../weekly-hours-editor/weekly-hours-editor.component';
import { AvailabilityOverride, AvailabilityRule, BookingCalendar, SlotsResponse } from '../scheduling.models';
import { addDays, browserTimeZone, formatDay, problem, todayIn } from '../scheduling.util';

const INPUT = 'w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';
const LABEL = 'block text-xs font-medium text-text-muted uppercase tracking-wider mb-1';

@Component({
  selector: 'app-scheduling-calendar-editor',
  standalone: true,
  imports: [FormsModule, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent, ConfirmModalComponent, SchedulingNavComponent, WeeklyHoursEditorComponent],
  template: `
    <app-page-header [title]="id ? 'Edit calendar' : 'New calendar'">
      @if (id) { <app-button variant="danger" (click)="confirmDelete.set(true)">Delete</app-button> }
    </app-page-header>
    <app-scheduling-nav [appSlug]="appSlug" />

    @if (loading()) {
      <app-loading-spinner />
    } @else {
      <form class="max-w-lg space-y-5" (ngSubmit)="saveDetails()">
        <div><label [class]="label">Name</label><input name="name" [(ngModel)]="name" (input)="autoSlug()" [class]="input" placeholder="Dr. Smith" /></div>
        <div><label [class]="label">Slug</label><input name="slug" [(ngModel)]="slug" (input)="slugTouched = true" [class]="input + ' font-mono'" placeholder="dr-smith" /></div>
        <div>
          <label [class]="label">Time zone (IANA)</label>
          <input name="timezone" [(ngModel)]="timezone" list="tz-list" [class]="input + ' font-mono'" placeholder="Europe/Berlin" />
          <datalist id="tz-list">@for (z of zones; track z) { <option [value]="z"></option> }</datalist>
        </div>
        <div>
          <label [class]="label">Notify emails (comma separated)</label>
          <input name="notify" [(ngModel)]="notify" [class]="input" placeholder="frontdesk@example.com" />
        </div>
        <label class="flex items-center gap-2 text-sm text-text-primary"><input type="checkbox" name="active" [(ngModel)]="active" /> Active (bookable)</label>
        @if (detailsError()) { <p class="text-xs text-error">{{ detailsError() }}</p> }
        <app-button type="submit" [loading]="saving()">{{ id ? 'Save details' : 'Create calendar' }}</app-button>
      </form>

      @if (id) {
        <h3 class="text-sm font-semibold text-text-primary mt-10 mb-3">Weekly hours <span class="text-text-muted font-normal">({{ timezone }})</span></h3>
        <app-weekly-hours-editor #hours [rules]="rules()" [timezone]="timezone" (rulesChange)="rules.set($event); rulesSaved.set(false)" />
        <div class="mt-3 flex items-center gap-3">
          <app-button (click)="saveRules()" [disabled]="!hours.valid">Save weekly hours</app-button>
          @if (rulesSaved()) { <span class="text-xs text-success">Saved.</span> }
          @if (rulesError()) { <span class="text-xs text-error">{{ rulesError() }}</span> }
        </div>

        <h3 class="text-sm font-semibold text-text-primary mt-10 mb-3">Date overrides</h3>
        <div class="flex flex-wrap items-end gap-3 mb-3">
          <label class="text-xs text-text-muted">Date<input type="date" [(ngModel)]="ovDate" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary" /></label>
          <label class="text-xs text-text-muted flex items-center gap-1 pb-2"><input type="checkbox" [(ngModel)]="ovClosed" /> Closed all day</label>
          @if (!ovClosed) {
            <label class="text-xs text-text-muted">From<input type="time" [(ngModel)]="ovStart" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary" /></label>
            <label class="text-xs text-text-muted">To<input type="time" [(ngModel)]="ovEnd" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary" /></label>
          }
          <label class="text-xs text-text-muted">Reason<input [(ngModel)]="ovReason" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary" /></label>
          <app-button size="sm" (click)="addOverride()" [disabled]="!ovDate">Add override</app-button>
        </div>
        @if (overrideError()) { <p class="text-xs text-error mb-2">{{ overrideError() }}</p> }
        <div class="border border-border divide-y divide-border">
          @for (o of overrides(); track o.id) {
            <div class="flex items-center gap-4 px-4 py-2 text-sm">
              <span class="w-64 text-text-primary">{{ day(o.date) }}</span>
              <span class="flex-1 text-text-muted">{{ o.is_unavailable ? 'Closed' : o.start_time + '–' + o.end_time }}@if (o.reason) { · {{ o.reason }} }</span>
              <button type="button" class="text-xs text-error" (click)="deleteOverride(o)">Remove</button>
            </div>
          } @empty {
            <p class="px-4 py-3 text-xs text-text-muted">No upcoming overrides.</p>
          }
        </div>

        <h3 class="text-sm font-semibold text-text-primary mt-10 mb-3">Availability preview <span class="text-text-muted font-normal">(next 7 days, 30-minute slots, {{ timezone }})</span></h3>
        <div class="space-y-1 text-xs">
          @for (d of preview(); track d.date) {
            <div><span class="inline-block w-56 text-text-muted">{{ day(d.date) }}</span>
              <span class="font-mono text-text-primary">{{ d.slots.length ? d.slots[0].local_time + '–' + d.slots[d.slots.length - 1].local_time + ' (' + d.slots.length + ' slots)' : '—' }}</span></div>
          } @empty { <p class="text-text-muted">No availability in the next 7 days.</p> }
        </div>
      }
    }

    @if (confirmDelete()) {
      <app-confirm-modal title="Delete calendar" [message]="'Delete ' + name + ' and its appointment types? Calendars with appointments cannot be deleted; deactivate them instead.'"
        confirmLabel="Delete" (confirmed)="delete()" (cancelled)="confirmDelete.set(false)" />
    }
  `,
})
export class CalendarEditorComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id?: string;
  @ViewChild('hours') hours?: WeeklyHoursEditorComponent;
  private api = inject(ApiService);
  private router = inject(Router);
  readonly input = INPUT;
  readonly label = LABEL;
  readonly zones: string[] = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone') ?? [];

  loading = signal(false);
  saving = signal(false);
  detailsError = signal<string | null>(null);
  rules = signal<AvailabilityRule[]>([]);
  rulesSaved = signal(false);
  rulesError = signal<string | null>(null);
  overrides = signal<AvailabilityOverride[]>([]);
  overrideError = signal<string | null>(null);
  preview = signal<SlotsResponse['days']>([]);
  confirmDelete = signal(false);

  name = '';
  slug = '';
  timezone = browserTimeZone();
  notify = '';
  active = true;
  slugTouched = false;

  ovDate = '';
  ovClosed = true;
  ovStart = '09:00';
  ovEnd = '13:00';
  ovReason = '';

  ngOnInit() {
    if (!this.id) return;
    this.loading.set(true);
    this.api.get<BookingCalendar>(this.base()).subscribe({
      next: c => {
        this.name = c.name; this.slug = c.slug; this.timezone = c.timezone; this.slugTouched = true;
        this.notify = c.notify_emails.join(', '); this.active = c.is_active; this.rules.set(c.rules);
        this.loading.set(false);
        this.loadOverrides();
        this.loadPreview();
      },
      error: () => { this.detailsError.set('Calendar not found.'); this.loading.set(false); },
    });
  }

  private base() { return `/apps/${this.appSlug}/scheduling/calendars${this.id ? '/' + this.id : ''}`; }

  autoSlug() {
    if (!this.id && !this.slugTouched) this.slug = this.name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 100);
  }

  saveDetails() {
    this.saving.set(true);
    this.detailsError.set(null);
    const body = {
      name: this.name, slug: this.slug, timezone: this.timezone, is_active: this.active,
      notify_emails: this.notify.split(',').map(s => s.trim()).filter(Boolean),
    };
    const request = this.id ? this.api.put<BookingCalendar>(this.base(), body) : this.api.post<BookingCalendar>(this.base(), body);
    request.subscribe({
      next: c => {
        this.saving.set(false);
        if (!this.id) this.router.navigate(['/apps', this.appSlug, 'scheduling', 'calendars', c.id]);
        else this.loadPreview();
      },
      error: err => { this.detailsError.set(problem(err, 'Failed to save calendar.')); this.saving.set(false); },
    });
  }

  saveRules() {
    this.rulesError.set(null);
    this.api.put<BookingCalendar>(`${this.base()}/rules`, { rules: this.rules() }).subscribe({
      next: c => { this.rules.set(c.rules); this.rulesSaved.set(true); this.loadPreview(); },
      error: err => this.rulesError.set(problem(err, 'Failed to save hours.')),
    });
  }

  loadOverrides() {
    this.api.get<AvailabilityOverride[]>(`${this.base()}/overrides`, { from: todayIn(this.timezone) }).subscribe({
      next: o => this.overrides.set(o), error: () => {},
    });
  }

  addOverride() {
    this.overrideError.set(null);
    this.api.post<AvailabilityOverride>(`${this.base()}/overrides`, {
      date: this.ovDate, is_unavailable: this.ovClosed, start_time: this.ovClosed ? null : this.ovStart,
      end_time: this.ovClosed ? null : this.ovEnd, reason: this.ovReason || null,
    }).subscribe({
      next: () => { this.ovDate = ''; this.ovReason = ''; this.loadOverrides(); this.loadPreview(); },
      error: err => this.overrideError.set(problem(err, 'Failed to add override.')),
    });
  }

  deleteOverride(o: AvailabilityOverride) {
    this.api.delete<void>(`${this.base()}/overrides/${o.id}`).subscribe({ next: () => { this.loadOverrides(); this.loadPreview(); }, error: () => {} });
  }

  loadPreview() {
    const from = todayIn(this.timezone);
    this.api.get<SlotsResponse>(`${this.base()}/preview-slots`, { from, to: addDays(from, 7), duration: 30 }).subscribe({
      next: r => this.preview.set(r.days), error: () => this.preview.set([]),
    });
  }

  delete() {
    this.confirmDelete.set(false);
    this.api.delete<void>(this.base()).subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'scheduling', 'calendars']),
      error: err => this.detailsError.set(problem(err, 'Failed to delete calendar.')),
    });
  }

  day(date: string) { return formatDay(date); }
}

