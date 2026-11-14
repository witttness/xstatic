import { Component, Input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Sub-navigation shared by the comments screens. */
@Component({
  selector: 'app-comments-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="flex gap-4 border-b border-border mb-6 text-sm">
      <a [routerLink]="['/apps', appSlug, 'comments']" routerLinkActive="text-text-primary border-accent"
        [routerLinkActiveOptions]="{ exact: true }"
        class="pb-2 border-b-2 border-transparent text-text-muted hover:text-text-primary">Moderation</a>
      <a [routerLink]="['/apps', appSlug, 'comments', 'pages']" routerLinkActive="text-text-primary border-accent"
        class="pb-2 border-b-2 border-transparent text-text-muted hover:text-text-primary">Pages</a>
      <a [routerLink]="['/apps', appSlug, 'comments', 'settings']" routerLinkActive="text-text-primary border-accent"
        class="pb-2 border-b-2 border-transparent text-text-muted hover:text-text-primary">Settings</a>
    </nav>
  `,
})
export class CommentsNavComponent {
  @Input() appSlug = '';
}
