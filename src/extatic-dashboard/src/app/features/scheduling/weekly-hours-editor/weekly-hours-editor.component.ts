import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { AvailabilityRule } from '../scheduling.models';
import { WEEKDAYS, WEEKDAY_ORDER, addRange, removeRange, sortRules, validateRules } from '../scheduling.util';

/** Weekly opening hours grid: one row per weekday, any number of time ranges per day. */
@Component({
  selector: 'app-weekly-hours-editor',
  standalone: true,
  template: `
    <div class="border border-border divide-y divide-border">
      @for (day of days; track day) {
        <div class="flex items-start gap-4 px-4 py-3" [attr.data-day]="day">
          <div class="w-28 pt-1.5 text-sm text-text-primary">{{ weekdays[day] }}</div>
          <div class="flex-1 space-y-2">
            @for (entry of rangesFor(day); track entry.index) {
              <div class="flex items-center gap-2" data-range>
                <input type="time" step="300" [value]="entry.rule.start_time" [disabled]="disabled"
                  (change)="update(entry.index, 'start_time', $any($event.target).value)"
                  [attr.aria-label]="weekdays[day] + ' start'"
                  class="bg-background border border-border px-2 py-1 text-sm text-text-primary focus:border-accent focus:outline-none" />
                <span class="text-text-muted text-xs">to</span>
                <input type="time" step="300" [value]="entry.rule.end_time === '24:00' ? '00:00' : entry.rule.end_time" [disabled]="disabled"
                  (change)="update(entry.index, 'end_time', $any($event.target).value)"
                  [attr.aria-label]="weekdays[day] + ' end'"
                  class="bg-background border border-border px-2 py-1 text-sm text-text-primary focus:border-accent focus:outline-none" />
                @if (!disabled) {
                  <button type="button" class="text-xs text-error hover:text-red-400" (click)="remove(entry.index)"
                    [attr.aria-label]="'Remove ' + weekdays[day] + ' ' + entry.rule.start_time">Remove</button>
                }
              </div>
            } @empty {
              <p class="pt-1.5 text-xs text-text-muted">Unavailable</p>
            }
          </div>
          @if (!disabled) {
            <button type="button" class="pt-1.5 text-xs text-accent hover:underline" (click)="add(day)"
              [attr.aria-label]="'Add hours on ' + weekdays[day]">+ Add hours</button>
          }
        </div>
      }
    </div>
    @if (errors().length) {
      <ul class="mt-2 space-y-1">
        @for (e of errors(); track e) { <li class="text-xs text-error">{{ e }}</li> }
      </ul>
    }
    <p class="mt-2 text-xs text-text-muted">Times are local to the calendar's time zone ({{ timezone }}). An end of 00:00 means midnight.</p>
  `,
})
export class WeeklyHoursEditorComponent {
  @Input() timezone = 'UTC';
  @Input() disabled = false;
  @Input() set rules(value: AvailabilityRule[] | null) { this.state.set(sortRules(value ?? [])); }
  @Output() rulesChange = new EventEmitter<AvailabilityRule[]>();

  readonly weekdays = WEEKDAYS;
  readonly days = WEEKDAY_ORDER;
  readonly state = signal<AvailabilityRule[]>([]);
  readonly errors = computed(() => validateRules(this.state()));

  rangesFor(day: number) {
    return this.state().map((rule, index) => ({ rule, index })).filter(e => e.rule.day_of_week === day);
  }

  add(day: number) { this.emit(addRange(this.state(), day)); }
  remove(index: number) { this.emit(removeRange(this.state(), index)); }

  update(index: number, field: 'start_time' | 'end_time', value: string) {
    const next = this.state().map((r, i) => i === index ? { ...r, [field]: field === 'end_time' && value === '00:00' ? '24:00' : value } : r);
    this.emit(next);
  }

  get valid(): boolean { return this.errors().length === 0; }

  private emit(next: AvailabilityRule[]) {
    this.state.set(next);
    this.rulesChange.emit(next);
  }
}
