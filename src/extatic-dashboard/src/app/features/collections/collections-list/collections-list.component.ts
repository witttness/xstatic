import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { Collection } from '../../../core/models/collection.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';

@Component({
  selector: 'app-collections-list',
  standalone: true,
  imports: [
    RouterLink,
    PageHeaderComponent,
    ButtonComponent,
    BadgeComponent,
    LoadingSpinnerComponent,
    EmptyStateComponent,
    ConfirmModalComponent,
  ],
  template: `
    <app-page-header title="Collections">
      <a [routerLink]="['/apps', appSlug, 'collections', 'new']">
        <app-button>New Collection</app-button>
      </a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (collections().length === 0) {
      <app-empty-state message="No collections yet." description="Define a collection to start storing structured data.">
        <a [routerLink]="['/apps', appSlug, 'collections', 'new']">
          <app-button>New Collection</app-button>
        </a>
      </app-empty-state>
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Name</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Schema</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Attachments</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Created</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (col of collections(); track col.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3">
                  <div class="text-text-primary font-medium">{{ col.name }}</div>
                  <div class="font-mono text-xs text-text-muted">{{ col.slug }}</div>
                </td>
                <td class="px-4 py-3">
                  @if (col.schema) {
                    <app-badge variant="info">Has schema</app-badge>
                  } @else {
                    <app-badge variant="muted">None</app-badge>
                  }
                </td>
                <td class="px-4 py-3">
                  @if (col.attachments_enabled) {
                    <app-badge variant="success">Enabled</app-badge>
                  } @else {
                    <app-badge variant="muted">Disabled</app-badge>
                  }
                </td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ formatDate(col.created_at) }}</td>
                <td class="px-4 py-3 text-right flex items-center justify-end gap-3">
                  <a [routerLink]="['/apps', appSlug, 'collections', col.slug]"
                    class="text-xs text-text-muted hover:text-text-primary">Edit</a>
                  <button type="button" (click)="confirmDelete(col)"
                    class="text-xs text-error hover:text-red-400">Delete</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (deletingCollection()) {
      <app-confirm-modal
        title="Delete Collection"
        [message]="'Delete collection ' + deletingCollection()!.name + '? All items will be lost.'"
        [confirmWord]="deletingCollection()!.slug"
        confirmLabel="Delete"
        (confirmed)="onDelete()"
        (cancelled)="deletingCollection.set(null)"
      />
    }
  `,
})
export class CollectionsListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  collections = signal<Collection[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  deletingCollection = signal<Collection | null>(null);

  ngOnInit() {
    this.load();
  }

  load() {
    this.api.get<Collection[]>(`/apps/${this.appSlug}/collections`).subscribe({
      next: cols => { this.collections.set(cols); this.loading.set(false); },
      error: () => { this.error.set('Failed to load collections.'); this.loading.set(false); },
    });
  }

  confirmDelete(col: Collection) {
    this.deletingCollection.set(col);
  }

  onDelete() {
    const col = this.deletingCollection()!;
    this.deletingCollection.set(null);
    this.api.delete<void>(`/apps/${this.appSlug}/collections/${col.slug}`).subscribe({
      next: () => this.collections.update(cs => cs.filter(c => c.id !== col.id)),
      error: () => {},
    });
  }

  formatDate(d: string) {
    return new Date(d).toLocaleDateString();
  }
}
