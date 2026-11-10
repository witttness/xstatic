import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { App } from '../../../core/models/app.model';
import { Collection } from '../../../core/models/collection.model';
import { Item, PagedItems } from '../../../core/models/item.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-items-list',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent],
  template: `
    <app-page-header [title]="collection()?.name ? collection()!.name + ' Items' : 'Items'">
      @if (app()?.can_manage_items) { <a [routerLink]="['/apps', appSlug, 'collections', collectionSlug, 'items', 'new']" class="bg-accent px-3 py-2 text-sm text-background">New Item</a> }
    </app-page-header>
    <a [routerLink]="['/apps', appSlug, 'collections']" class="text-xs text-accent">← Collections</a>
    @if (loading()) { <p role="status" class="mt-5 text-sm">Loading Items…</p> }
    @else if (error()) { <p role="alert" class="mt-5 text-sm text-error">{{ error() }} <button type="button" (click)="load()" class="text-accent">Retry</button></p> }
    @else if (!result()?.items?.length) { <p class="mt-5 text-sm text-text-muted">No Items in this Collection yet.</p> }
    @else {
      <p class="mt-4 text-xs text-text-muted">{{ result()!.total }} Items · Page {{ page() }} of {{ pageCount() }}</p>
      <div class="mt-3 border border-border divide-y divide-border">
        @for (item of result()!.items; track item.id) {
          <div class="p-4 flex items-start justify-between gap-4">
            <div class="min-w-0">
              <a [routerLink]="['/apps', appSlug, 'collections', collectionSlug, 'items', item.id]" class="font-mono text-xs text-accent break-all">{{ item.id }}</a>
              <p class="font-mono text-xs text-text-muted break-words mt-2">{{ preview(item) }}</p>
              <p class="text-xs text-text-muted mt-2">Created {{ date(item.created_at) }} · Updated {{ date(item.updated_at) }}</p>
            </div>
            @if (app()?.can_manage_items) { <a [routerLink]="['/apps', appSlug, 'collections', collectionSlug, 'items', item.id]" class="text-xs text-accent">Edit</a> }
          </div>
        }
      </div>
      <div class="flex gap-3 items-center mt-4">
        <button type="button" (click)="go(page() - 1)" [disabled]="page() <= 1" class="text-sm text-accent disabled:opacity-40">Previous</button>
        <span class="text-xs">{{ page() }} / {{ pageCount() }}</span>
        <button type="button" (click)="go(page() + 1)" [disabled]="page() >= pageCount()" class="text-sm text-accent disabled:opacity-40">Next</button>
      </div>
    }
  `,
})
export class ItemsListComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() collectionSlug!: string;
  private api = inject(ApiService);
  app = signal<App | null>(null);
  collection = signal<Collection | null>(null);
  result = signal<PagedItems | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);
  page = signal(1);
  pageCount() { return Math.max(1, Math.ceil((this.result()?.total ?? 0) / 20)); }
  ngOnInit() {
    this.api.get<App>(`/apps/${this.appSlug}`).subscribe({ next: app => this.app.set(app) });
    this.api.get<Collection>(`/apps/${this.appSlug}/collections/${this.collectionSlug}`).subscribe({ next: collection => this.collection.set(collection) });
    this.load();
  }
  load() {
    this.loading.set(true); this.error.set(null);
    this.api.get<PagedItems>(`/apps/${this.appSlug}/collections/${this.collectionSlug}/items?page=${this.page()}&page_size=20`).subscribe({
      next: result => { this.result.set(result); this.loading.set(false); },
      error: () => { this.error.set('Unable to load Items.'); this.loading.set(false); },
    });
  }
  go(page: number) { if (page < 1 || page > this.pageCount()) return; this.page.set(page); this.load(); }
  preview(item: Item) { const value = JSON.stringify(item.data); return value.length > 180 ? value.slice(0, 177) + '…' : value; }
  date(value: string) { return new Date(value).toLocaleString(); }
}
