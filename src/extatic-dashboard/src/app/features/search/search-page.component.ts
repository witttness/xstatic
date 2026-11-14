import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../shared/button/button.component';
import { BadgeComponent } from '../../shared/badge/badge.component';
import { CollectionConfigEditorComponent } from './collection-config-editor.component';
import {
  SearchAnalyticsComponent,
  SearchDocumentsComponent,
  SearchIntegrationComponent,
  SearchSettingsComponent,
  SearchTryComponent,
} from './search-panels.component';
import { ReindexJob, SearchOverview } from './search.models';

type Tab = 'overview' | 'collections' | 'documents' | 'try' | 'analytics' | 'integration' | 'settings';

@Component({
  selector: 'app-search-page',
  standalone: true,
  imports: [
    PageHeaderComponent,
    ButtonComponent,
    BadgeComponent,
    CollectionConfigEditorComponent,
    SearchSettingsComponent,
    SearchDocumentsComponent,
    SearchTryComponent,
    SearchAnalyticsComponent,
    SearchIntegrationComponent,
  ],
  template: `
    <app-page-header title="Search" subtitle="Full-text search over pages pushed from CI and opted-in collections." />

    <nav class="flex flex-wrap gap-1 border-b border-border mb-6" role="tablist">
      @for (t of tabs; track t.id) {
        <button type="button" role="tab" [attr.aria-selected]="tab() === t.id" (click)="tab.set(t.id)"
          [class]="'px-3 py-2 text-sm -mb-px border-b-2 ' + (tab() === t.id ? 'border-accent text-text-primary' : 'border-transparent text-text-muted hover:text-text-primary')">
          {{ t.label }}
        </button>
      }
    </nav>

    @switch (tab()) {
      @case ('overview') {
        @if (error()) { <p role="alert" class="text-sm text-error">{{ error() }}</p> }
        @else if (overview(); as o) {
          <div class="flex items-center gap-2 mb-4">
            @if (o.enabled) { <app-badge variant="success">Enabled</app-badge> } @else { <app-badge variant="muted">Disabled</app-badge> }
            @if (o.enabled && !o.public_search_enabled) { <app-badge variant="warning">Public endpoint off</app-badge> }
            @if (!o.enabled) {
              <span class="text-xs text-text-muted">Turn search on under Settings to start indexing.</span>
            }
          </div>
          <dl class="grid gap-4 sm:grid-cols-4 mb-6">
            @for (stat of stats(o); track stat.label) {
              <div class="border border-border p-4">
                <dt class="text-xs text-text-muted uppercase tracking-wider">{{ stat.label }}</dt>
                <dd class="text-sm text-text-primary mt-1">{{ stat.value }}</dd>
              </div>
            }
          </dl>
          <div class="flex items-center gap-3">
            <app-button variant="secondary" [loading]="reindexing()" (click)="reindex()">Reindex all collections</app-button>
            @if (job(); as j) {
              <span class="text-xs text-text-muted" role="status">
                Last reindex: <app-badge [variant]="j.status === 'completed' ? 'success' : j.status === 'failed' ? 'error' : 'info'">{{ j.status }}</app-badge>
                @if (j.total > 0) { {{ j.processed }} / {{ j.total }} }
              </span>
            }
          </div>
          @if (reindexError()) { <p role="alert" class="text-xs text-error mt-2">{{ reindexError() }}</p> }
        } @else { <p role="status" class="text-sm text-text-muted">Loading search overview…</p> }
      }
      @case ('collections') { <app-collection-config-editor [appSlug]="appSlug" /> }
      @case ('documents') { <app-search-documents [appSlug]="appSlug" /> }
      @case ('try') { <app-search-try [appSlug]="appSlug" /> }
      @case ('analytics') { <app-search-analytics [appSlug]="appSlug" /> }
      @case ('integration') { <app-search-integration [publicId]="overview()?.public_id ?? 'app_…'" /> }
      @case ('settings') { <app-search-settings [appSlug]="appSlug" /> }
    }
  `,
})
export class SearchPageComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);

  readonly tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'collections', label: 'Collections' },
    { id: 'documents', label: 'Documents' },
    { id: 'try', label: 'Try it' },
    { id: 'analytics', label: 'Analytics' },
    { id: 'integration', label: 'Integration' },
    { id: 'settings', label: 'Settings' },
  ];

  tab = signal<Tab>('overview');
  overview = signal<SearchOverview | null>(null);
  error = signal<string | null>(null);
  job = signal<ReindexJob | null>(null);
  reindexing = signal(false);
  reindexError = signal<string | null>(null);

  ngOnInit() { this.load(); }

  stats(o: SearchOverview): { label: string; value: string }[] {
    return [
      { label: 'Documents', value: `${o.total} (max ${o.max_documents} pages)` },
      { label: 'Pages', value: String(o.pages) },
      { label: 'Items', value: `${o.items} from ${o.enabled_collections} collection(s)` },
      { label: 'Last indexed', value: o.last_indexed_at ? new Date(o.last_indexed_at).toLocaleString() : 'Never' },
    ];
  }

  load() {
    this.api.get<SearchOverview>(`/apps/${this.appSlug}/search`).subscribe({
      next: o => { this.overview.set(o); this.job.set(o.latest_job); },
      error: () => this.error.set('Failed to load search overview.'),
    });
  }

  reindex() {
    this.reindexing.set(true);
    this.reindexError.set(null);
    this.api.post<ReindexJob>(`/apps/${this.appSlug}/search/reindex`).subscribe({
      next: j => { this.job.set(j); this.reindexing.set(false); },
      error: (e: HttpErrorResponse) => {
        this.reindexing.set(false);
        this.reindexError.set(e.status === 403 ? 'Only owners and admins can reindex.' : 'Failed to start reindex.');
      },
    });
  }
}
