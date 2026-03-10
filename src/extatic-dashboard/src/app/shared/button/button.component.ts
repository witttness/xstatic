import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-button',
  standalone: true,
  host: { class: 'inline-block' },
  template: `
    <button
      [type]="type"
      [disabled]="disabled || loading"
      [class]="classes"
    >
      @if (loading) {
        <svg class="animate-spin h-3.5 w-3.5 mr-1.5 inline" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"/>
        </svg>
      }
      <ng-content />
    </button>
  `,
})
export class ButtonComponent {
  @Input() variant: 'primary' | 'secondary' | 'danger' = 'primary';
  @Input() size: 'sm' | 'md' = 'md';
  @Input() loading = false;
  @Input() disabled = false;
  @Input() type: 'button' | 'submit' | 'reset' = 'button';

  get classes(): string {
    const base = 'inline-flex items-center font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed';
    const sizes: Record<string, string> = {
      sm: 'px-3 py-1.5 text-xs',
      md: 'px-4 py-2 text-sm',
    };
    const variants: Record<string, string> = {
      primary: 'bg-accent text-white hover:bg-blue-600',
      secondary: 'bg-surface border border-border text-text-primary hover:bg-border',
      danger: 'bg-error text-white hover:bg-red-600',
    };
    return `${base} ${sizes[this.size]} ${variants[this.variant]}`;
  }
}
