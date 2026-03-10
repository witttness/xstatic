import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-page-header',
  standalone: true,
  template: `
    <div class="flex items-start justify-between mb-6">
      <div>
        <h2 class="text-lg font-semibold text-text-primary">{{ title }}</h2>
        @if (subtitle) {
          <p class="text-sm text-text-muted mt-0.5">{{ subtitle }}</p>
        }
      </div>
      <div class="flex items-center gap-2">
        <ng-content />
      </div>
    </div>
  `,
})
export class PageHeaderComponent {
  @Input() title = '';
  @Input() subtitle = '';
}
