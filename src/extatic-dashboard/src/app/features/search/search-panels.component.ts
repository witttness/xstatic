import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import { PagedResult } from '../../core/models/paged.model';
import { ButtonComponent } from '../../shared/button/button.component';
import { BadgeComponent } from '../../shared/badge/badge.component';
import { CopyButtonComponent } from '../../shared/copy-button/copy-button.component';
import { ConfirmModalComponent } from '../../shared/confirm-modal/confirm-modal.component';
import { SearchSnippetComponent } from './search-snippet.component';
import {
  INPUT_CLASS,
  LABEL_CLASS,
  SearchAnalytics,
  SearchDocumentSummary,
  SearchResponse,
  SearchSettings,
} from './search.models';

/** Settings form (owner/admin). */
@Component({
  selector: 'app-search-settings',
  standalone: true,
  imports: [FormsModule, ButtonComponent],
  template: `
    @if (loadError()) {
      <p role="alert" class="text-sm text-error">{{ loadError() }}</p>
    } @else if (settings(); as s) {
      <form (ngSubmit)="save()" class="max-w-lg space-y-4" aria-label="Search settings">
        <label class="flex items-center gap-2 cursor-pointer">
          <input type="checkbox" name="enabled" [(ngModel)]="s.enabled" class="w-4 h-4 accent-accent" />
          <span class="text-sm text-text-primary">Search enabled (indexing and queries)</span>
        </label>
        <label class="flex items-center gap-2 cursor-pointer">
          <input type="checkbox" name="public" [(ngModel)]="s.public_search_enabled" class="w-4 h-4 accent-accent" />
          <span class="text-sm text-text-primary">Public search endpoint for the site</span>
        </label>
        <div>
          <label [class]="labelClass" for="default_language">Default language</label>
          <select id="default_language" name="default_language" [(ngModel)]="s.default_language" [class]="inputClass">
            @for (l of s.available_languages; track l) { <option [value]="l">{{ l }}</option> }
          </select>
        </div>
        <div>
          <label [class]="labelClass" for="max_documents">Max page documents</label>
          <input id="max_documents" name="max_documents" type="number" min="1" [(ngModel)]="s.max_documents" [class]="inputClass" />
        </div>
        @if (error()) { <p role="alert" class="text-xs text-error">{{ error() }}</p> }
        @if (saved()) { <p role="status" class="text-xs text-success">Saved.</p> }
        <app-button type="submit" [loading]="saving()">Save settings</app-button>
      </form>
    } @else {
      <p role="status" class="text-sm text-text-muted">Loading settings…</p>
    }
  `,
})
export class SearchSettingsComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  readonly inputClass = INPUT_CLASS;
  readonly labelClass = LABEL_CLASS;
  settings = signal<SearchSettings | null>(null);
  loadError = signal<string | null>(null);
  error = signal<string | null>(null);
  saving = signal(false);
  saved = signal(false);

  ngOnInit() {
    this.api.get<SearchSettings>(`/apps/${this.appSlug}/search/settings`).subscribe({
      next: s => this.settings.set({ ...s }),
      error: (e: HttpErrorResponse) => this.loadError.set(e.status === 403
        ? 'Only owners and admins can change search settings.' : 'Failed to load settings.'),
    });
  }

  save() {
    const s = this.settings();
    if (!s) return;
    this.saving.set(true); this.error.set(null); this.saved.set(false);
    this.api.put<SearchSettings>(`/apps/${this.appSlug}/search/settings`, {
      enabled: s.enabled,
      public_search_enabled: s.public_search_enabled,
      default_language: s.default_language,
      max_documents: Number(s.max_documents),
    }).subscribe({
      next: updated => { this.settings.set({ ...updated }); this.saving.set(false); this.saved.set(true); },
      error: (e: HttpErrorResponse) => {
        this.saving.set(false);
        const errors = e.error?.errors as { message: string }[] | undefined;
        this.error.set(errors?.map(x => x.message).join(' ') || 'Failed to save settings.');
      },
    });
  }
}

