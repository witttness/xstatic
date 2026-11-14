import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import {
  ACTIONS_BY_STATUS, MEMBERSHIP_STATUSES, MembershipAction, MembershipPlan, MembershipStatus, MembershipSummary,
  MembersStats, actionLabel, downloadBlob, statusVariant,
} from '../members.model';

@Component({
  selector: 'app-members-list',
  standalone: true,
  imports: [
    DatePipe, FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent,
    LoadingSpinnerComponent, EmptyStateComponent,
  ],
  template: `
    <app-page-header title="Members" subtitle="Membership applications, renewals and status">
      <a [routerLink]="['/apps', appSlug, 'members', 'plans']"><app-button variant="secondary">Plans</app-button></a>
      <a [routerLink]="['/apps', appSlug, 'members', 'settings']"><app-button variant="secondary">Settings</app-button></a>
      <app-button variant="secondary" [loading]="exporting()" (click)="exportCsv()">Export CSV</app-button>
    </app-page-header>

    @if (stats(); as s) {
      <div class="grid grid-cols-2 md:grid-cols-4 gap-3 mb-6">
        <div class="border border-border p-3"><div class="text-xs text-text-muted uppercase tracking-wider">Active</div><div class="text-lg text-text-primary">{{ s.by_status['active'] }}</div></div>
        <div class="border border-border p-3"><div class="text-xs text-text-muted uppercase tracking-wider">Pending</div><div class="text-lg text-text-primary">{{ s.by_status['pending'] }}</div></div>
        <div class="border border-border p-3"><div class="text-xs text-text-muted uppercase tracking-wider">Expiring in 30 days</div><div class="text-lg text-text-primary">{{ s.expiring_soon }}</div></div>
        <div class="border border-border p-3"><div class="text-xs text-text-muted uppercase tracking-wider">Total</div><div class="text-lg text-text-primary">{{ s.total }}</div></div>
      </div>
    }

    <div class="flex flex-wrap gap-1 border-b border-border mb-4" role="tablist">
      @for (tab of tabs; track tab.value) {
        <button type="button" role="tab" [attr.aria-selected]="status() === tab.value"
          class="px-3 py-2 text-sm -mb-px border-b-2"
          [class.border-accent]="status() === tab.value" [class.text-text-primary]="status() === tab.value"
          [class.border-transparent]="status() !== tab.value" [class.text-text-muted]="status() !== tab.value"
          (click)="setStatus(tab.value)">{{ tab.label }}</button>
      }
    </div>

    <div class="flex flex-wrap gap-3 mb-4">
      <input type="search" [ngModel]="search()" (ngModelChange)="search.set($event)" (keyup.enter)="go(1)"
        placeholder="Search name, email or member number"
        class="flex-1 min-w-48 bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
      <select [ngModel]="planId()" (ngModelChange)="planId.set($event); go(1)" aria-label="Plan"
        class="bg-background border border-border px-3 py-2 text-sm text-text-primary">
        <option value="">All plans</option>
        @for (p of plans(); track p.id) { <option [value]="p.id">{{ p.name }}</option> }
      </select>
      <app-button variant="secondary" (click)="go(1)">Search</app-button>
    </div>

    @if (actionError()) { <p class="text-error text-sm mb-3">{{ actionError() }}</p> }

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (members().length === 0) {
      <app-empty-state message="No members found." description="Members appear here when users join a plan from your site." />
    } @else {
      <div class="border border-border overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Member</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Number</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Plan</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Expires</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (m of members(); track m.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3">
                  <a [routerLink]="['/apps', appSlug, 'members', m.id]" class="text-text-primary hover:underline">{{ m.display_name || m.email || m.app_user_id }}</a>
                  @if (m.display_name && m.email) { <div class="text-xs text-text-muted">{{ m.email }}</div> }
                </td>
                <td class="px-4 py-3 font-mono text-xs text-text-primary">{{ m.member_number || '—' }}</td>
                <td class="px-4 py-3 text-text-primary">{{ m.plan_name }}</td>
                <td class="px-4 py-3"><app-badge [variant]="variant(m.status)">{{ m.status }}</app-badge></td>
                <td class="px-4 py-3 text-text-muted">{{ m.expires_at ? (m.expires_at | date: 'mediumDate') : '—' }}</td>
                <td class="px-4 py-3 text-right">
                  <div class="flex items-center justify-end gap-3">
                    @for (a of actionsFor(m.status); track a) {
                      <button type="button" (click)="act(m, a)" [disabled]="busy() === m.id"
                        class="text-xs hover:text-text-primary"
                        [class.text-error]="a === 'reject' || a === 'cancel' || a === 'suspend'"
                        [class.text-text-muted]="a !== 'reject' && a !== 'cancel' && a !== 'suspend'">{{ label(a) }}</button>
                    }
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <div class="flex items-center justify-between mt-4 text-sm text-text-muted">
        <span>{{ total() }} member{{ total() === 1 ? '' : 's' }}</span>
        <div class="flex gap-2">
          <app-button variant="secondary" size="sm" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</app-button>
          <app-button variant="secondary" size="sm" [disabled]="page() * pageSize >= total()" (click)="go(page() + 1)">Next</app-button>
        </div>
      </div>
    }
  `,
})
export class MembersListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  readonly pageSize = 20;
  readonly tabs: { value: MembershipStatus | ''; label: string }[] = [
    { value: '', label: 'All' },
    ...MEMBERSHIP_STATUSES.map(s => ({ value: s, label: s.charAt(0).toUpperCase() + s.slice(1) })),
  ];

  members = signal<MembershipSummary[]>([]);
  plans = signal<MembershipPlan[]>([]);
  stats = signal<MembersStats | null>(null);
  total = signal(0);
  page = signal(1);
  status = signal<MembershipStatus | ''>('');
  search = signal('');
  planId = signal('');
  loading = signal(true);
  error = signal<string | null>(null);
  actionError = signal<string | null>(null);
  busy = signal<string | null>(null);
  exporting = signal(false);

  private get base() { return `/apps/${this.appSlug}/members`; }

  ngOnInit() {
    this.api.get<MembershipPlan[]>(`${this.base}/plans`).subscribe({ next: p => this.plans.set(p), error: () => {} });
    this.loadStats();
    this.load();
  }

  private query() {
    return {
      status: this.status() || null,
      plan_id: this.planId() || null,
      q: this.search().trim() || null,
    };
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<PagedResult<MembershipSummary>>(this.base, { ...this.query(), page: this.page(), page_size: this.pageSize })
      .subscribe({
        next: r => { this.members.set(r.items); this.total.set(r.total); this.loading.set(false); },
        error: () => { this.error.set('Failed to load members.'); this.loading.set(false); },
      });
  }

  loadStats() {
    this.api.get<MembersStats>(`${this.base}/stats`).subscribe({ next: s => this.stats.set(s), error: () => {} });
  }

  setStatus(status: MembershipStatus | '') {
    this.status.set(status);
    this.go(1);
  }

  go(page: number) {
    this.page.set(page);
    this.load();
  }

  actionsFor(status: MembershipStatus) { return ACTIONS_BY_STATUS[status]; }
  variant(status: MembershipStatus) { return statusVariant(status); }
  label(action: MembershipAction) { return actionLabel(action); }

  act(m: MembershipSummary, action: MembershipAction) {
    this.busy.set(m.id);
    this.actionError.set(null);
    this.api.post(`${this.base}/${m.id}/${action}`, {}).subscribe({
      next: () => { this.busy.set(null); this.load(); this.loadStats(); },
      error: (e: { error?: { detail?: string } }) => {
        this.busy.set(null);
        this.actionError.set(e?.error?.detail ?? `Could not ${action} this membership.`);
      },
    });
  }

  exportCsv() {
    this.exporting.set(true);
    this.api.getBlob(`${this.base}/export.csv`, this.query()).subscribe({
      next: blob => { downloadBlob(blob, 'members.csv'); this.exporting.set(false); },
      error: () => { this.actionError.set('Export failed.'); this.exporting.set(false); },
    });
  }
}
