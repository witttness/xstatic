import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { filter, map } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '../../core/services/auth.service';
import { ApiService } from '../../core/services/api.service';
import { App } from '../../core/models/app.model';

export interface NavLink {
  label: string;
  /** App-scoped links: path segment under /apps/:appSlug. Admin links: absolute path. */
  path: string;
}

/** Core app-scoped pages (Settings is rendered last, after the feature links). */
export const CORE_APP_LINKS: NavLink[] = [
  { label: 'Collections', path: 'collections' },
  { label: 'Team', path: 'collaborators' },
  { label: 'Users', path: 'appusers' },
  { label: 'Webhooks', path: 'webhooks' },
];

/** Feature modules (see docs/feature-modules.md). Routes live in features/<x>/<x>.routes.ts. */
export const FEATURE_APP_LINKS: NavLink[] = [
  { label: 'Forms', path: 'forms' },
  { label: 'Events', path: 'events' },
  { label: 'Comments', path: 'comments' },
  { label: 'Authentication', path: 'authentication' },
  { label: 'Members', path: 'members' },
  { label: 'Donations', path: 'donations' },
  { label: 'Search', path: 'search' },
  { label: 'Scheduling', path: 'scheduling' },
];

/** Only /apps/:slug[/...] is app-scoped; /apps, /admin/... and anything else are not. */
export function extractAppSlug(url: string): string | null {
  const match = url.match(/^\/apps\/([^/?#;]+)/);
  return match ? decodeURIComponent(match[1]) : null;
}

/** Platform-admin pages, shown only when the current user is_admin. */
export const ADMIN_LINKS: NavLink[] = [
  { label: 'Schema Library', path: '/admin/schemas' },
];

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="w-56 bg-background border-r border-border flex flex-col h-full shrink-0">
      <!-- Logo -->
      <div class="px-4 py-4 border-b border-border">
        <a routerLink="/apps" class="text-text-primary font-semibold text-base tracking-tight">extatic</a>
      </div>

      <!-- Nav -->
      <div class="flex-1 overflow-y-auto py-2">
        <a
          routerLink="/apps"
          routerLinkActive="bg-surface text-text-primary"
          [routerLinkActiveOptions]="{ exact: true }"
          class="flex items-center px-4 py-2 text-sm text-text-muted hover:bg-surface hover:text-text-primary transition-colors"
        >
          Apps
        </a>

        @if (currentAppSlug()) {
          <div class="mt-2 pt-2 border-t border-border">
            <div class="px-4 py-1 text-xs text-text-muted uppercase tracking-wider font-medium">
              {{ currentAppName() || currentAppSlug() }}
            </div>
            @for (link of coreLinks; track link.path) {
              <a
                [routerLink]="['/apps', currentAppSlug(), link.path]"
                routerLinkActive="bg-surface text-text-primary"
                class="flex items-center px-4 py-2 text-sm text-text-muted hover:bg-surface hover:text-text-primary transition-colors"
              >{{ link.label }}</a>
            }
            <div class="px-4 pt-3 pb-1 text-xs text-text-muted uppercase tracking-wider font-medium">Features</div>
            @for (link of featureLinks; track link.path) {
              <a
                [routerLink]="['/apps', currentAppSlug(), link.path]"
                routerLinkActive="bg-surface text-text-primary"
                class="flex items-center px-4 py-2 text-sm text-text-muted hover:bg-surface hover:text-text-primary transition-colors"
              >{{ link.label }}</a>
            }
            <a
              [routerLink]="['/apps', currentAppSlug(), 'settings']"
              routerLinkActive="bg-surface text-text-primary"
              class="flex items-center px-4 py-2 text-sm text-text-muted hover:bg-surface hover:text-text-primary transition-colors"
            >Settings</a>
          </div>
        }

        @if (user()?.is_admin) {
          <div class="mt-2 pt-2 border-t border-border">
            <div class="px-4 py-1 text-xs text-text-muted uppercase tracking-wider font-medium">Admin</div>
            @for (link of adminLinks; track link.path) {
              <a
                [routerLink]="link.path"
                routerLinkActive="bg-surface text-text-primary"
                class="flex items-center px-4 py-2 text-sm text-text-muted hover:bg-surface hover:text-text-primary transition-colors"
              >{{ link.label }}</a>
            }
          </div>
        }
      </div>

      <!-- User -->
      @if (user()) {
        <div class="px-4 py-3 border-t border-border flex items-center gap-2">
          <div class="w-7 h-7 rounded bg-accent flex items-center justify-center text-xs font-semibold text-white shrink-0">
            {{ initials() }}
          </div>
          <span class="text-xs text-text-muted truncate">{{ user()!.email }}</span>
        </div>
      }
    </nav>
  `,
})
export class SidebarComponent implements OnInit {
  private router = inject(Router);
  private auth = inject(AuthService);
  private api = inject(ApiService);

  user = this.auth.currentUser;
  coreLinks = CORE_APP_LINKS;
  featureLinks = FEATURE_APP_LINKS;
  adminLinks = ADMIN_LINKS;

  currentAppSlug = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      map(() => extractAppSlug(this.router.url))
    ),
    { initialValue: extractAppSlug(this.router.url) }
  );

  currentAppName = signal<string | null>(null);
  private appsCache: App[] = [];

  initials() {
    const u = this.user();
    if (!u) return '';
    return (u.name || u.email).substring(0, 2).toUpperCase();
  }

  ngOnInit() {
    this.api.get<App[]>('/apps').subscribe({
      next: apps => {
        this.appsCache = apps;
        this.updateAppName();
      },
      error: () => {}
    });

    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd)
    ).subscribe(() => this.updateAppName());
  }

  private updateAppName() {
    const slug = this.currentAppSlug();
    if (slug) {
      const app = this.appsCache.find(a => a.slug === slug);
      this.currentAppName.set(app?.name ?? null);
    } else {
      this.currentAppName.set(null);
    }
  }
}
