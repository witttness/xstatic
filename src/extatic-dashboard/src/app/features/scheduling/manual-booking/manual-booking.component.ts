import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { SchedulingNavComponent } from '../scheduling-nav.component';
import { SlotPickerComponent } from '../slot-picker/slot-picker.component';
import { AppointmentType, BookingCalendar, Slot } from '../scheduling.models';

const INPUT = 'w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';

@Component({
  selector: 'app-scheduling-manual-booking',
  standalone: true,
  imports: [FormsModule, PageHeaderComponent, ButtonComponent, SchedulingNavComponent, SlotPickerComponent],
  template: `
    <app-page-header title="New booking" subtitle="Book on behalf of a customer. Minimum notice is not enforced; overlaps never are allowed." />
    <app-scheduling-nav [appSlug]="appSlug" />

    <form class="max-w-lg space-y-5" (ngSubmit)="submit()">
      <div>
        <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Appointment type</label>
        <select name="type" [(ngModel)]="typeId" (ngModelChange)="slot.set(null); typeSignal.set($event)" [class]="input">
          <option value="">Select…</option>
          @for (t of types(); track t.id) { <option [value]="t.id">{{ t.name }} ({{ t.duration_minutes }} min)</option> }
        </select>
      </div>
      @if (selectedType(); as t) {
        <app-slot-picker [appSlug]="appSlug" [calendarId]="t.calendar_id" [typeId]="t.id" [timezone]="zone()" (picked)="slot.set($event)" />
      }
      <div>
        <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Customer name</label>
        <input name="name" [(ngModel)]="name" required [class]="input" />
      </div>
      <div>
        <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Email</label>
        <input name="email" type="email" [(ngModel)]="email" required [class]="input" />
      </div>
      <div>
        <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Phone (optional)</label>
        <input name="phone" [(ngModel)]="phone" [class]="input" />
      </div>
      <div>
        <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Internal notes (optional)</label>
        <textarea name="notes" [(ngModel)]="notes" rows="3" [class]="input"></textarea>
      </div>
      <label class="flex items-center gap-2 text-sm text-text-primary">
        <input type="checkbox" name="confirmed" [(ngModel)]="confirmed" /> Confirm immediately
      </label>
      @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }
      <div class="flex gap-3">
        <app-button type="submit" [loading]="saving()" [disabled]="!slot() || !name || !email">Book</app-button>
        <app-button variant="secondary" (click)="back()">Cancel</app-button>
      </div>
    </form>
  `,
})
export class ManualBookingComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  private router = inject(Router);
  readonly input = INPUT;

  types = signal<AppointmentType[]>([]);
  calendars = signal<BookingCalendar[]>([]);
  typeSignal = signal('');
  selectedType = computed(() => this.types().find(t => t.id === this.typeSignal()) ?? null);
  zone = computed(() => this.calendars().find(c => c.id === this.selectedType()?.calendar_id)?.timezone ?? 'UTC');
  slot = signal<Slot | null>(null);
  saving = signal(false);
  error = signal<string | null>(null);

  typeId = '';
  name = '';
  email = '';
  phone = '';
  notes = '';
  confirmed = true;

  ngOnInit() {
    this.api.get<AppointmentType[]>(`/apps/${this.appSlug}/scheduling/types`).subscribe({ next: t => this.types.set(t), error: () => {} });
    this.api.get<BookingCalendar[]>(`/apps/${this.appSlug}/scheduling/calendars`).subscribe({ next: c => this.calendars.set(c), error: () => {} });
  }

  submit() {
    const slot = this.slot();
    if (!slot) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.post(`/apps/${this.appSlug}/scheduling/appointments`, {
      appointment_type_id: this.typeId, starts_at: slot.starts_at, name: this.name, email: this.email,
      phone: this.phone || null, notes: this.notes || null, confirmed: this.confirmed,
    }).subscribe({
      next: () => this.back(),
      error: err => { this.error.set(err?.error?.detail ?? 'Booking failed.'); this.saving.set(false); },
    });
  }

  back() { this.router.navigate(['/apps', this.appSlug, 'scheduling']); }
}
