import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-loading-spinner',
  standalone: true,
  template: `
    <div class="flex items-center justify-center" [class]="containerClass">
      <svg
        [class]="spinnerClass"
        class="animate-spin text-text-muted"
        fill="none"
        viewBox="0 0 24 24"
      >
        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
        <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"/>
      </svg>
    </div>
  `,
})
export class LoadingSpinnerComponent {
  @Input() size: 'sm' | 'md' | 'lg' = 'md';

  get containerClass(): string {
    return this.size === 'lg' ? 'py-16' : 'py-8';
  }

  get spinnerClass(): string {
    const sizes: Record<string, string> = { sm: 'h-4 w-4', md: 'h-6 w-6', lg: 'h-8 w-8' };
    return sizes[this.size];
  }
}
