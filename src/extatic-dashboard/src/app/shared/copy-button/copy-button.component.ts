import { Component, Input, signal } from '@angular/core';

@Component({
  selector: 'app-copy-button',
  standalone: true,
  template: `
    <button
      type="button"
      (click)="copy()"
      class="text-xs text-text-muted hover:text-text-primary transition-colors px-2 py-1 border border-border"
    >
      {{ copied() ? 'Copied!' : label }}
    </button>
  `,
})
export class CopyButtonComponent {
  @Input() value = '';
  @Input() label = 'Copy';

  copied = signal(false);

  copy() {
    navigator.clipboard.writeText(this.value).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    });
  }
}
