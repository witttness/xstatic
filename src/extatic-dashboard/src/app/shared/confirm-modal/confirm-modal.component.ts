import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonComponent } from '../button/button.component';

@Component({
  selector: 'app-confirm-modal',
  standalone: true,
  imports: [FormsModule, ButtonComponent],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center">
      <div class="absolute inset-0 bg-black/60" (click)="onCancel()"></div>
      <div class="relative bg-surface border border-border w-full max-w-md p-6 z-10">
        <h3 class="text-base font-semibold text-text-primary mb-2">{{ title }}</h3>
        <p class="text-sm text-text-muted mb-4">{{ message }}</p>

        @if (confirmWord) {
          <div class="mb-4">
            <label class="text-xs font-medium text-text-muted uppercase tracking-wider block mb-1">
              Type <span class="font-mono text-text-primary">{{ confirmWord }}</span> to confirm
            </label>
            <input
              type="text"
              [(ngModel)]="typedWord"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"
              [placeholder]="confirmWord"
            />
          </div>
        }

        <div class="flex justify-end gap-2">
          <app-button variant="secondary" (click)="onCancel()">Cancel</app-button>
          <app-button
            variant="danger"
            [disabled]="confirmWord ? typedWord !== confirmWord : false"
            (click)="onConfirm()"
          >{{ confirmLabel }}</app-button>
        </div>
      </div>
    </div>
  `,
})
export class ConfirmModalComponent {
  @Input() title = 'Confirm';
  @Input() message = 'Are you sure?';
  @Input() confirmWord = '';
  @Input() confirmLabel = 'Confirm';
  @Output() confirmed = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  typedWord = '';

  onConfirm() {
    this.confirmed.emit();
  }

  onCancel() {
    this.cancelled.emit();
  }
}
