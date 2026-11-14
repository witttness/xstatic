import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PagedResult } from '../../../core/models/paged.model';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { AvailableSchema, AvailableSchemaDetail, apiErrorMessage, prettyJson } from '../schema-library.models';

/** What the collection form receives when a library schema is chosen. */
export interface PickedSchema {
  id: string;
  name: string;
  version: number;
  /** Pretty-printed JSON, ready for the schema textarea. */
  schemaText: string;
}

/**
 * Modal listing the schemas an App may use (Shared global + the App's own; platform admins also see
 * admin-only ones), with search and a JSON preview. Selecting copies the content; nothing is linked live.
 */
@Component({
  selector: 'app-schema-picker',
  standalone: true,
  imports: [FormsModule, RouterLink, ButtonComponent, BadgeComponent],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/60" (click)="cancelled.emit()"></div>
      <div class="relative bg-surface border border-border w-full max-w-4xl max-h-[90vh] flex flex-col z-10" role="dialog" aria-label="Choose a schema">
        <div class="flex items-center justify-between gap-3 p-4 border-b border-border">
          <h3 class="text-base font-semibold text-text-primary">Choose from library</h3>
          <a [routerLink]="['/apps', appSlug, 'schemas']" (click)="cancelled.emit()"
            class="text-xs text-text-muted hover:text-text-primary">Manage app schemas</a>
        </div>
        <div class="p-4 border-b border-border flex gap-2">
          <input type="search" [(ngModel)]="q" (keyup.enter)="search()" placeholder="Search schemas…" aria-label="Search schemas"
            class="flex-1 bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          <app-button variant="secondary" size="sm" (click)="search()">Search</app-button>
        </div>
        <div class="grid md:grid-cols-2 min-h-0 flex-1 overflow-hidden">
          <div class="overflow-auto border-r border-border p-2">
            @if (loading()) { <p class="text-sm text-text-muted p-2">Loading…</p> }
            @if (error()) { <p class="text-sm text-error p-2">{{ error() }}</p> }
            @for (group of groups(); track group.label) {
              <h4 class="text-xs font-medium text-text-muted uppercase tracking-wider px-2 pt-3 pb-1">{{ group.label }}</h4>
              @if (group.items.length === 0) { <p class="text-xs text-text-muted px-2">None.</p> }
              @for (s of group.items; track s.id) {
                <button type="button" (click)="preview(s)" [attr.data-schema-id]="s.id"
                  [class]="'w-full text-left px-2 py-2 hover:bg-background ' + (selected()?.id === s.id ? 'bg-background' : '')">
                  <span class="text-sm text-text-primary">{{ s.name }}</span>
                  <span class="text-xs text-text-muted font-mono"> v{{ s.version }}</span>
                  @if (s.admin_only) { <app-badge variant="warning">Admin only</app-badge> }
                  @if (s.description) { <span class="block text-xs text-text-muted">{{ s.description }}</span> }
                </button>
              }
            }
          </div>
          <div class="overflow-auto p-4">
            @if (selected(); as s) {
              <p class="text-sm text-text-primary mb-2">{{ s.name }} <span class="text-xs text-text-muted font-mono">v{{ s.version }}</span></p>
              <pre class="text-xs font-mono bg-background border border-border p-2 overflow-auto max-h-96" data-testid="preview">{{ previewText() }}</pre>
            } @else {
              <p class="text-sm text-text-muted">Select a schema to preview it.</p>
            }
          </div>
        </div>
        <div class="flex justify-between items-center gap-2 p-4 border-t border-border">
          <p class="text-xs text-text-muted">The schema is copied into the collection; later library edits never change it automatically.</p>
          <div class="flex gap-2">
            <app-button variant="secondary" (click)="cancelled.emit()">Cancel</app-button>
            <app-button [disabled]="!selected()" (click)="use()">Use this schema</app-button>
          </div>
        </div>
      </div>
    </div>
  `,
})
export class SchemaPickerComponent implements OnInit {
  @Input({ required: true }) appSlug!: string;
  @Output() picked = new EventEmitter<PickedSchema>();
  @Output() cancelled = new EventEmitter<void>();

  private api = inject(ApiService);

  q = '';
  schemas = signal<AvailableSchema[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  selected = signal<AvailableSchemaDetail | null>(null);
  previewText = computed(() => { const s = this.selected(); return s ? prettyJson(s.schema) : ''; });
  groups = computed(() => [
    { label: 'Shared', items: this.schemas().filter(s => s.scope === 'global') },
    { label: 'This app', items: this.schemas().filter(s => s.scope === 'app') },
  ]);

  ngOnInit() { this.search(); }

  search() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<PagedResult<AvailableSchema>>(`/apps/${this.appSlug}/schemas/available`, { q: this.q.trim() || null }).subscribe({
      next: r => { this.schemas.set(r.items); this.loading.set(false); },
      error: err => { this.error.set(apiErrorMessage(err, 'Failed to load schemas.')); this.loading.set(false); },
    });
  }

  preview(s: AvailableSchema) {
    this.api.get<AvailableSchemaDetail>(`/apps/${this.appSlug}/schemas/available/${s.id}`).subscribe({
      next: d => this.selected.set(d),
      error: err => this.error.set(apiErrorMessage(err, 'Failed to load schema.')),
    });
  }

  use() {
    const s = this.selected();
    if (!s) return;
    this.picked.emit({ id: s.id, name: s.name, version: s.version, schemaText: prettyJson(s.schema) });
  }
}
