import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-badge',
  standalone: true,
  template: `
    <span [class]="classes"><ng-content /></span>
  `,
})
export class BadgeComponent {
  @Input() variant: 'success' | 'error' | 'warning' | 'muted' | 'info' = 'muted';

  get classes(): string {
    const base = 'inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-sm';
    const variants: Record<string, string> = {
      success: 'bg-success/10 text-success',
      error: 'bg-error/10 text-error',
      warning: 'bg-warning/10 text-warning',
      muted: 'bg-surface text-text-muted',
      info: 'bg-accent/10 text-accent',
    };
    return `${base} ${variants[this.variant]}`;
  }
}
