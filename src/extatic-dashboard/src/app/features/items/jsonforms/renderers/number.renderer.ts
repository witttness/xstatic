import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ViewRef,
  inject,
} from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { JsonFormsControl } from '@jsonforms/angular';
import {
  deriveTypes,
  isIntegerControl,
  isNumberControl,
  or,
  RankedTester,
  rankWith,
} from '@jsonforms/core';

@Component({
  selector: 'app-number-control-renderer',
  standalone: true,
  template: `
    <div class="flex flex-col gap-1" [style.display]="hidden ? 'none' : null">
      @if (label) {
        <label [for]="id" class="text-xs font-medium text-text-muted uppercase tracking-wider">
          {{ label }}
        </label>
      }
      <input
        [id]="id"
        type="number"
        (input)="onChange($event)"
        (focus)="focused = true"
        (focusout)="focused = false"
        [formControl]="form"
        [attr.min]="min"
        [attr.max]="max"
        [attr.step]="step"
        class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed"
      />
      @if (description && (shouldShowUnfocusedDescription() || focused)) {
        <p class="text-xs text-text-muted">{{ description }}</p>
      }
      @if (error) {
        <p class="text-xs text-error" role="alert">{{ error }}</p>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
})
export class NumberControlRenderer extends JsonFormsControl {
  focused = false;
  min?: number;
  max?: number;
  step?: number;
  private changeDetectorRef = inject(ChangeDetectorRef);

  override getEventValue = (event: Event): number | undefined => {
    const value = (event.target as HTMLInputElement).value;
    if (value === '' || value === null) {
      return undefined;
    }
    const parsed = +value;
    return Number.isNaN(parsed) ? undefined : parsed;
  };

  override mapAdditionalProps() {
    if (this.scopedSchema) {
      this.min = this.scopedSchema.minimum;
      this.max = this.scopedSchema.maximum;
      const types = deriveTypes(this.scopedSchema);
      this.step =
        this.scopedSchema.multipleOf ??
        (types.includes('integer') ? 1 : 0.1);
    }
    if (!(this.changeDetectorRef as ViewRef).destroyed) {
      this.changeDetectorRef.markForCheck();
    }
  }
}
export const numberControlTester: RankedTester = rankWith(
  2,
  or(isNumberControl, isIntegerControl),
);
