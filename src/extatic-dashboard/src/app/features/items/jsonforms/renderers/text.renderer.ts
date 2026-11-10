import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ViewRef,
  inject,
} from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { JsonFormsControl } from '@jsonforms/angular';
import { isStringControl, RankedTester, rankWith } from '@jsonforms/core';

@Component({
  selector: 'app-text-control-renderer',
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
        [type]="getType()"
        (input)="onChange($event)"
        (focus)="focused = true"
        (focusout)="focused = false"
        [formControl]="form"
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
export class TextControlRenderer extends JsonFormsControl {
  focused = false;
  private changeDetectorRef = inject(ChangeDetectorRef);

  override getEventValue = (event: Event) =>
    (event.target as HTMLInputElement).value || undefined;

  getType = (): string => {
    if (this.uischema.options?.['format']) {
      return this.uischema.options['format'] as string;
    }
    if (this.scopedSchema?.format) {
      switch (this.scopedSchema.format) {
        case 'email':
          return 'email';
        case 'tel':
          return 'tel';
        default:
          return 'text';
      }
    }
    return 'text';
  };

  override mapAdditionalProps() {
    if (!(this.changeDetectorRef as ViewRef).destroyed) {
      this.changeDetectorRef.markForCheck();
    }
  }
}
export const textControlTester: RankedTester = rankWith(1, isStringControl);
