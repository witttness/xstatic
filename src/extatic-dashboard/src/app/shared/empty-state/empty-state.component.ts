import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  template: `
    <div class="flex flex-col items-center justify-center py-16 text-center">
      <p class="text-text-muted text-sm mb-1">{{ message }}</p>
      @if (description) {
        <p class="text-text-muted text-xs mb-4">{{ description }}</p>
      }
      <div class="mt-4">
        <ng-content />
      </div>
    </div>
  `,
})
export class EmptyStateComponent {
  @Input() message = 'No items found.';
  @Input() description = '';
}
