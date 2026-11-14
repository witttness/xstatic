import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { CommentsNavComponent } from '../comments-nav.component';
import { CommentPage } from '../comments.model';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-comments-pages',
  standalone: true,
  imports: [
    DatePipe, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent,
    EmptyStateComponent, CommentsNavComponent,
  ],
  template: `
    <app-page-header title="Comments" subtitle="Pages that have comments." />
    <app-comments-nav [appSlug]="appSlug" />

    @if (message()) { <p class="text-xs text-text-muted mb-3">{{ message() }}</p> }

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (pages().length === 0) {
      <app-empty-state message="No pages have comments yet." />
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Page</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Approved</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Pending</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Last comment</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (p of pages(); track p.page_key) {
              <tr class="border-b border-border">
                <td class="px-4 py-3">
                  <div class="text-text-primary">{{ p.page_title || p.page_key }}</div>
                  <div class="font-mono text-xs text-text-muted">{{ p.page_key }}</div>
                </td>
                <td class="px-4 py-3">{{ p.approved }}</td>
                <td class="px-4 py-3">{{ p.pending }}</td>
                <td class="px-4 py-3 text-xs text-text-muted">{{ p.last_comment_at | date: 'medium' }}</td>
                <td class="px-4 py-3 text-right">
                  @if (p.is_closed) {
                    <app-badge variant="muted">Closed</app-badge>
                    <app-button size="sm" variant="secondary" (click)="setClosed(p, false)">Reopen</app-button>
                  } @else {
                    <app-button size="sm" variant="secondary" (click)="setClosed(p, true)">Close</app-button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (pageCount() > 1) {
        <div class="flex items-center justify-end gap-2 mt-3 text-xs text-text-muted">
          <app-button size="sm" variant="secondary" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</app-button>
          <span>Page {{ page() }} of {{ pageCount() }}</span>
          <app-button size="sm" variant="secondary" [disabled]="page() >= pageCount()" (click)="go(page() + 1)">Next</app-button>
        </div>
      }
    }
  `,
})
export class CommentsPagesComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  pages = signal<CommentPage[]>([]);
  total = signal(0);
  page = signal(1);
  loading = signal(true);
  error = signal<string | null>(null);
  message = signal<string | null>(null);
  pageCount = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  private get base() { return `/apps/${this.appSlug}/comments/pages`; }

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.api.get<PagedResult<CommentPage>>(this.base, { page: this.page(), page_size: PAGE_SIZE }).subscribe({
      next: r => { this.pages.set(r.items); this.total.set(r.total); this.loading.set(false); },
      error: () => { this.error.set('Failed to load pages.'); this.loading.set(false); },
    });
  }

  go(page: number) { this.page.set(page); this.load(); }

  setClosed(p: CommentPage, closed: boolean) {
    this.api.post(`${this.base}/${closed ? 'close' : 'reopen'}`, { page_key: p.page_key }).subscribe({
      next: () => {
        this.pages.update(list => list.map(x => x.page_key === p.page_key ? { ...x, is_closed: closed } : x));
        this.message.set(closed ? 'Page closed for new comments.' : 'Page reopened.');
      },
      error: err => this.message.set(err?.status === 403 ? 'You do not have permission to change this.' : 'Update failed.'),
    });
  }
}
