import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { LibrarySchemaSummary, libraryBase } from '../schema-library.models';

/** Global library (admin/schemas) when no appSlug; an App's library (apps/:appSlug/schemas) otherwise. */
@Component({
  selector: 'app-schema-list',
  standalone: true,
  imports: [FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent, LoadingSpinnerComponent, EmptyStateComponent],
  template: `
    <app-page-header [title]="appSlug ? 'App Schemas' : 'Schema Library'"
      [subtitle]="appSlug ? 'Reusable schemas private to this app.' : 'Global schemas. Shared ones can be picked by every app.'">
      <a [routerLink]="newLink()"><app-button>New Schema</app-button></a>
    </app-page-header>

    <div class="flex flex-wrap items-end gap-3 mb-4">
      <input type="search" [(ngModel)]="q" (keyup.enter)="load()" placeholder="Search name, slug, tag…"
        class="bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none w-64" />
      @if (!appSlug) {
        <select [(ngModel)]="visibility" (change)="load()" aria-label="Visibility"
          class="bg-background border border-border px-3 py-2 text-sm text-text-primary">
          <option value="">All visibilities</option>
          <option value="shared">Shared</option>
          <option value="admin_only">Admin only</option>
        </select>
      }
      <label class="flex items-center gap-2 text-sm text-text-muted">
        <input type="checkbox" [(ngModel)]="showArchived" (change)="load()" /> Show archived
      </label>
      <app-button variant="secondary" size="sm" (click)="load()">Search</app-button>
    </div>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (schemas().length === 0) {
      <app-empty-state message="No schemas yet." description="Create reusable JSON Schemas to pick from when editing collections." />
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Name</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Category / Tags</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Version</th>
            </tr>
          </thead>
          <tbody>
            @for (s of schemas(); track s.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3">
                  <a [routerLink]="editLink(s.id)" class="text-text-primary hover:underline">{{ s.name }}</a>
                  <span class="block font-mono text-xs text-text-muted">{{ s.slug }}</span>
                </td>
                <td class="px-4 py-3">
                  @if (s.category) { <span class="text-xs text-text-muted mr-2">{{ s.category }}</span> }
                  @for (t of s.tags; track t) { <app-badge variant="info">{{ t }}</app-badge> }
                </td>
                <td class="px-4 py-3 space-x-1">
                  @if (s.visibility === 'shared') { <app-badge variant="success">Shared</app-badge> }
                  @if (s.visibility === 'admin_only') { <app-badge variant="warning">Admin only</app-badge> }
                  @if (s.is_archived) { <app-badge variant="muted">Archived</app-badge> }
                </td>
                <td class="px-4 py-3 font-mono text-xs">v{{ s.version }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <p class="text-xs text-text-muted mt-2">{{ total() }} schema(s)</p>
    }
  `,
})
export class SchemaListComponent implements OnInit {
  @Input() appSlug?: string;

  private api = inject(ApiService);

  schemas = signal<LibrarySchemaSummary[]>([]);
  total = signal(0);
  loading = signal(true);
  error = signal<string | null>(null);
  q = '';
  visibility = '';
  showArchived = false;

  ngOnInit() { this.load(); }

  newLink() { return this.appSlug ? ['/apps', this.appSlug, 'schemas', 'new'] : ['/admin/schemas/new']; }
  editLink(id: string) { return this.appSlug ? ['/apps', this.appSlug, 'schemas', id] : ['/admin/schemas', id]; }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<PagedResult<LibrarySchemaSummary>>(libraryBase(this.appSlug), {
      q: this.q.trim() || null,
      visibility: this.appSlug ? null : this.visibility || null,
      archived: this.showArchived ? null : false,
      page: 1,
      page_size: 100,
    }).subscribe({
      next: r => { this.schemas.set(r.items); this.total.set(r.total); this.loading.set(false); },
      error: () => { this.error.set('Failed to load schemas.'); this.loading.set(false); },
    });
  }
}
