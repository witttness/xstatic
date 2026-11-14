import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { MembershipPlan } from '../members.model';

@Component({
  selector: 'app-membership-plans-list',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent, ConfirmModalComponent],
  template: `
    <a [routerLink]="['/apps', appSlug, 'members']" class="text-xs text-text-muted hover:text-text-primary">&larr; Members</a>
    <app-page-header title="Membership plans">
      <a [routerLink]="['/apps', appSlug, 'members', 'plans', 'new']"><app-button>New Plan</app-button></a>
    </app-page-header>

    @if (error()) { <p class="text-error text-sm mb-3">{{ error() }}</p> }
    @if (loading()) {
      <app-loading-spinner />
    } @else if (plans().length === 0) {
      <app-empty-state message="No plans yet." description="Create a plan your site's users can join.">
        <a [routerLink]="['/apps', appSlug, 'members', 'plans', 'new']"><app-button>New Plan</app-button></a>
      </app-empty-state>
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Plan</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Price</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Term</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Members</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (p of plans(); track p.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3"><div class="text-text-primary">{{ p.name }}</div><div class="font-mono text-xs text-text-muted">{{ p.slug }}</div></td>
                <td class="px-4 py-3 text-text-primary">{{ p.price_label || '—' }}</td>
                <td class="px-4 py-3 text-text-primary">{{ p.duration_days ? p.duration_days + ' days' : 'No expiry' }}</td>
                <td class="px-4 py-3 text-text-primary">{{ p.active_members }}{{ p.max_members ? ' / ' + p.max_members : '' }}</td>
                <td class="px-4 py-3">
                  <div class="flex flex-wrap gap-1">
                    <app-badge [variant]="p.is_active ? 'success' : 'muted'">{{ p.is_active ? 'Active' : 'Inactive' }}</app-badge>
                    @if (p.requires_approval) { <app-badge variant="info">Approval</app-badge> }
                    @if (!p.self_signup_enabled) { <app-badge variant="muted">Invite only</app-badge> }
                  </div>
                </td>
                <td class="px-4 py-3 text-right">
                  <div class="flex items-center justify-end gap-3">
                    <a [routerLink]="['/apps', appSlug, 'members', 'plans', p.id]" class="text-xs text-text-muted hover:text-text-primary">Edit</a>
                    @if (p.total_memberships === 0) {
                      <button type="button" (click)="deleting.set(p)" class="text-xs text-error hover:text-red-400">Delete</button>
                    }
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (deleting(); as d) {
      <app-confirm-modal title="Delete plan" [message]="'Delete the ' + d.name + ' plan?'" confirmLabel="Delete"
        (confirmed)="onDelete()" (cancelled)="deleting.set(null)" />
    }
  `,
})
export class MembershipPlansListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  plans = signal<MembershipPlan[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  deleting = signal<MembershipPlan | null>(null);

  ngOnInit() {
    this.api.get<MembershipPlan[]>(`/apps/${this.appSlug}/members/plans`).subscribe({
      next: p => { this.plans.set(p); this.loading.set(false); },
      error: () => { this.error.set('Failed to load plans.'); this.loading.set(false); },
    });
  }

  onDelete() {
    const p = this.deleting()!;
    this.deleting.set(null);
    this.api.delete<void>(`/apps/${this.appSlug}/members/plans/${p.id}`).subscribe({
      next: () => this.plans.update(ps => ps.filter(x => x.id !== p.id)),
      error: (e: { error?: { detail?: string } }) => this.error.set(e?.error?.detail ?? 'Could not delete plan.'),
    });
  }
}