/** Paged document browser with delete. */
@Component({
  selector: 'app-search-documents',
  standalone: true,
  imports: [FormsModule, DatePipe, BadgeComponent, ConfirmModalComponent],
  template: `
    <form (ngSubmit)="go(1)" class="flex flex-wrap gap-2 mb-4" aria-label="Filter documents">
      <input name="q" [(ngModel)]="text" [class]="inputClass + ' max-w-xs'" placeholder="Filter by title, id or URL" />
      <select name="source" [(ngModel)]="source" (ngModelChange)="go(1)" [class]="inputClass + ' max-w-[10rem]'">
        <option value="">All sources</option>
        <option value="page">Pages</option>
        <option value="item">Items</option>
      </select>
      <input name="section" [(ngModel)]="section" [class]="inputClass + ' max-w-[10rem]'" placeholder="Section" />
      <button type="submit" class="text-sm text-accent">Apply</button>
    </form>
    @if (error()) { <p role="alert" class="text-sm text-error">{{ error() }}</p> }
    @else if (!result()) { <p role="status" class="text-sm text-text-muted">Loading documents…</p> }
    @else if (result()!.items.length === 0) { <p class="text-sm text-text-muted">No documents match.</p> }
    @else {
      <p class="text-xs text-text-muted mb-2">{{ result()!.total }} documents · Page {{ page() }} of {{ pageCount() }}</p>
      <div class="border border-border overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border text-left text-xs text-text-muted uppercase tracking-wider">
              <th class="px-3 py-2">Title</th><th class="px-3 py-2">Source</th><th class="px-3 py-2">Section</th>
              <th class="px-3 py-2">Indexed</th><th class="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            @for (d of result()!.items; track d.id) {
              <tr class="border-b border-border">
                <td class="px-3 py-2">
                  <span class="block text-text-primary">{{ d.title || '(untitled)' }}</span>
                  <span class="block font-mono text-xs text-text-muted break-all">{{ d.url || d.external_id }}</span>
                </td>
                <td class="px-3 py-2"><app-badge [variant]="d.source === 'page' ? 'info' : 'muted'">{{ d.source }}{{ d.collection ? ' · ' + d.collection : '' }}</app-badge></td>
                <td class="px-3 py-2 text-xs">{{ d.section }}</td>
                <td class="px-3 py-2 text-xs text-text-muted">{{ d.indexed_at | date: 'short' }}</td>
                <td class="px-3 py-2 text-right"><button type="button" class="text-xs text-error" (click)="deleting.set(d)">Delete</button></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <div class="flex gap-3 items-center mt-3">
        <button type="button" (click)="go(page() - 1)" [disabled]="page() <= 1" class="text-sm text-accent disabled:opacity-40">Previous</button>
        <button type="button" (click)="go(page() + 1)" [disabled]="page() >= pageCount()" class="text-sm text-accent disabled:opacity-40">Next</button>
      </div>
    }
    @if (deleting()) {
      <app-confirm-modal title="Delete document" confirmLabel="Delete"
        [message]="'Remove “' + (deleting()!.title || deleting()!.external_id) + '” from the index? Pages come back on the next CI push; Items on their next update.'"
        (confirmed)="remove()" (cancelled)="deleting.set(null)" />
    }
  `,
})
export class SearchDocumentsComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  readonly inputClass = INPUT_CLASS;
  text = '';
  source = '';
  section = '';
  page = signal(1);
  result = signal<PagedResult<SearchDocumentSummary> | null>(null);
  error = signal<string | null>(null);
  deleting = signal<SearchDocumentSummary | null>(null);
  pageCount = computed(() => Math.max(1, Math.ceil((this.result()?.total ?? 0) / 20)));

  ngOnInit() { this.go(1); }

  go(page: number) {
    this.page.set(page);
    this.error.set(null);
    this.api.get<PagedResult<SearchDocumentSummary>>(`/apps/${this.appSlug}/search/documents`, {
      page, page_size: 20, q: this.text || null, source: this.source || null, section: this.section || null,
    }).subscribe({
      next: r => this.result.set(r),
      error: () => this.error.set('Failed to load documents.'),
    });
  }

  remove() {
    const d = this.deleting();
    this.deleting.set(null);
    if (!d) return;
    this.api.delete<void>(`/apps/${this.appSlug}/search/documents/${d.id}`).subscribe({
      next: () => this.go(this.page()),
      error: () => this.error.set('Failed to delete document (editors and above only).'),
    });
  }
}

