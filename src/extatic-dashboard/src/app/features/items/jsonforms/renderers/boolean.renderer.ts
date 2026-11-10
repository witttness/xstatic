import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ViewRef,
  inject,
} from '@angular/core';
import { JsonFormsControl } from '@jsonforms/angular';
import { isBooleanControl, RankedTester, rankWith } from '@jsonforms/core';

@Component({
  selector: 'app-boolean-control-renderer',
  standalone: true,
  template: `
    <div class="flex flex-col gap-1" [style.display]="hidden ? 'none' : null">
      <label class="flex items-center gap-2" [class.cursor-pointer]="isEnabled()">
        <input
          type="checkbox"
          [id]="id"
          (change)="onChange($event)"
          [checked]="isChecked()"
          [disabled]="!isEnabled()"
          class="w-4 h-4 border border-border bg-background accent-accent"
        />
        <span class="text-sm text-text-primary">{{ label }}</span>
      </label>
      @if (description && shouldShowUnfocusedDescription()) {
        <p class="text-xs text-text-muted">{{ description }}</p>
      }
      @if (error) {
        <p class="text-xs text-error" role="alert">{{ error }}</p>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BooleanControlRenderer extends JsonFormsControl {
  private changeDetectorRef = inject(ChangeDetectorRef);

  isChecked = () => this.data || false;
  override getEventValue = (event: Event) => (event.target as HTMLInputElement).checked;

  override mapAdditionalProps() {
    if (!(this.changeDetectorRef as ViewRef).destroyed) {
      this.changeDetectorRef.markForCheck();
    }
  }
}
export const booleanControlTester: RankedTester = rankWith(2, isBooleanControl);
