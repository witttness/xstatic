import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { SchedulingNavComponent } from '../scheduling-nav.component';
import { AppointmentType, BookingCalendar } from '../scheduling.models';
import { problem } from '../scheduling.util';

const INPUT = 'w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';
const LABEL = 'block text-xs font-medium text-text-muted uppercase tracking-wider mb-1';

@Component({
  selector: 'app-scheduling-type-editor',
  standalone: true,
  imports: [FormsModule, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent, ConfirmModalComponent, SchedulingNavComponent],
  template: `
    <app-page-header [title]="id ? 'Edit appointment type' : 'New appointment type'">
      @if (id) { <app-button variant="danger" (click)="confirmDelete.set(true)">Delete</app-button> }
    </app-page-header>
    <app-scheduling-nav [appSlug]="appSlug" />
    @if (loading()) {
      <app-loading-spinner />
    } @else {
      <form class="max-w-2xl space-y-5" (ngSubmit)="save()">
        <div class="grid grid-cols-2 gap-4">
          <div><label [class]="label">Name</label><input name="name" [(ngModel)]="model.name" (input)="autoSlug()" [class]="input" /></div>
          <div><label [class]="label">Slug</label><input name="slug" [(ngModel)]="model.slug" (input)="slugTouched = true" [class]="input + ' font-mono'" /></div>
        </div>
        <div>
          <label [class]="label">Calendar</label>
          <select name="calendar" [(ngModel)]="model.calendar_id" [class]="input">
            @for (c of calendars(); track c.id) { <option [value]="c.id">{{ c.name }} ({{ c.timezone }})</option> }
          </select>
        </div>
        <div><label [class]="label">Description</label><textarea name="description" rows="2" [(ngModel)]="model.description" [class]="input"></textarea></div>
        <div class="grid grid-cols-3 gap-4">
          <div><label [class]="label">Duration (min)</label><input name="duration" type="number" min="5" [(ngModel)]="model.duration_minutes" [class]="input" /></div>
          <div><label [class]="label">Start every (min)</label><input name="interval" type="number" min="5" [(ngModel)]="model.slot_interval_minutes" [class]="input" /></div>
          <div><label [class]="label">Max per day</label><input name="maxPerDay" type="number" min="0" [(ngModel)]="model.max_per_day" [class]="input" placeholder="No limit" /></div>
          <div><label [class]="label">Buffer before (min)</label><input name="bufferBefore" type="number" min="0" [(ngModel)]="model.buffer_before_minutes" [class]="input" /></div>
          <div><label [class]="label">Buffer after (min)</label><input name="bufferAfter" type="number" min="0" [(ngModel)]="model.buffer_after_minutes" [class]="input" /></div>
          <div></div>
          <div><label [class]="label">Min notice (min)</label><input name="minNotice" type="number" min="0" [(ngModel)]="model.min_notice_minutes" [class]="input" /></div>
          <div><label [class]="label">Max advance (days)</label><input name="maxAdvance" type="number" min="1" [(ngModel)]="model.max_advance_days" [class]="input" /></div>
        </div>
        <div class="grid grid-cols-2 gap-4">
          <div><label [class]="label">Location</label><input name="location" [(ngModel)]="model.location" [class]="input" /></div>
          <div><label [class]="label">Meeting URL (shared once confirmed)</label><input name="meeting" [(ngModel)]="model.meeting_url" [class]="input" placeholder="https://" /></div>
        </div>
        <div>
          <label [class]="label">Manage page URL on your site</label>
          <input name="manage" [(ngModel)]="model.manage_url_template" [class]="input + ' font-mono'" placeholder="https://example.com/booking?token={token}" />
          <p class="text-xs text-text-muted mt-1">Must be https and contain <span class="font-mono">{{ '{' }}token{{ '}' }}</span>. Linked from the booking email so customers can view, reschedule or cancel.</p>
        </div>
        <div>
          <label [class]="label">Intake form (JSON Schema, optional)</label>
          <textarea name="schema" rows="6" [(ngModel)]="schemaText" [class]="input + ' font-mono text-xs'" placeholder='{"type":"object","properties":{"reason":{"type":"string"}}}'></textarea>
        </div>
        <label class="flex items-center gap-2 text-sm text-text-primary"><input type="checkbox" name="approval" [(ngModel)]="model.requires_approval" /> Bookings require approval</label>
        <label class="flex items-center gap-2 text-sm text-text-primary"><input type="checkbox" name="active" [(ngModel)]="model.is_active" /> Active (bookable)</label>
        @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }
        <div class="flex gap-3">
          <app-button type="submit" [loading]="saving()">{{ id ? 'Save' : 'Create' }}</app-button>
          <app-button variant="secondary" (click)="back()">Cancel</app-button>
        </div>
      </form>
    }
    @if (confirmDelete()) {
      <app-confirm-modal title="Delete appointment type" [message]="'Delete ' + model.name + '? Types with appointments cannot be deleted; deactivate them instead.'"
        confirmLabel="Delete" (confirmed)="delete()" (cancelled)="confirmDelete.set(false)" />
    }
  `,
})
export class TypeEditorComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id?: string;
  private api = inject(ApiService);
  private router = inject(Router);
  readonly input = INPUT;
  readonly label = LABEL;

  calendars = signal<BookingCalendar[]>([]);
  loading = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);
  confirmDelete = signal(false);
  slugTouched = false;
  schemaText = '';

  model: Partial<AppointmentType> = {
    name: '', slug: '', calendar_id: '', description: '', duration_minutes: 30, slot_interval_minutes: 30,
    buffer_before_minutes: 0, buffer_after_minutes: 0, min_notice_minutes: 120, max_advance_days: 60, max_per_day: null,
    requires_approval: false, location: '', meeting_url: '', manage_url_template: '', is_active: true,
  };

  ngOnInit() {
    this.api.get<BookingCalendar[]>(`/apps/${this.appSlug}/scheduling/calendars`).subscribe({
      next: c => { this.calendars.set(c); if (!this.model.calendar_id && c.length) this.model.calendar_id = c[0].id; },
      error: () => {},
    });
    if (!this.id) return;
    this.loading.set(true);
    this.api.get<AppointmentType>(this.base()).subscribe({
      next: t => {
        this.model = { ...t };
        this.schemaText = t.intake_schema ? JSON.stringify(t.intake_schema, null, 2) : '';
        this.slugTouched = true;
        this.loading.set(false);
      },
      error: () => { this.error.set('Appointment type not found.'); this.loading.set(false); },
    });
  }

  private base() { return `/apps/${this.appSlug}/scheduling/types${this.id ? '/' + this.id : ''}`; }

  autoSlug() {
    if (!this.id && !this.slugTouched)
      this.model.slug = (this.model.name ?? '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 100);
  }

  save() {
    let schema: unknown = null;
    if (this.schemaText.trim()) {
      try { schema = JSON.parse(this.schemaText); } catch { this.error.set('Intake form is not valid JSON.'); return; }
    }
    this.saving.set(true);
    this.error.set(null);
    const m = this.model;
    const body = {
      calendar_id: m.calendar_id, name: m.name, slug: m.slug, description: m.description ?? '',
      duration_minutes: num(m.duration_minutes), slot_interval_minutes: num(m.slot_interval_minutes),
      buffer_before_minutes: num(m.buffer_before_minutes) ?? 0, buffer_after_minutes: num(m.buffer_after_minutes) ?? 0,
      min_notice_minutes: num(m.min_notice_minutes), max_advance_days: num(m.max_advance_days), max_per_day: num(m.max_per_day) ?? 0,
      requires_approval: !!m.requires_approval, intake_schema: schema, location: m.location ?? '', meeting_url: m.meeting_url ?? '',
      manage_url_template: m.manage_url_template ?? '', is_active: !!m.is_active,
    };
    const request = this.id ? this.api.put<AppointmentType>(this.base(), body) : this.api.post<AppointmentType>(this.base(), body);
    request.subscribe({
      next: () => this.back(),
      error: err => { this.error.set(problem(err, 'Failed to save.')); this.saving.set(false); },
    });
  }

  delete() {
    this.confirmDelete.set(false);
    this.api.delete<void>(this.base()).subscribe({ next: () => this.back(), error: err => this.error.set(problem(err, 'Failed to delete.')) });
  }

  back() { this.router.navigate(['/apps', this.appSlug, 'scheduling', 'types']); }
}

function num(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}