/** "Try it" box: runs the query through the dashboard test endpoint and renders snippets safely. */
@Component({
  selector: 'app-search-try',
  standalone: true,
  imports: [FormsModule, DecimalPipe, ButtonComponent, BadgeComponent, SearchSnippetComponent],
  template: `
    <form (ngSubmit)="run()" class="flex gap-2 mb-4 max-w-xl" aria-label="Try a search">
      <input name="q" [(ngModel)]="q" [class]="inputClass" placeholder='e.g. getting started -beta or "exact phrase"' maxlength="200" />
      <app-button type="submit" [loading]="loading()">Search</app-button>
    </form>
    @if (error()) { <p role="alert" class="text-sm text-error">{{ error() }}</p> }
    @if (response(); as r) {
      <p class="text-xs text-text-muted mb-3">{{ r.total }} result{{ r.total === 1 ? '' : 's' }}</p>
      <ol class="space-y-4">
        @for (hit of r.items; track hit.source + hit.id) {
          <li class="border-b border-border pb-3">
            <div class="flex items-center gap-2">
              <span class="text-sm font-medium text-text-primary">{{ hit.title || '(untitled)' }}</span>
              <app-badge [variant]="hit.source === 'page' ? 'info' : 'muted'">{{ hit.source }}</app-badge>
              @if (hit.section) { <app-badge>{{ hit.section }}</app-badge> }
              <span class="text-xs text-text-muted ml-auto">{{ hit.score | number: '1.3-3' }}</span>
            </div>
            @if (hit.url) { <p class="font-mono text-xs text-accent break-all">{{ hit.url }}</p> }
            <p class="text-sm text-text-muted mt-1"><app-search-snippet [snippet]="hit.snippet" /></p>
          </li>
        }
      </ol>
    }
  `,
})
export class SearchTryComponent {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  readonly inputClass = INPUT_CLASS;
  q = '';
  loading = signal(false);
  error = signal<string | null>(null);
  response = signal<SearchResponse | null>(null);

  run() {
    if (!this.q.trim()) return;
    this.loading.set(true); this.error.set(null);
    this.api.get<SearchResponse>(`/apps/${this.appSlug}/search/test`, { q: this.q, page_size: 20 }).subscribe({
      next: r => { this.response.set(r); this.loading.set(false); },
      error: (e: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(e.error?.detail || 'Search failed.');
      },
    });
  }
}

/** Top and zero-result queries for the last 30 days. */
@Component({
  selector: 'app-search-analytics',
  standalone: true,
  imports: [DecimalPipe],
  template: `
    @if (error()) { <p role="alert" class="text-sm text-error">{{ error() }}</p> }
    @else if (analytics(); as a) {
      <p class="text-xs text-text-muted mb-4">{{ a.total_queries }} public searches in the last 30 days.</p>
      <div class="grid gap-6 md:grid-cols-2">
        @for (block of [{ title: 'Top queries', rows: a.top_queries }, { title: 'Queries with no results', rows: a.zero_result_queries }]; track block.title) {
          <section>
            <h3 class="text-sm font-medium text-text-primary mb-2">{{ block.title }}</h3>
            @if (block.rows.length === 0) { <p class="text-xs text-text-muted">Nothing yet.</p> }
            @else {
              <table class="w-full text-sm border border-border">
                <tbody>
                  @for (row of block.rows; track row.query) {
                    <tr class="border-b border-border">
                      <td class="px-3 py-1.5 font-mono text-xs break-all">{{ row.query }}</td>
                      <td class="px-3 py-1.5 text-right text-xs">{{ row.count }}×</td>
                      <td class="px-3 py-1.5 text-right text-xs text-text-muted">{{ row.average_results | number: '1.0-1' }} avg</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        }
      </div>
    } @else { <p role="status" class="text-sm text-text-muted">Loading analytics…</p> }
  `,
})
export class SearchAnalyticsComponent implements OnInit {
  @Input() appSlug!: string;
  private api = inject(ApiService);
  analytics = signal<SearchAnalytics | null>(null);
  error = signal<string | null>(null);

