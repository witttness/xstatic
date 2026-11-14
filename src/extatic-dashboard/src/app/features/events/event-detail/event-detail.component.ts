import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import {
  EventRegistration, ExtaticEvent, RegistrationStatus, seatSummary, statusVariant,
} from '../event.model';

type PendingAction =
  | { kind: 'publish' }
  | { kind: 'cancel-event' }
  | { kind: 'delete' }
  | { kind: 'cancel-registration'; registration: EventRegistration };

const inputClass =
  'bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';

@Component({
  selector: 'app-event-detail',
  standalone: true,
  imports: [
    FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent,
    LoadingSpinnerComponent, EmptyStateComponent, ConfirmModalComponent,
  ],
  template: `
    @if (!event()) {
      @if (error()) { <p class="text-error text-sm">{{ error() }}</p> } @else { <app-loading-spinner /> }
    } @else {
      <app-page-header [title]="event()!.title" [subtitle]="formatDate(event()!.starts_at) + ' · ' + event()!.timezone">
        <app-badge [variant]="variant(event()!.status)">{{ event()!.status }}</app-badge>
        @if (event()!.status !== 'cancelled') {
          <a [routerLink]="['/apps', appSlug, 'events', event()!.id, 'edit']"><app-button variant="secondary">Edit</app-button></a>
        }
        @if (event()!.status === 'draft') {
          <app-button (click)="pending.set({ kind: 'publish' })">Publish</app-button>
        }
        @if (event()!.status !== 'cancelled') {
          <app-button variant="danger" (click)="pending.set({ kind: 'cancel-event' })">Cancel Event</app-button>
        }
        <app-button variant="secondary" (click)="pending.set({ kind: 'delete' })">Delete</app-button>
      </app-page-header>

      <div class="grid grid-cols-2 md:grid-cols-4 gap-3 mb-6">
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Seats</div>
          <div class="text-sm text-text-primary mt-1">{{ seats() }}</div>
        </div>
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Waitlisted seats</div>
          <div class="text-sm text-text-primary mt-1">{{ event()!.stats.waitlisted_seats }}</div>
        </div>
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Cancelled</div>
          <div class="text-sm text-text-primary mt-1">{{ event()!.stats.cancelled_registrations }}</div>
        </div>
        <div class="border border-border p-3">
          <div class="text-xs text-text-muted uppercase tracking-wider">Checked in</div>
          <div class="text-sm text-text-primary mt-1">{{ event()!.stats.checked_in_seats }} seats</div>
        </div>
      </div>

      @if (!event()!.registration_open && event()!.registration_closed_reason) {
        <p class="text-xs text-text-muted mb-4">Registration closed: <span class="font-mono">{{ event()!.registration_closed_reason }}</span></p>
      }

      <div class="flex flex-wrap items-end gap-2 mb-4">
        <input [(ngModel)]="search" (keyup.enter)="go(1)" placeholder="Search name or email" class="${inputClass}" />
        <select [(ngModel)]="statusFilter" (ngModelChange)="go(1)" class="${inputClass}">
          <option [ngValue]="null">All statuses</option>
          <option value="confirmed">Confirmed</option>
          <option value="waitlisted">Waitlisted</option>
          <option value="cancelled">Cancelled</option>
        </select>
        <app-button variant="secondary" (click)="go(1)">Search</app-button>
        <span class="flex-1"></span>
        <app-button variant="secondary" (click)="exportCsv()">Export CSV</app-button>
        <app-button (click)="showAdd.set(!showAdd())">Add Registration</app-button>
      </div>

      @if (showAdd()) {
        <form (ngSubmit)="addRegistration()" class="border border-border p-4 mb-4 flex flex-wrap items-end gap-3">
          <input [(ngModel)]="newName" name="name" placeholder="Name" class="${inputClass}" />
          <input [(ngModel)]="newEmail" name="email" type="email" placeholder="Email" class="${inputClass}" />
          <input [(ngModel)]="newSeats" name="seats" type="number" min="1" class="${inputClass} w-20" />
          <label class="flex items-center gap-2 text-xs text-text-primary">
            <input type="checkbox" [(ngModel)]="newOverride" name="override" class="accent-accent" /> Override capacity
          </label>
          <app-button type="submit">Add</app-button>
        </form>
      }
      @if (actionError()) { <p class="text-error text-sm mb-3">{{ actionError() }}</p> }
      @if (notice()) { <p class="text-success text-sm mb-3">{{ notice() }}</p> }

      @if (registrations().length === 0) {
        <app-empty-state message="No registrations." />
      } @else {
        <div class="border border-border overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="border-b border-border">
                <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Name</th>
                <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Seats</th>
                <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
                <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Registered</th>
                <th class="px-4 py-3"></th>
              </tr>
            </thead>
            <tbody>
              @for (r of registrations(); track r.id) {
                <tr class="border-b border-border hover:bg-surface">
                  <td class="px-4 py-3">
                    <div class="text-text-primary">{{ r.name }}</div>
                    <div class="text-xs text-text-muted">{{ r.email }}</div>
                  </td>
                  <td class="px-4 py-3 text-text-primary">{{ r.seats }}</td>
                  <td class="px-4 py-3">
                    <app-badge [variant]="variant(r.status)">{{ r.status }}</app-badge>
                    @if (r.checked_in_at) { <app-badge variant="info">checked in</app-badge> }
                  </td>
                  <td class="px-4 py-3 text-xs text-text-muted">{{ formatDate(r.created_at) }}</td>
                  <td class="px-4 py-3 text-right whitespace-nowrap">
                    @if (r.status === 'confirmed') {
                      <button type="button" class="text-xs text-accent hover:underline mr-3" (click)="toggleCheckIn(r)">
                        {{ r.checked_in_at ? 'Undo check-in' : 'Check in' }}
                      </button>
                    }
                    @if (r.status === 'waitlisted') {
                      <button type="button" class="text-xs text-accent hover:underline mr-3" (click)="promote(r)">Promote</button>
                    }
                    @if (r.status !== 'cancelled') {
                      <button type="button" class="text-xs text-error hover:text-red-400"
                        (click)="pending.set({ kind: 'cancel-registration', registration: r })">Cancel</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (total() > pageSize) {
          <div class="flex items-center justify-between mt-3 text-xs text-text-muted">
            <span>{{ total() }} registrations</span>
            <div class="flex gap-2">
              <app-button variant="secondary" size="sm" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</app-button>
              <app-button variant="secondary" size="sm" [disabled]="page() * pageSize >= total()" (click)="go(page() + 1)">Next</app-button>
            </div>
          </div>
        }
      }

      @if (pending(); as p) {
        <app-confirm-modal
          [title]="confirmTitle(p)"
          [message]="confirmMessage(p)"
          [confirmWord]="p.kind === 'delete' ? event()!.slug : ''"
          [confirmLabel]="confirmTitle(p)"
          (confirmed)="runPending()"
          (cancelled)="pending.set(null)"
        />
      }
    }
  `,
})
export class EventDetailComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id!: string;

  private api = inject(ApiService);
  private router = inject(Router);

  readonly pageSize = 50;
  event = signal<ExtaticEvent | null>(null);
  registrations = signal<EventRegistration[]>([]);
  total = signal(0);
  page = signal(1);
  error = signal<string | null>(null);
  actionError = signal<string | null>(null);
  notice = signal<string | null>(null);
  pending = signal<PendingAction | null>(null);
  showAdd = signal(false);

  search = '';
  statusFilter: RegistrationStatus | null = null;
  newName = '';
  newEmail = '';
  newSeats = 1;
  newOverride = false;

  private get base() {
    return `/apps/${this.appSlug}/events/${this.id}`;
  }

  ngOnInit() {
    this.loadEvent();
    this.loadRegistrations();
  }

  loadEvent() {
    this.api.get<ExtaticEvent>(this.base).subscribe({
      next: ev => this.event.set(ev),
      error: () => this.error.set('Failed to load event.'),
    });
  }

  loadRegistrations() {
    this.api.get<PagedResult<EventRegistration>>(`${this.base}/registrations`, {
      page: this.page(), page_size: this.pageSize, status: this.statusFilter, search: this.search.trim() || null,
    }).subscribe({
      next: res => { this.registrations.set(res.items); this.total.set(res.total); },
      error: () => this.actionError.set('Failed to load registrations.'),
    });
  }

  go(page: number) {
    this.page.set(page);
    this.loadRegistrations();
  }

  refresh() {
    this.loadEvent();
    this.loadRegistrations();
  }

  seats() {
    return seatSummary(this.event()!);
  }

  variant(status: ExtaticEvent['status'] | RegistrationStatus) {
    return statusVariant(status);
  }

  confirmTitle(p: PendingAction) {
    switch (p.kind) {
      case 'publish': return 'Publish';
      case 'cancel-event': return 'Cancel Event';
      case 'delete': return 'Delete';
      case 'cancel-registration': return 'Cancel Registration';
    }
  }

  confirmMessage(p: PendingAction) {
    switch (p.kind) {
      case 'publish': return 'Publish this event? It becomes visible on your site and opens for registration.';
      case 'cancel-event': return 'Cancel this event? Every active registrant is emailed. This cannot be undone.';
      case 'delete': return 'Delete this event and all its registrations permanently? No emails are sent.';
      case 'cancel-registration':
        return `Cancel the registration for ${p.registration.name}? They are emailed and freed seats go to the waitlist.`;
    }
  }

  runPending() {
    const p = this.pending();
    this.pending.set(null);
    if (!p) return;
    this.actionError.set(null);
    const fail = (err: HttpErrorResponse) => this.actionError.set(err.error?.detail ?? 'Action failed.');
    switch (p.kind) {
      case 'publish':
        this.api.post<ExtaticEvent>(`${this.base}/publish`).subscribe({ next: ev => this.event.set(ev), error: fail });
        break;
      case 'cancel-event':
        this.api.post<ExtaticEvent>(`${this.base}/cancel`).subscribe({ next: ev => this.event.set(ev), error: fail });
        break;
      case 'delete':
        this.api.delete<void>(this.base).subscribe({
          next: () => this.router.navigate(['/apps', this.appSlug, 'events']),
          error: fail,
        });
        break;
      case 'cancel-registration':
        this.api.post<EventRegistration>(`${this.base}/registrations/${p.registration.id}/cancel`)
          .subscribe({ next: () => this.refresh(), error: fail });
        break;
    }
  }

  promote(r: EventRegistration, overrideCapacity = false) {
    this.actionError.set(null);
    this.api.post<EventRegistration>(`${this.base}/registrations/${r.id}/promote`, { override_capacity: overrideCapacity })
      .subscribe({
        next: () => this.refresh(),
        error: (err: HttpErrorResponse) => {
          if (err.status === 409 && !overrideCapacity
              && confirm(`${err.error?.detail ?? 'Not enough seats.'} Promote anyway and exceed capacity?`)) {
            this.promote(r, true);
          } else {
            this.actionError.set(err.error?.detail ?? 'Promotion failed.');
          }
        },
      });
  }

  toggleCheckIn(r: EventRegistration) {
    this.api.put<EventRegistration>(`${this.base}/registrations/${r.id}/check-in`, { checked_in: !r.checked_in_at })
      .subscribe({
        next: updated => {
          this.registrations.update(list => list.map(x => (x.id === updated.id ? updated : x)));
          this.loadEvent();
        },
        error: (err: HttpErrorResponse) => this.actionError.set(err.error?.detail ?? 'Check-in failed.'),
      });
  }

  addRegistration() {
    this.actionError.set(null);
    this.notice.set(null);
    this.api.post<{ registration: EventRegistration; manage_token: string }>(`${this.base}/registrations`, {
      name: this.newName, email: this.newEmail, seats: Number(this.newSeats) || 1, override_capacity: this.newOverride,
    }).subscribe({
      next: res => {
        this.notice.set(`Added ${res.registration.name} (${res.registration.status}). A confirmation email was sent.`);
        this.newName = '';
        this.newEmail = '';
        this.newSeats = 1;
        this.newOverride = false;
        this.showAdd.set(false);
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        const errors = err.error?.errors as { message: string }[] | undefined;
        this.actionError.set(errors?.length ? errors.map(e => e.message).join(' ') : err.error?.detail ?? 'Failed to add.');
      },
    });
  }

  exportCsv() {
    this.api.getBlob(`${this.base}/registrations/export.csv`, {
      status: this.statusFilter, search: this.search.trim() || null,
    }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.event()?.slug ?? 'event'}-registrations.csv`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.actionError.set('Export failed.'),
    });
  }

  formatDate(d: string) {
    return new Date(d).toLocaleString();
  }
}
