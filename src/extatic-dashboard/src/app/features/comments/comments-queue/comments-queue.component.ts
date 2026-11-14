import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { CommentsNavComponent } from '../comments-nav.component';
import {
  AdminComment, BulkModerateResult, CommentStats, CommentStatus, ModerationAction,
} from '../comments.model';

const PAGE_SIZE = 20;

/**
 * Moderation queue. Comment bodies and author fields are rendered ONLY through interpolation
 * ({{ }}), which Angular escapes — never via innerHTML.
 */
@Component({
  selector: 'app-comments-queue',
  standalone: true,
  imports: [
    DatePipe, FormsModule, PageHeaderComponent, ButtonComponent, BadgeComponent,
    LoadingSpinnerComponent, EmptyStateComponent, ConfirmModalComponent, CommentsNavComponent,
  ],
  template: `
    <app-page-header title="Comments" subtitle="Moderate comments posted on your site." />
    <app-comments-nav [appSlug]="appSlug" />

    <div class="flex flex-wrap items-center justify-between gap-3 mb-4">
      <div class="flex gap-1" role="tablist">
        @for (tab of tabs; track tab.status) {
          <button type="button" role="tab" [attr.aria-selected]="status() === tab.status"
            (click)="setStatus(tab.status)"
            [class]="'px-3 py-1.5 text-xs font-medium border ' + (status() === tab.status
              ? 'border-accent text-text-primary bg-surface' : 'border-border text-text-muted hover:text-text-primary')">
            {{ tab.label }}
            @if (stats(); as s) { <span class="ml-1 text-text-muted">{{ s[tab.status] }}</span> }
          </button>
        }
      </div>
      <form (ngSubmit)="search()" class="flex gap-2">
        <input name="search" [(ngModel)]="searchText" placeholder="Search text, author, email"
          class="bg-background border border-border px-3 py-1.5 text-sm text-text-primary focus:border-accent focus:outline-none" />
        <app-button type="submit" variant="secondary" size="sm">Search</app-button>
      </form>
    </div>

    @if (selected().size > 0) {
      <div class="flex items-center gap-2 mb-3 text-sm" data-testid="bulk-bar">
        <span class="text-text-muted">{{ selected().size }} selected</span>
        @if (status() !== 'approved') { <app-button size="sm" (click)="bulk('approve')">Approve</app-button> }
        @if (status() !== 'rejected') { <app-button size="sm" variant="secondary" (click)="bulk('reject')">Reject</app-button> }
        @if (status() !== 'spam') { <app-button size="sm" variant="secondary" (click)="bulk('spam')">Spam</app-button> }
      </div>
    }

    @if (message()) { <p class="text-xs text-text-muted mb-3">{{ message() }}</p> }

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (comments().length === 0) {
      <app-empty-state [message]="'No ' + status() + ' comments.'" />
    } @else {
      <div class="border border-border">
        <div class="flex items-center gap-2 px-4 py-2 border-b border-border text-xs text-text-muted">
          <input type="checkbox" aria-label="Select all" [checked]="allSelected()" (change)="toggleAll($any($event.target).checked)" />
          <span>{{ total() }} comment(s)</span>
        </div>
        @for (c of comments(); track c.id) {
          <div class="flex gap-3 px-4 py-3 border-b border-border" data-testid="comment-row">
            <input type="checkbox" class="mt-1" [attr.aria-label]="'Select comment by ' + c.author_name"
              [checked]="selected().has(c.id)" (change)="toggle(c.id, $any($event.target).checked)" />
            <div class="flex-1 min-w-0">
              <div class="flex flex-wrap items-center gap-2 text-xs text-text-muted mb-1">
                <span class="text-text-primary font-medium">{{ c.author_name }}</span>
                @if (c.author_email) { <span class="font-mono">{{ c.author_email }}</span> }
                @if (c.app_user_id) { <app-badge variant="info">signed in</app-badge> }
                <span>{{ c.created_at | date: 'medium' }}</span>
                @if (c.edited_at) { <span>(edited)</span> }
                @if (c.parent_id) { <app-badge variant="muted">reply</app-badge> }
                @if (c.is_deleted) { <app-badge variant="muted">deleted by author</app-badge> }
              </div>
              <div class="text-xs text-text-muted mb-2">
                on <span class="font-mono">{{ c.page_title || c.page_key }}</span>
                @if (c.page_title) { <span class="font-mono"> ({{ c.page_key }})</span> }
              </div>
              <p class="text-sm text-text-primary whitespace-pre-wrap break-words" data-testid="comment-body">{{ c.body }}</p>
            </div>
            <div class="flex flex-col items-end gap-1 text-xs shrink-0">
              @if (c.status !== 'approved') {
                <button type="button" class="text-success hover:underline" (click)="moderate(c, 'approve')">Approve</button>
              }
              @if (c.status !== 'rejected') {
                <button type="button" class="text-text-muted hover:text-text-primary" (click)="moderate(c, 'reject')">Reject</button>
              }
              @if (c.status !== 'spam') {
                <button type="button" class="text-warning hover:underline" (click)="moderate(c, 'spam')">Spam</button>
              }
              <button type="button" class="text-error hover:underline" (click)="deleting.set(c)">Delete</button>
            </div>
          </div>
        }
      </div>
      @if (pageCount() > 1) {
        <div class="flex items-center justify-end gap-2 mt-3 text-xs text-text-muted">
          <app-button size="sm" variant="secondary" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</app-button>
          <span>Page {{ page() }} of {{ pageCount() }}</span>
          <app-button size="sm" variant="secondary" [disabled]="page() >= pageCount()" (click)="go(page() + 1)">Next</app-button>
        </div>
      }
    }

    @if (deleting()) {
      <app-confirm-modal title="Delete comment"
        message="Permanently delete this comment and all replies to it? This cannot be undone."
        confirmLabel="Delete" (confirmed)="onDelete()" (cancelled)="deleting.set(null)" />
    }
  `,
})
export class CommentsQueueComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  readonly tabs: { status: CommentStatus; label: string }[] = [
    { status: 'pending', label: 'Pending' },
    { status: 'approved', label: 'Approved' },
    { status: 'rejected', label: 'Rejected' },
    { status: 'spam', label: 'Spam' },
  ];

  status = signal<CommentStatus>('pending');
  comments = signal<AdminComment[]>([]);
  total = signal(0);
  page = signal(1);
  loading = signal(true);
  error = signal<string | null>(null);
  message = signal<string | null>(null);
  stats = signal<CommentStats | null>(null);
  selected = signal<Set<string>>(new Set());
  deleting = signal<AdminComment | null>(null);
  searchText = '';
  private activeSearch = '';

  pageCount = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  allSelected = computed(() =>
    this.comments().length > 0 && this.comments().every(c => this.selected().has(c.id)));

  private get base() { return `/apps/${this.appSlug}/comments`; }

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<PagedResult<AdminComment>>(this.base, {
      status: this.status(), search: this.activeSearch || null, page: this.page(), page_size: PAGE_SIZE,
    }).subscribe({
      next: r => {
        this.comments.set(r.items);
        this.total.set(r.total);
        this.selected.set(new Set());
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err?.status === 403 ? 'You do not have permission to moderate comments.' : 'Failed to load comments.');
        this.loading.set(false);
      },
    });
    this.api.get<CommentStats>(`${this.base}/stats`).subscribe({ next: s => this.stats.set(s), error: () => {} });
  }

  setStatus(status: CommentStatus) {
    this.status.set(status);
    this.page.set(1);
    this.message.set(null);
    this.load();
  }

  search() {
    this.activeSearch = this.searchText.trim();
    this.page.set(1);
    this.load();
  }

  go(page: number) {
    this.page.set(page);
    this.load();
  }

  toggle(id: string, checked: boolean) {
    const next = new Set(this.selected());
    if (checked) next.add(id); else next.delete(id);
    this.selected.set(next);
  }

  toggleAll(checked: boolean) {
    this.selected.set(checked ? new Set(this.comments().map(c => c.id)) : new Set());
  }

  moderate(comment: AdminComment, action: ModerationAction) {
    this.api.post<AdminComment>(`${this.base}/${comment.id}/${action}`).subscribe({
      next: () => this.afterChange(`Comment ${this.pastTense(action)}.`),
      error: () => this.message.set('Moderation failed.'),
    });
  }

  bulk(action: ModerationAction) {
    const ids = [...this.selected()];
    if (ids.length === 0) return;
    this.api.post<BulkModerateResult>(`${this.base}/bulk`, { ids, action }).subscribe({
      next: r => this.afterChange(`${r.updated} comment(s) ${this.pastTense(action)}.`),
      error: () => this.message.set('Bulk moderation failed.'),
    });
  }

  onDelete() {
    const c = this.deleting()!;
    this.deleting.set(null);
    this.api.delete<void>(`${this.base}/${c.id}`).subscribe({
      next: () => this.afterChange('Comment deleted.'),
      error: err => this.message.set(err?.status === 403 ? 'Only owners and admins can delete comments.' : 'Delete failed.'),
    });
  }

  private afterChange(message: string) {
    this.message.set(message);
    this.load();
  }

  private pastTense(action: ModerationAction) {
    return action === 'approve' ? 'approved' : action === 'reject' ? 'rejected' : 'marked as spam';
  }
}
