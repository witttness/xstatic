import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService, QueryParams } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { Campaign, Donation, DonationStatus, DonationSummary } from '../donations.models';
import { formatMoney, fromMinor, toMinor } from '../money';

const INPUT = 'bg-background border border-border px-2 py-1.5 text-sm text-text-primary focus:border-accent focus:outline-none';

@Component({
  selector: 'app-donations-overview',
  standalone: true,
  imports: [FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent],
  template: `
    <app-page-header title="Donations" subtitle="Payments are processed by your own provider account.">
      <a [routerLink]="['/apps', appSlug, 'donations', 'campaigns']"><app-button variant="secondary">Campaigns</app-button></a>
      <a [routerLink]="['/apps', appSlug, 'donations', 'setup']"><app-button variant="secondary">Provider setup</app-button></a>
    </app-page-header>

    @if (summary(); as s) {
      <div class="grid grid-cols-2 lg:grid-cols-4 gap-3 mb-6">
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Total raised</div>
          @for (t of s.total_raised; track t.currency) { <div class="text-lg font-semibold text-text-primary">{{ money(t.amount_minor, t.currency) }}</div> }
          @empty { <div class="text-lg font-semibold text-text-primary">—</div> }
        </div>
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Last 30 days</div>
          @for (t of s.last_30_days; track t.currency) { <div class="text-lg font-semibold text-text-primary">{{ money(t.amount_minor, t.currency) }}</div> }
          @empty { <div class="text-lg font-semibold text-text-primary">—</div> }
        </div>
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Recurring donors</div>
          <div class="text-lg font-semibold text-text-primary">{{ s.recurring_count }}</div>
        </div>
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Pending checkouts</div>
          <div class="text-lg font-semibold text-text-primary">{{ s.pending_count }}</div>
        </div>
      </div>
    }

    <div class="flex flex-wrap items-end gap-2 mb-4">
      <input [(ngModel)]="q" placeholder="Search donor name or email" class="${INPUT} w-64" (keyup.enter)="search()" />
      <select [(ngModel)]="status" class="${INPUT}" (change)="search()">
        <option value="">All statuses</option>
        @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
      </select>
      <select [(ngModel)]="campaignId" class="${INPUT}" (change)="search()">
        <option value="">All campaigns</option>
        @for (c of campaigns(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
      </select>
      <input type="date" [(ngModel)]="from" class="${INPUT}" (change)="search()" aria-label="From" />
      <input type="date" [(ngModel)]="to" class="${INPUT}" (change)="search()" aria-label="To" />
      <app-button size="sm" variant="secondary" (click)="search()">Filter</app-button>
      <app-button size="sm" variant="secondary" (click)="exportCsv()">Export CSV</app-button>
    </div>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (donations().length === 0) {
      <app-empty-state message="No donations found." />
    } @else {
      <div class="border border-border overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border text-left text-xs font-medium text-text-muted uppercase tracking-wider">
              <th class="px-4 py-3">Date</th><th class="px-4 py-3">Donor</th><th class="px-4 py-3">Campaign</th>
              <th class="px-4 py-3">Amount</th><th class="px-4 py-3">Status</th><th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (d of donations(); track d.id) {
              <tr class="border-b border-border hover:bg-surface align-top">
                <td class="px-4 py-3 text-xs text-text-muted whitespace-nowrap">{{ d.created_at.slice(0, 16).replace('T', ' ') }}</td>
                <td class="px-4 py-3">
                  <div class="text-text-primary">{{ d.donor_name || '—' }} @if (d.is_anonymous) { <app-badge>anonymous</app-badge> }</div>
                  <div class="text-xs text-text-muted">{{ d.donor_email }}</div>
                  @if (d.message) { <div class="text-xs text-text-muted italic mt-1">"{{ d.message }}"</div> }
                </td>
                <td class="px-4 py-3 text-xs">{{ d.campaign_name || '—' }}</td>
                <td class="px-4 py-3 whitespace-nowrap">
                  {{ money(d.amount_minor, d.currency) }}
                  @if (d.interval !== 'one_time') { <span class="text-xs text-text-muted">/ {{ d.interval === 'monthly' ? 'month' : 'year' }}</span> }
                  @if (d.refunded_amount_minor > 0) { <div class="text-xs text-warning">−{{ money(d.refunded_amount_minor, d.currency) }} refunded</div> }
                </td>
                <td class="px-4 py-3"><app-badge [variant]="badge(d.status)">{{ d.status }}</app-badge></td>
                <td class="px-4 py-3 text-right">
                  @if (canRefund(d)) {
                    <button type="button" class="text-xs text-error hover:underline" (click)="startRefund(d)">Refund</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <div class="flex items-center justify-between mt-3 text-xs text-text-muted">
        <span>{{ total() }} donation{{ total() === 1 ? '' : 's' }}</span>
        <div class="flex gap-2">
          <app-button size="sm" variant="secondary" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</app-button>
          <app-button size="sm" variant="secondary" [disabled]="page() * pageSize >= total()" (click)="go(page() + 1)">Next</app-button>
        </div>
      </div>
    }

    @if (refunding(); as d) {
      <div class="fixed inset-0 z-50 flex items-center justify-center">
        <div class="absolute inset-0 bg-black/60" (click)="refunding.set(null)"></div>
        <div class="relative bg-surface border border-border w-full max-w-md p-6 z-10">
          <h3 class="text-base font-semibold text-text-primary mb-2">Refund donation</h3>
          <p class="text-sm text-text-muted mb-4">
            Refund up to {{ money(d.amount_minor - d.refunded_amount_minor, d.currency) }} to {{ d.donor_name || 'the donor' }}
            through your payment provider. This cannot be undone.
          </p>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="refund_amount">
            Amount ({{ d.currency.toUpperCase() }})
          </label>
          <input id="refund_amount" [(ngModel)]="refundAmount" class="${INPUT} w-full mb-2" inputmode="decimal" />
          @if (refundError()) { <p class="text-xs text-error mb-2">{{ refundError() }}</p> }
          <div class="flex justify-end gap-2">
            <app-button variant="secondary" (click)="refunding.set(null)">Cancel</app-button>
            <app-button variant="danger" [loading]="refundBusy()" (click)="confirmRefund()">Refund</app-button>
          </div>
        </div>
      </div>
    }
  `,
})
export class DonationsOverviewComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);

  readonly statuses: DonationStatus[] = ['pending', 'succeeded', 'failed', 'expired', 'refunded'];
  readonly pageSize = 20;
  money = formatMoney;

  summary = signal<DonationSummary | null>(null);
  campaigns = signal<Campaign[]>([]);
  donations = signal<Donation[]>([]);
  total = signal(0);
  page = signal(1);
  loading = signal(true);
  error = signal<string | null>(null);

  q = '';
  status = '';
  campaignId = '';
  from = '';
  to = '';

  refunding = signal<Donation | null>(null);
  refundAmount = '';
  refundBusy = signal(false);
  refundError = signal<string | null>(null);

  private get base() { return `/apps/${this.appSlug}/donations`; }

  ngOnInit() {
    this.loadSummary();
    // Campaign names for the filter; viewers may not be allowed to list campaigns, which is fine.
    this.api.get<Campaign[]>(`${this.base}/campaigns`).subscribe({ next: cs => this.campaigns.set(cs), error: () => {} });
    this.load();
  }

  loadSummary() {
    this.api.get<DonationSummary>(`${this.base}/summary`).subscribe({ next: s => this.summary.set(s), error: () => {} });
  }

  private filters(): QueryParams {
    return {
      q: this.q.trim() || null,
      status: this.status || null,
      campaign_id: this.campaignId || null,
      from: this.from ? new Date(this.from + 'T00:00:00').toISOString() : null,
      to: this.to ? new Date(this.to + 'T23:59:59.999').toISOString() : null,
    };
  }

  load() {
    this.loading.set(true);
    this.api.get<PagedResult<Donation>>(this.base, { ...this.filters(), page: this.page(), page_size: this.pageSize }).subscribe({
      next: r => { this.donations.set(r.items); this.total.set(r.total); this.loading.set(false); this.error.set(null); },
      error: () => { this.error.set('Failed to load donations.'); this.loading.set(false); },
    });
  }

  search() { this.page.set(1); this.load(); }
  go(page: number) { this.page.set(page); this.load(); }

  exportCsv() {
    this.api.getBlob(`${this.base}/export.csv`, this.filters()).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'donations.csv';
      a.click();
      URL.revokeObjectURL(url);
    });
  }

  badge(status: DonationStatus): 'success' | 'error' | 'warning' | 'muted' | 'info' {
    return ({ succeeded: 'success', failed: 'error', refunded: 'warning', expired: 'muted', pending: 'info' } as const)[status];
  }

  canRefund(d: Donation) {
    return d.status === 'succeeded' && d.refunded_amount_minor < d.amount_minor && !!d.provider_payment_id;
  }

  startRefund(d: Donation) {
    this.refundAmount = fromMinor(d.amount_minor - d.refunded_amount_minor, d.currency);
    this.refundError.set(null);
    this.refunding.set(d);
  }

  confirmRefund() {
    const d = this.refunding()!;
    const amount = toMinor(this.refundAmount, d.currency);
    if (amount === null || amount < 1 || amount > d.amount_minor - d.refunded_amount_minor) {
      this.refundError.set('Enter an amount up to the refundable balance.');
      return;
    }
    this.refundBusy.set(true);
    this.api.post<Donation>(`${this.base}/${d.id}/refund`, { amount_minor: amount }).subscribe({
      next: updated => {
        this.refundBusy.set(false);
        this.refunding.set(null);
        this.donations.update(list => list.map(x => (x.id === updated.id ? updated : x)));
        this.loadSummary();
      },
      error: (e: HttpErrorResponse) => {
        this.refundBusy.set(false);
        this.refundError.set(e.error?.detail ?? 'Refund failed.');
      },
    });
  }
}
