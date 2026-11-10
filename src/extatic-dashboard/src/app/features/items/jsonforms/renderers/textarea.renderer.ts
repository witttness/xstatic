import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ViewRef,
  inject,
} from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { JsonFormsControl } from '@jsonforms/angular';
import { isMultiLineControl, RankedTester, rankWith } from '@jsonforms/core';

@Component({
  selector: 'app-text-area-renderer',
  standalone: true,
  template: `
    <div class="flex flex-col gap-1" [style.display]="hidden ? 'none' : null">
      @if (label) {
        <label [for]="id" class="text-xs font-medium text-text-muted uppercase tracking-wider">
          {{ label }}
        </label>
      }
      <textarea
        [id]="id"
        (input)="onChange($event)"
        (focus)="focused = true"
        (focusout)="focused = false"
        [formControl]="form"
        rows="4"
        class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed resize-y"
      ></textarea>
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
export class TextAreaRenderer extends JsonFormsControl {
  focused = false;
  private changeDetectorRef = inject(ChangeDetectorRef);

  override getEventValue = (event: Event) =>
    (event.target as HTMLTextAreaElement).value || undefined;

  override mapAdditionalProps() {
    if (!(this.changeDetectorRef as ViewRef).destroyed) {
      this.changeDetectorRef.markForCheck();
    }
  }
}
export const textAreaTester: RankedTester = rankWith(2, isMultiLineControl);
