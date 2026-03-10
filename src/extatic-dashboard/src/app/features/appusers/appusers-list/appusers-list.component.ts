import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/services/api.service';
import { AppUser } from '../../../core/models/app-user.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';

@Component({
  selector: 'app-appusers-list',
  standalone: true,
  imports: [PageHeaderComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent],
  template: `
    <app-page-header
      title="Users"
      subtitle="AppUsers are created automatically via the Client API on first login."
    />

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (users().length === 0) {
      <app-empty-state
        message="No users yet."
        description="AppUsers are created automatically when end-users sign in via the Client API."
      />
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">User</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Provider</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Last Login</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Joined</th>
            </tr>
          </thead>
          <tbody>
            @for (u of users(); track u.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3">
                  <div class="flex items-center gap-2">
                    <div class="w-6 h-6 rounded bg-surface border border-border flex items-center justify-center text-xs font-medium text-text-muted shrink-0">
                      {{ initials(u) }}
                    </div>
                    <div>
                      <div class="text-text-primary">{{ u.display_name || 'Unknown' }}</div>
                      @if (u.email) {
                        <div class="text-xs text-text-muted">{{ u.email }}</div>
                      }
                    </div>
                  </div>
                </td>
                <td class="px-4 py-3">
                  <app-badge variant="info">{{ u.provider }}</app-badge>
                </td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ u.last_login_at ? formatDate(u.last_login_at) : '—' }}</td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ formatDate(u.created_at) }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class AppUsersListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  users = signal<AppUser[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  ngOnInit() {
    this.api.get<AppUser[]>(`/apps/${this.appSlug}/appusers`).subscribe({
      next: users => { this.users.set(users); this.loading.set(false); },
      error: () => { this.error.set('Failed to load users.'); this.loading.set(false); },
    });
  }

  initials(u: AppUser): string {
    const name = u.display_name || u.email || '?';
    return name.substring(0, 2).toUpperCase();
  }

  formatDate(d: string) {
    return new Date(d).toLocaleDateString();
  }
}
