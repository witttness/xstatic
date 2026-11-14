import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { Campaign } from '../donations.models';
import { formatMoney } from '../money';

@Component({
  selector: 'app-donations-campaigns-list',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent],
  template: `
    <app-page-header title="Donation campaigns">
      <a [routerLink]="['/apps', appSlug, 'donations']"><app-button variant="secondary">Donations</app-button></a>
      <a [routerLink]="['/apps', appSlug, 'donations', 'campaigns', 'new']"><app-button>New campaign</app-button></a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (campaigns().length === 0) {
      <app-empty-state message="No campaigns yet." description="Create a campaign to start accepting donations.">
        <a [routerLink]="['/apps', appSlug, 'donations', 'campaigns', 'new']"><app-button>New campaign</app-button></a>
      </app-empty-state>
    } @else {
      <div class="space-y-3">
        @for (c of campaigns(); track c.id) {
          <a [routerLink]="['/apps', appSlug, 'donations', 'campaigns', c.id]" class="block border border-border p-4 hover:bg-surface">
            <div class="flex items-center justify-between gap-2 mb-2">
              <div class="flex items-center gap-2">
                <span class="text-sm font-medium text-text-primary">{{ c.name }}</span>
                <span class="font-mono text-xs text-text-muted">{{ c.slug }}</span>
              </div>
              @if (c.is_active) { <app-badge variant="success">Active</app-badge> } @else { <app-badge variant="muted">Inactive</app-badge> }
            </div>
            <div class="text-xs text-text-muted mb-1">
              {{ money(c.raised_minor, c.currency) }} raised from {{ c.donor_count }} donation{{ c.donor_count === 1 ? '' : 's' }}
              @if (c.goal_amount_minor) { of {{ money(c.goal_amount_minor, c.currency) }} }
            </div>
            @if (c.goal_amount_minor) {
              <div class="h-1.5 bg-surface border border-border" role="progressbar" [attr.aria-valuenow]="progress(c)" aria-valuemin="0" aria-valuemax="100">
                <div class="h-full bg-accent" [style.width.%]="progress(c)"></div>
              </div>
            }
          </a>
        }
      </div>
    }
  `,
})
export class CampaignsListComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);

  campaigns = signal<Campaign[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  money = formatMoney;

  ngOnInit() {
    this.api.get<Campaign[]>(`/apps/${this.appSlug}/donations/campaigns`).subscribe({
      next: cs => { this.campaigns.set(cs); this.loading.set(false); },
      error: () => { this.error.set('Failed to load campaigns.'); this.loading.set(false); },
    });
  }

  progress(c: Campaign): number {
    if (!c.goal_amount_minor) return 0;
    return Math.min(100, Math.round((c.raised_minor / c.goal_amount_minor) * 100));
  }
}