  ngOnInit() {
    this.api.get<SearchAnalytics>(`/apps/${this.appSlug}/search/analytics`).subscribe({
      next: a => this.analytics.set(a),
      error: () => this.error.set('Failed to load analytics.'),
    });
  }
}

/** CI and site integration snippets. */
@Component({
  selector: 'app-search-integration',
  standalone: true,
  imports: [CopyButtonComponent],
  template: `
    <div class="space-y-6 max-w-3xl">
      <section>
        <div class="flex items-center justify-between mb-1">
          <h3 class="text-sm font-medium text-text-primary">1. Push pages from CI</h3>
          <app-copy-button [value]="curlPush()" />
        </div>
        <p class="text-xs text-text-muted mb-2">
          Use the app's secret key (never ship it to the browser). Up to 500 documents per request; HTML in
          <code>body</code> is stripped to text. Unchanged documents are skipped.
        </p>
        <pre class="bg-surface border border-border p-3 text-xs font-mono overflow-x-auto">{{ curlPush() }}</pre>
      </section>
      <section>
        <div class="flex items-center justify-between mb-1">
          <h3 class="text-sm font-medium text-text-primary">2. Prune deleted pages</h3>
          <app-copy-button [value]="curlSync()" />
        </div>
        <pre class="bg-surface border border-border p-3 text-xs font-mono overflow-x-auto">{{ curlSync() }}</pre>
      </section>
      <section>
        <div class="flex items-center justify-between mb-1">
          <h3 class="text-sm font-medium text-text-primary">3. Query from the site</h3>
          <app-copy-button [value]="fetchSnippet()" />
        </div>
        <p class="text-xs text-text-muted mb-2">
          Snippets are escaped text with <code>&lt;mark&gt;</code> highlights. Render them as text nodes as shown,
          not with <code>innerHTML</code>.
        </p>
        <pre class="bg-surface border border-border p-3 text-xs font-mono overflow-x-auto">{{ fetchSnippet() }}</pre>
      </section>
    </div>
  `,
})
export class SearchIntegrationComponent {
  @Input() set publicId(value: string) { this.id.set(value); }
  private id = signal('app_…');
  private base = typeof window !== 'undefined' ? window.location.origin : 'https://your-extatic-host';

  curlPush = computed(() => `curl -X PUT "${this.base}/api/${this.id()}/search/documents" \\
  -H "X-Extatic-Secret-Key: $EXTATIC_SECRET_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{"documents":[{"id":"/blog/hello","url":"/blog/hello","title":"Hello world","body":"<p>Rendered HTML or text</p>","summary":"Optional","section":"blog","tags":["intro"],"published_at":"2026-01-01T00:00:00Z"}]}'`);

  curlSync = computed(() => `curl -X POST "${this.base}/api/${this.id()}/search/sync" \\
  -H "X-Extatic-Secret-Key: $EXTATIC_SECRET_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{"external_ids":["/blog/hello","/about"]}'`);

  fetchSnippet = computed(() => `const API = '${this.base}/api/${this.id()}/search';

async function search(q) {
  const res = await fetch(API + '?q=' + encodeURIComponent(q) + '&page_size=10');
  if (!res.ok) return [];
  return (await res.json()).items;
}

// Safe snippet rendering: split on <mark> tags, decode entities, append text nodes.
function renderSnippet(el, snippet) {
  const decode = s => new DOMParser().parseFromString(s, 'text/html').documentElement.textContent;
  let mark = false;
  for (const part of snippet.split(/(<\\/?mark>)/)) {
    if (part === '<mark>') { mark = true; continue; }
    if (part === '</mark>') { mark = false; continue; }
    const text = document.createTextNode(decode(part));
    if (mark) { const m = document.createElement('mark'); m.append(text); el.append(m); }
    else el.append(text);
  }
}`);
}
