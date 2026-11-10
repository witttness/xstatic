import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ViewRef,
  inject,
} from '@angular/core';
import { JsonFormsControl } from '@jsonforms/angular';
import {
  isDateControl,
  isDateTimeControl,
  isTimeControl,
  RankedTester,
  rankWith,
} from '@jsonforms/core';

const template = `
  <div class="flex flex-col gap-1" [style.display]="hidden ? 'none' : null">
    @if (label) {
      <label [for]="id" class="text-xs font-medium text-text-muted uppercase tracking-wider">
        {{ label }}
      </label>
    }
    <input
      [id]="id"
      [type]="inputType"
      [step]="step"
      [value]="displayValue()"
      (change)="onChange($event)"
      [disabled]="!isEnabled()"
      class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed"
    />
    @if (description) {
      <p class="text-xs text-text-muted">{{ description }}</p>
    }
    @if (error) {
      <p class="text-xs text-error" role="alert">{{ error }}</p>
    }
  </div>
`;

abstract class DateTimeBaseControl extends JsonFormsControl {
  abstract inputType: 'date' | 'time' | 'datetime-local';
  step?: string;
  private changeDetectorRef = inject(ChangeDetectorRef);

  abstract displayValue(): string;

  override mapAdditionalProps() {
    if (!(this.changeDetectorRef as ViewRef).destroyed) {
      this.changeDetectorRef.markForCheck();
    }
  }
}

/** Matches the leading ISO date part ("YYYY-MM-DD") of a string, or ''. */
const isoDatePart = (value: unknown): string =>
  typeof value === 'string' && /^\d{4}-\d{2}-\d{2}/.test(value)
    ? value.slice(0, 10)
    : '';

const pad = (n: number) => `${n}`.padStart(2, '0');

/** Formats a Date as a local datetime string accepted by datetime-local inputs. */
const toLocalInputValue = (date: Date): string =>
  `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
  `T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;

@Component({
  selector: 'app-date-control-renderer',
  standalone: true,
  template,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DateControlRenderer extends DateTimeBaseControl {
  override inputType = 'date' as const;

  displayValue(): string {
    return isoDatePart(this.data);
  }

  override getEventValue = (event: Event): string | undefined =>
    (event.target as HTMLInputElement).value || undefined;
}
export const dateControlTester: RankedTester = rankWith(2, isDateControl);

@Component({
  selector: 'app-time-control-renderer',
  standalone: true,
  template,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimeControlRenderer extends DateTimeBaseControl {
  override inputType = 'time' as const;
  override step = '1';

  displayValue(): string {
    return typeof this.data === 'string' ? this.data : '';
  }

  override getEventValue = (event: Event): string | undefined =>
    (event.target as HTMLInputElement).value || undefined;
}
export const timeControlTester: RankedTester = rankWith(2, isTimeControl);

@Component({
  selector: 'app-datetime-control-renderer',
  standalone: true,
  template,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DateTimeControlRenderer extends DateTimeBaseControl {
  override inputType = 'datetime-local' as const;
  override step = '1';

  displayValue(): string {
    if (typeof this.data !== 'string' || this.data === '') {
      return '';
    }
    const date = new Date(this.data);
    return Number.isNaN(date.getTime()) ? '' : toLocalInputValue(date);
  }

  override getEventValue = (event: Event): string | undefined => {
    const value = (event.target as HTMLInputElement).value;
    if (!value) {
      return undefined;
    }
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toISOString();
  };
}
export const dateTimeControlTester: RankedTester = rankWith(
  2,
  isDateTimeControl,
);
