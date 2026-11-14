import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { FormSubmission, SubmissionStatus, displayValue } from '../forms.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';

@Component({
  selector: 'app-submission-detail',
  standalone: true,
  imports: [DatePipe, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, ConfirmModalComponent],
  template: `
    <app-page-header title="Submission" [subtitle]="submission() ? (submission()!.created_at | date: 'medium') ?? '' : ''">
      <a [routerLink]="['/apps', appSlug, 'forms', id, 'submissions']"><app-button variant="secondary">Back</app-button></a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (submission(); as s) {
      <div class="flex flex-wrap items-center gap-3 mb-4">
        <app-badge [variant]="s.status === 'spam' ? 'error' : s.status === 'new' ? 'info' : 'muted'">{{ s.status }}</app-badge>
        @for (st of statuses; track st) {
          @if (st !== s.status) {
            <button type="button" (click)="setStatus(st)" class="text-xs text-text-muted hover:text-text-primary">Mark {{ st }}</button>
          }
        }
        <button type="button" (click)="confirmDelete.set(true)" class="text-xs text-error hover:text-red-400">Delete</button>
      </div>

      <dl class="border border-border divide-y divide-border">
        @for (f of fields(); track f.key) {
          <div class="grid grid-cols-1 sm:grid-cols-3 gap-1 px-4 py-3">
            <dt class="text-xs font-medium text-text-muted uppercase tracking-wider break-all">{{ f.key }}</dt>
            <dd class="sm:col-span-2 text-sm text-text-primary whitespace-pre-wrap break-words">{{ f.value }}</dd>
          </div>
        } @empty {
          <p class="px-4 py-3 text-sm text-text-muted">No fields.</p>
        }
      </dl>

      <dl class="mt-6 text-xs text-text-muted space-y-1">
        <div><dt class="inline">Submission id: </dt><dd class="inline font-mono">{{ s.id }}</dd></div>
        @if (s.app_user_id) { <div><dt class="inline">AppUser: </dt><dd class="inline font-mono">{{ s.app_user_id }}</dd></div> }
        @if (s.referrer) { <div><dt class="inline">Referrer: </dt><dd class="inline break-all">{{ s.referrer }}</dd></div> }
        @if (s.user_agent) { <div><dt class="inline">User agent: </dt><dd class="inline break-all">{{ s.user_agent }}</dd></div> }
      </dl>
    }

    @if (confirmDelete()) {
      <app-confirm-modal title="Delete submission" message="Delete this submission? This cannot be undone."
        confirmLabel="Delete" (confirmed)="onDelete()" (cancelled)="confirmDelete.set(false)" />
    }
  `,
})
export class SubmissionDetailComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id!: string;
  @Input() submissionId!: string;

  private api = inject(ApiService);
  private router = inject(Router);

  readonly statuses: SubmissionStatus[] = ['new', 'read', 'spam', 'archived'];
  submission = signal<FormSubmission | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);
  confirmDelete = signal(false);
  fields = computed(() => Object.entries(this.submission()?.data ?? {}).map(([key, v]) => ({ key, value: displayValue(v) })));

  private get url() { return `/apps/${this.appSlug}/forms/${this.id}/submissions/${this.submissionId}`; }

  ngOnInit() {
    this.api.get<FormSubmission>(this.url).subscribe({
      next: s => {
        this.submission.set(s);
        this.loading.set(false);
        // Opening a new submission marks it read (silently ignored for viewers without write access).
        if (s.status === 'new') this.setStatus('read', true);
      },
      error: () => { this.error.set('Failed to load submission.'); this.loading.set(false); },
    });
  }

  setStatus(status: SubmissionStatus, quiet = false) {
    this.api.patch<FormSubmission>(this.url, { status }).subscribe({
      next: s => this.submission.set(s),
      error: () => { if (!quiet) this.error.set('Failed to update submission.'); },
    });
  }

  onDelete() {
    this.confirmDelete.set(false);
    this.api.delete<void>(this.url).subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'forms', this.id, 'submissions']),
      error: () => this.error.set('Failed to delete submission.'),
    });
  }
}
