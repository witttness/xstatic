import { Component, Input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-scheduling-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="flex gap-1 border-b border-border mb-6 text-sm">
      @for (tab of tabs; track tab.path) {
        <a [routerLink]="['/apps', appSlug, 'scheduling'].concat(tab.path ? [tab.path] : [])"
          routerLinkActive="border-accent text-text-primary" [routerLinkActiveOptions]="{ exact: !tab.path }"
          class="px-3 py-2 -mb-px border-b-2 border-transparent text-text-muted hover:text-text-primary">{{ tab.label }}</a>
      }
    </nav>
  `,
})
export class SchedulingNavComponent {
  @Input() appSlug = '';
  tabs = [
    { label: 'Appointments', path: '' },
    { label: 'Calendars', path: 'calendars' },
    { label: 'Appointment types', path: 'types' },
    { label: 'Integration', path: 'integration' },
  ];
}
