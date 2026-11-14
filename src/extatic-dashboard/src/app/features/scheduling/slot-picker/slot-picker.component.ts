import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { Slot, SlotsResponse } from '../scheduling.models';
import { addDays, todayIn } from '../scheduling.util';

/** Picks a slot for a type via the preview-slots endpoint (min-notice bypassed for staff). */
@Component({
  selector: 'app-slot-picker',
  standalone: true,
  imports: [FormsModule],
  template: `
    <label class="text-xs text-text-muted">Date ({{ timezone }})
      <input type="date" [(ngModel)]="date" (change)="load()" class="block mt-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary" />
    </label>
    <div class="mt-3 flex flex-wrap gap-2 min-h-8">
      @if (loading()) {
        <span class="text-xs text-text-muted">Loading slots…</span>
      } @else {
        @for (s of slots(); track s.starts_at) {
          <button type="button" (click)="pick(s)"
            [class]="'px-2 py-1 text-xs font-mono border ' + (selected()?.starts_at === s.starts_at ? 'border-accent text-accent' : 'border-border text-text-primary hover:border-accent')">
            {{ s.local_time }}</button>
        } @empty {
          <span class="text-xs text-text-muted">No free slots on this day.</span>
        }
      }
    </div>
  `,
})
export class SlotPickerComponent implements OnChanges {
  @Input() appSlug = '';
  @Input() calendarId = '';
  @Input() typeId = '';
  @Input() timezone = 'UTC';
  @Output() picked = new EventEmitter<Slot | null>();
  private api = inject(ApiService);

  date = '';
  slots = signal<Slot[]>([]);
  loading = signal(false);
  selected = signal<Slot | null>(null);

  ngOnChanges() {
    if (!this.date) this.date = todayIn(this.timezone);
    this.load();
  }

  load() {
    this.selected.set(null);
    this.picked.emit(null);
    if (!this.calendarId || !this.typeId || !this.date) { this.slots.set([]); return; }
    this.loading.set(true);
    this.api.get<SlotsResponse>(`/apps/${this.appSlug}/scheduling/calendars/${this.calendarId}/preview-slots`,
      { type_id: this.typeId, from: this.date, to: addDays(this.date, 1) }).subscribe({
      next: r => { this.slots.set(r.days.flatMap(d => d.slots)); this.loading.set(false); },
      error: () => { this.slots.set([]); this.loading.set(false); },
    });
  }

  pick(slot: Slot) {
    this.selected.set(slot);
    this.picked.emit(slot);
  }
}
