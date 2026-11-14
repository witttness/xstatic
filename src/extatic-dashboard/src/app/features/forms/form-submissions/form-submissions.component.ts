import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { Form, FormSubmission, SubmissionStatus, displayValue } from '../forms.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-form-submissions',
  standalone: true,
  imports: [DatePipe, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent,
    EmptyStateComponent, ConfirmModalComponent],
  template: `
    <app-page-header [title]="(form()?.name ?? 'Form') + ' submissions'" [subtitle]="form() ? form()!.slug : ''">
      <app-button variant="secondary" (click)="download()" [loading]="downloading()">Export CSV</app-button>
      <a [routerLink]="['/apps', appSlug, 'forms', id]"><app-button variant="secondary">Settings &amp; embed</app-button></a>
    </app-page-header>

    <div class="flex flex-wrap items-center gap-3 mb-4">
      <label for="status-filter" class="text-xs font-medium text-text-muted uppercase tracking-wider">Status</label>
      <select id="status-filter" [value]="status()" (change)="setStatus($any($event.target).value)"
        class="bg-background border border-border px-2 py-1 text-sm text-text-primary">
        <option value="">All</option>
        <option value="new">New</option>
        <option value="read">Read</option>
        <option value="spam">Spam</option>
        <option value="archived">Archived</option>
      </select>
      @if (form()?.spam_count) {
        <button type="button" (click)="confirmPurge.set(true)" class="text-xs text-error hover:text-red-400">
          Delete all spam ({{ form()!.spam_count }})
        </button>
      }
    </div>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (!result() || result()!.items.length === 0) {
      <app-empty-state message="No submissions." description="Submissions posted to this form will appear here." />
    } @else {
      <div class="border border-border overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Received</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Summary</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (s of result()!.items; track s.id) {
              <tr class="border-b border-border hover:bg-surface" [class.font-semibold]="s.status === 'new'">
                <td class="px-4 py-3 text-xs text-text-muted whitespace-nowrap">{{ s.created_at | date: 'medium' }}</td>
                <td class="px-4 py-3 text-text-primary">
                  <a [routerLink]="['/apps', appSlug, 'forms', id, 'submissions', s.id]" class="hover:underline truncate block max-w-md">
                    {{ summary(s) }}
                  </a>
                </td>
                <td class="px-4 py-3"><app-badge [variant]="badge(s.status)">{{ s.status }}</app-badge></td>
                <td class="px-4 py-3 text-right">
                  <div class="flex items-center justify-end gap-3">
                    @if (s.status !== 'read') {
                      <button type="button" (click)="setSubmissionStatus(s, 'read')" class="text-xs text-text-muted hover:text-text-primary">Mark read</button>
                    }
                    @if (s.status !== 'spam') {
                      <button type="button" (click)="setSubmissionStatus(s, 'spam')" class="text-xs text-text-muted hover:text-text-primary">Spam</button>
                    }
                    <button type="button" (click)="deleting.set(s)" class="text-xs text-error hover:text-red-400">Delete</button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <div class="flex items-center justify-between mt-4 text-text-muted">
        <span class="text-xs">{{ result()!.total }} submissions · Page {{ page() }} of {{ pageCount() }}</span>
        <div class="flex items-center gap-3">
          <button type="button" (click)="go(page() - 1)" [disabled]="page() <= 1" class="text-sm text-accent disabled:opacity-40">Previous</button>
          <button type="button" (click)="go(page() + 1)" [disabled]="page() >= pageCount()" class="text-sm text-accent disabled:opacity-40">Next</button>
        </div>
      </div>
    }

    @if (deleting()) {
      <app-confirm-modal title="Delete submission" message="Delete this submission? This cannot be undone."
        confirmLabel="Delete" (confirmed)="onDelete()" (cancelled)="deleting.set(null)" />
    }
    @if (confirmPurge()) {
      <app-confirm-modal title="Delete spam" message="Permanently delete every submission marked as spam?"
        confirmLabel="Delete spam" (confirmed)="purgeSpam()" (cancelled)="confirmPurge.set(false)" />
    }
  `,
})
export class FormSubmissionsComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id!: string;

  private api = inject(ApiService);

  form = signal<Form | null>(null);
  result = signal<PagedResult<FormSubmission> | null>(null);
  page = signal(1);
  status = signal<'' | SubmissionStatus>('');
  loading = signal(true);
  downloading = signal(false);
  error = signal<string | null>(null);
  deleting = signal<FormSubmission | null>(null);
  confirmPurge = signal(false);
  pageCount = computed(() => Math.max(1, Math.ceil((this.result()?.total ?? 0) / PAGE_SIZE)));

  private get base() { return `/apps/${this.appSlug}/forms/${this.id}`; }

  ngOnInit() {
    this.loadForm();
    this.load();
  }

  loadForm() {
    this.api.get<Form>(this.base).subscribe({ next: f => this.form.set(f), error: () => {} });
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<PagedResult<FormSubmission>>(`${this.base}/submissions`, {
      page: this.page(), page_size: PAGE_SIZE, status: this.status() || null,
    }).subscribe({
      next: r => { this.result.set(r); this.loading.set(false); },
      error: () => { this.error.set('Failed to load submissions.'); this.loading.set(false); },
    });
  }

  setStatus(value: '' | SubmissionStatus) {
    this.status.set(value);
    this.page.set(1);
    this.load();
  }

  go(page: number) {
    if (page < 1 || page > this.pageCount()) return;
    this.page.set(page);
    this.load();
  }

  summary(s: FormSubmission): string {
    const entries = Object.entries(s.data ?? {});
    if (entries.length === 0) return '(empty)';
    return entries.slice(0, 3).map(([k, v]) => `${k}: ${displayValue(v)}`).join(' · ');
  }

  badge(status: SubmissionStatus): 'info' | 'muted' | 'error' {
    return status === 'new' ? 'info' : status === 'spam' ? 'error' : 'muted';
  }

  setSubmissionStatus(s: FormSubmission, status: SubmissionStatus) {
    this.api.patch<FormSubmission>(`${this.base}/submissions/${s.id}`, { status }).subscribe({
      next: updated => {
        this.result.update(r => r && { ...r, items: r.items.map(x => x.id === updated.id ? updated : x) });
        this.loadForm();
      },
      error: () => this.error.set('Failed to update submission.'),
    });
  }

  onDelete() {
    const s = this.deleting()!;
    this.deleting.set(null);
    this.api.delete<void>(`${this.base}/submissions/${s.id}`).subscribe({
      next: () => { this.load(); this.loadForm(); },
      error: () => this.error.set('Failed to delete submission.'),
    });
  }

  purgeSpam() {
    this.confirmPurge.set(false);
    this.api.delete<{ deleted: number }>(`${this.base}/submissions/spam`).subscribe({
      next: () => { this.load(); this.loadForm(); },
      error: () => this.error.set('Failed to delete spam.'),
    });
  }

  download() {
    this.downloading.set(true);
    this.api.getBlob(`${this.base}/submissions/export.csv`, { status: this.status() || null }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.form()?.slug ?? 'form'}-submissions.csv`;
        a.click();
        URL.revokeObjectURL(url);
        this.downloading.set(false);
      },
      error: () => { this.error.set('Failed to export submissions.'); this.downloading.set(false); },
    });
  }
}
