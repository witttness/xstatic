import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import {
  ACTIONS_BY_STATUS, MembershipAction, MembershipDetail, MembershipPlan, actionLabel, statusVariant,
} from '../members.model';

@Component({
  selector: 'app-member-detail',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent],
  template: `
    <a [routerLink]="['/apps', appSlug, 'members']" class="text-xs text-text-muted hover:text-text-primary">&larr; Members</a>
    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm mt-4">{{ error() }}</p>
    } @else if (detail(); as d) {
      <app-page-header [title]="d.membership.display_name || d.membership.email || 'Member'"
        [subtitle]="(d.membership.member_number || 'No member number') + ' · ' + d.membership.plan_name" />

      <div class="grid gap-6 md:grid-cols-3">
        <section class="md:col-span-2 space-y-6">
          <div class="border border-border p-4 space-y-2 text-sm">
            <div class="flex items-center gap-2">
              <app-badge [variant]="variant()">{{ d.membership.status }}</app-badge>
              @if (d.membership.show_in_directory) { <app-badge variant="info">in directory</app-badge> }
            </div>
            <div class="text-text-muted">Email: <span class="text-text-primary">{{ d.membership.email || '—' }}</span></div>
            <div class="text-text-muted">Started: <span class="text-text-primary">{{ d.membership.starts_at ? (d.membership.starts_at | date: 'medium') : '—' }}</span></div>
            <div class="text-text-muted">Expires: <span class="text-text-primary">{{ d.membership.expires_at ? (d.membership.expires_at | date: 'medium') : 'Never' }}</span></div>
          </div>

          <div>
            <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Profile</h3>
            @if (profileEntries().length === 0) {
              <p class="text-sm text-text-muted">No profile data.</p>
            } @else {
              <dl class="border border-border divide-y divide-border text-sm">
                @for (e of profileEntries(); track e.key) {
                  <div class="flex gap-4 px-4 py-2">
                    <dt class="w-40 shrink-0 font-mono text-xs text-text-muted">{{ e.key }}</dt>
                    <dd class="text-text-primary break-all">{{ e.value }}</dd>
                  </div>
                }
              </dl>
            }
          </div>

          <div>
            <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Timeline</h3>
            <ol class="border-l border-border pl-4 space-y-3 text-sm">
              @for (e of d.events; track e.id) {
                <li>
                  <div class="text-text-primary">{{ e.type.replaceAll('_', ' ') }}
                    @if (e.from_status && e.to_status && e.from_status !== e.to_status) {
                      <span class="text-text-muted">({{ e.from_status }} → {{ e.to_status }})</span>
                    }
                  </div>
                  <div class="text-xs text-text-muted">{{ e.created_at | date: 'medium' }} · {{ e.actor_user_id ? 'admin' : e.actor_app_user_id ? 'member' : 'system' }}</div>
                  @if (e.detail) { <div class="text-xs text-text-muted">{{ e.detail }}</div> }
                </li>
              }
            </ol>
          </div>
        </section>

        <aside class="space-y-6">
          <div class="border border-border p-4 space-y-3">
            <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider">Actions</h3>
            @if (actions().length > 0) {
              <input [(ngModel)]="reason" placeholder="Reason (optional, emailed to member)"
                class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
              <div class="flex flex-wrap gap-2">
                @for (a of actions(); track a) {
                  <app-button size="sm" [variant]="a === 'approve' || a === 'renew' || a === 'reinstate' ? 'primary' : 'secondary'"
                    [loading]="busy()" (click)="act(a)">{{ label(a) }}</app-button>
                }
              </div>
            } @else {
              <p class="text-sm text-text-muted">No actions available for a {{ d.membership.status }} membership.</p>
            }
            @if (canChangePlan()) {
              <div class="flex gap-2 pt-2">
                <select [(ngModel)]="newPlanId" aria-label="Change plan"
                  class="flex-1 bg-background border border-border px-2 py-1.5 text-sm text-text-primary">
                  @for (p of plans(); track p.id) { <option [value]="p.id">{{ p.name }}</option> }
                </select>
                <app-button size="sm" variant="secondary" [disabled]="newPlanId === d.membership.plan_id" (click)="changePlan()">Change plan</app-button>
              </div>
            }
            @if (actionError()) { <p class="text-xs text-error">{{ actionError() }}</p> }
          </div>

          <div class="border border-border p-4 space-y-2">
            <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider">Admin notes</h3>
            <p class="text-xs text-text-muted">Never shown to the member.</p>
            <textarea [(ngModel)]="notes" rows="5"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"></textarea>
            <app-button size="sm" variant="secondary" [loading]="savingNotes()" (click)="saveNotes()">Save notes</app-button>
            @if (notesSaved()) { <span class="text-xs text-text-muted ml-2">Saved</span> }
          </div>
        </aside>
      </div>
    }
  `,
})
export class MemberDetailComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id!: string;

  private api = inject(ApiService);

  detail = signal<MembershipDetail | null>(null);
  plans = signal<MembershipPlan[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  actionError = signal<string | null>(null);
  busy = signal(false);
  savingNotes = signal(false);
  notesSaved = signal(false);
  reason = '';
  notes = '';
  newPlanId = '';

  actions = computed(() => {
    const d = this.detail();
    return d ? ACTIONS_BY_STATUS[d.membership.status] : [];
  });
  variant = computed(() => statusVariant(this.detail()?.membership.status ?? 'pending'));
  canChangePlan = computed(() => {
    const s = this.detail()?.membership.status;
    return s === 'pending' || s === 'active' || s === 'suspended';
  });
  profileEntries = computed(() => Object.entries(this.detail()?.profile ?? {}).map(([key, v]) => ({
    key, value: typeof v === 'string' ? v : JSON.stringify(v),
  })));

  private get base() { return `/apps/${this.appSlug}/members`; }

  ngOnInit() {
    this.api.get<MembershipPlan[]>(`${this.base}/plans`).subscribe({ next: p => this.plans.set(p), error: () => {} });
    this.load();
  }

  load() {
    this.api.get<MembershipDetail>(`${this.base}/${this.id}`).subscribe({
      next: d => this.apply(d),
      error: () => { this.error.set('Member not found.'); this.loading.set(false); },
    });
  }

  private apply(d: MembershipDetail) {
    this.detail.set(d);
    this.notes = d.notes ?? '';
    this.newPlanId = d.membership.plan_id;
    this.loading.set(false);
  }

  label(a: MembershipAction) { return actionLabel(a); }

  act(action: MembershipAction) {
    this.busy.set(true);
    this.actionError.set(null);
    this.api.post<MembershipDetail>(`${this.base}/${this.id}/${action}`, { reason: this.reason.trim() || null }).subscribe({
      next: d => { this.busy.set(false); this.reason = ''; this.apply(d); },
      error: (e: { error?: { detail?: string } }) => {
        this.busy.set(false);
        this.actionError.set(e?.error?.detail ?? `Could not ${action} this membership.`);
      },
    });
  }

  changePlan() {
    this.actionError.set(null);
    this.api.post<MembershipDetail>(`${this.base}/${this.id}/change-plan`, { plan_id: this.newPlanId }).subscribe({
      next: d => this.apply(d),
      error: (e: { error?: { detail?: string } }) => this.actionError.set(e?.error?.detail ?? 'Could not change plan.'),
    });
  }

  saveNotes() {
    this.savingNotes.set(true);
    this.notesSaved.set(false);
    this.api.put<MembershipDetail>(`${this.base}/${this.id}/notes`, { notes: this.notes }).subscribe({
      next: d => { this.savingNotes.set(false); this.notesSaved.set(true); this.apply(d); },
      error: () => { this.savingNotes.set(false); this.actionError.set('Could not save notes.'); },
    });
  }
}
