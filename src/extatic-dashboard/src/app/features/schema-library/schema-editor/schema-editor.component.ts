import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, of, switchMap } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { DiffLine, LineDiffComponent, lineDiff } from '../line-diff';
import {
  LibrarySchema, LibrarySchemaVersion, SchemaUsage, ValidateSchemaResponse,
  apiErrorMessage, libraryBase, prettyJson,
} from '../schema-library.models';

const INPUT = 'w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';

/** Create/edit a library schema. Global library when no appSlug, an App's library otherwise. */
@Component({
  selector: 'app-schema-editor',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PageHeaderComponent, ButtonComponent, BadgeComponent,
    LoadingSpinnerComponent, ConfirmModalComponent, LineDiffComponent],
  template: `
    <app-page-header [title]="isEdit ? (current()?.name ?? 'Schema') : 'New Schema'"
      [subtitle]="appSlug ? 'App schema library' : 'Global schema library'">
      <a [routerLink]="listLink()" class="text-xs text-text-muted hover:text-text-primary">Back to list</a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else {
      <div class="grid gap-8 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <form [formGroup]="form" (ngSubmit)="save()" class="space-y-4 min-w-0">
          <div class="grid gap-4 sm:grid-cols-2">
            <div>
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-name">Name</label>
              <input id="sl-name" formControlName="name" type="text" class="${INPUT}" (input)="autoSlug()" />
            </div>
            <div>
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-slug">Slug</label>
              <input id="sl-slug" formControlName="slug" type="text" class="${INPUT} font-mono" />
            </div>
            <div>
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-category">Category</label>
              <input id="sl-category" formControlName="category" type="text" class="${INPUT}" />
            </div>
            <div>
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-tags">Tags (comma-separated)</label>
              <input id="sl-tags" formControlName="tags" type="text" class="${INPUT}" />
            </div>
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-desc">Description</label>
            <textarea id="sl-desc" formControlName="description" rows="2" class="${INPUT}"></textarea>
          </div>
          @if (!appSlug) {
            <div>
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-vis">Visibility</label>
              <select id="sl-vis" formControlName="visibility" class="${INPUT}">
                <option value="admin_only">Admin only (draft / internal)</option>
                <option value="shared">Shared with every app</option>
              </select>
            </div>
          }
          <div>
            <div class="flex items-center justify-between mb-1">
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider" for="sl-schema">JSON Schema (draft 2020-12)</label>
              <button type="button" (click)="validate()" class="text-xs text-accent hover:underline">Validate</button>
            </div>
            <textarea id="sl-schema" formControlName="schema" rows="16" class="${INPUT} font-mono resize-y"></textarea>
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-sample">Sample data (optional, tested by Validate)</label>
            <textarea id="sl-sample" formControlName="sample" rows="4" class="${INPUT} font-mono resize-y" placeholder='{ "title": "Hello" }'></textarea>
          </div>

          @if (jsonError()) { <p class="text-xs text-error" data-testid="json-error">{{ jsonError() }}</p> }
          @if (validation(); as v) {
            <div class="text-xs space-y-1" data-testid="validation">
              @if (v.valid) { <p class="text-success">Schema is valid{{ v.sample_errors ? ' and the sample passes' : '' }}.</p> }
              @for (e of v.schema_errors; track $index) { <p class="text-error">Schema {{ e.pointer }}: {{ e.message }}</p> }
              @for (e of v.sample_errors ?? []; track $index) { <p class="text-error">Sample {{ e.pointer || '/' }}: {{ e.message }}</p> }
            </div>
          }

          @if (isEdit) {
            <div>
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="sl-note">Change note (stored when the content changes)</label>
              <input id="sl-note" formControlName="change_note" type="text" class="${INPUT}" />
            </div>
          }

          @if (error()) { <p class="text-xs text-error" data-testid="save-error">{{ error() }}</p> }
          <div class="flex flex-wrap gap-3 pt-2">
            <app-button type="submit" [loading]="saving()">{{ isEdit ? 'Save' : 'Create Schema' }}</app-button>
            @if (current(); as s) {
              <app-button variant="secondary" (click)="toggleArchive()">{{ s.is_archived ? 'Unarchive' : 'Archive' }}</app-button>
              <app-button variant="danger" (click)="confirmingDelete.set(true)">Delete</app-button>
            }
          </div>
        </form>

        @if (current(); as s) {
          <aside class="space-y-6 text-sm">
            <section>
              <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Status</h3>
              <div class="space-x-1">
                <app-badge variant="info">v{{ s.version }}</app-badge>
                @if (s.visibility === 'shared') { <app-badge variant="success">Shared</app-badge> }
                @if (s.visibility === 'admin_only') { <app-badge variant="warning">Admin only</app-badge> }
                @if (s.is_archived) { <app-badge variant="muted">Archived</app-badge> }
              </div>
            </section>
            <section>
              <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Version history</h3>
              <ul class="space-y-1">
                @for (v of s.versions; track v.version) {
                  <li class="flex items-start justify-between gap-2">
                    <span><span class="font-mono">v{{ v.version }}</span>
                      <span class="text-xs text-text-muted"> {{ v.created_at.slice(0, 10) }}</span>
                      @if (v.change_note) { <span class="block text-xs text-text-muted">{{ v.change_note }}</span> }
                    </span>
                    @if (v.version !== s.version) {
                      <button type="button" class="text-xs text-accent hover:underline" (click)="compare(v.version)">Diff with v{{ s.version }}</button>
                    }
                  </li>
                }
              </ul>
            </section>
            <section>
              <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Used by</h3>
              @if (usage().length === 0) { <p class="text-xs text-text-muted">No collections reference this schema.</p> }
              <ul class="space-y-1">
                @for (u of usage(); track u.collection_id) {
                  <li class="text-xs"><span class="font-mono">{{ u.app_slug }}/{{ u.collection_slug }}</span>
                    <span class="text-text-muted"> (v{{ u.source_schema_version ?? '?' }})</span></li>
                }
              </ul>
            </section>
          </aside>
        }
      </div>

      @if (diff(); as d) {
        <section class="mt-6">
          <div class="flex items-center justify-between mb-1">
            <h3 class="text-xs font-medium text-text-muted uppercase tracking-wider">{{ diffLabel() }}</h3>
            <button type="button" class="text-xs text-text-muted hover:text-text-primary" (click)="diff.set(null)">Close</button>
          </div>
          <app-line-diff [lines]="d" />
        </section>
      }
    }

    @if (confirmingDelete()) {
      <app-confirm-modal title="Delete Schema"
        message="Delete this schema and its version history? Collections keep their copied schema but lose the link to it."
        confirmLabel="Delete" (confirmed)="remove()" (cancelled)="confirmingDelete.set(false)" />
    }
  `,
})
export class SchemaEditorComponent implements OnInit {
  @Input() appSlug?: string;
  @Input() id?: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  get isEdit() { return !!this.id; }
  private get base() { return libraryBase(this.appSlug); }

  form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    slug: ['', Validators.required],
    description: [''],
    category: [''],
    tags: [''],
    visibility: ['admin_only'],
    schema: ['{\n  "type": "object",\n  "properties": {}\n}'],
    sample: [''],
    change_note: [''],
  });

  loading = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);
  jsonError = signal<string | null>(null);
  validation = signal<ValidateSchemaResponse | null>(null);
  current = signal<LibrarySchema | null>(null);
  usage = signal<SchemaUsage[]>([]);
  diff = signal<DiffLine[] | null>(null);
  diffLabel = signal('');
  confirmingDelete = signal(false);

  ngOnInit() {
    if (this.isEdit) this.reload();
  }

  listLink() { return this.appSlug ? ['/apps', this.appSlug, 'schemas'] : ['/admin/schemas']; }

  reload() {
    this.loading.set(true);
    this.api.get<LibrarySchema>(`${this.base}/${this.id}`).subscribe({
      next: s => {
        this.current.set(s);
        this.form.patchValue({
          name: s.name, slug: s.slug, description: s.description ?? '', category: s.category ?? '',
          tags: s.tags.join(', '), visibility: s.visibility ?? 'admin_only', schema: prettyJson(s.schema), change_note: '',
        });
        this.loading.set(false);
      },
      error: err => { this.error.set(apiErrorMessage(err, 'Failed to load schema.')); this.loading.set(false); },
    });
    this.api.get<SchemaUsage[]>(`${this.base}/${this.id}/usage`).subscribe({ next: u => this.usage.set(u), error: () => {} });
  }

  autoSlug() {
    if (this.isEdit) return;
    const slug = this.form.controls.name.value.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, '');
    this.form.controls.slug.setValue(slug);
  }

  /** Parses the schema (and sample) textareas; sets jsonError and returns null when either is not JSON. */
  parseInputs(): { schema: unknown; sample?: unknown } | null {
    this.jsonError.set(null);
    let schema: unknown;
    try { schema = JSON.parse(this.form.controls.schema.value); } catch { this.jsonError.set('Schema is not valid JSON.'); return null; }
    if (!schema || typeof schema !== 'object' || Array.isArray(schema)) { this.jsonError.set('Schema must be a JSON object.'); return null; }
    const sampleText = this.form.controls.sample.value.trim();
    if (!sampleText) return { schema };
    try { return { schema, sample: JSON.parse(sampleText) }; } catch { this.jsonError.set('Sample data is not valid JSON.'); return null; }
  }

  validate() {
    this.validation.set(null);
    const parsed = this.parseInputs();
    if (!parsed) return;
    this.api.post<ValidateSchemaResponse>(`${this.base}/validate`, parsed).subscribe({
      next: r => this.validation.set(r),
      error: err => this.jsonError.set(apiErrorMessage(err, 'Validation request failed.')),
    });
  }

  save() {
    if (this.form.invalid) { this.form.markAllAsTouched(); this.error.set('Name and slug are required.'); return; }
    const parsed = this.parseInputs();
    if (!parsed) return;
    const v = this.form.getRawValue();
    const tags = v.tags.split(',').map(t => t.trim()).filter(Boolean);
    this.saving.set(true);
    this.error.set(null);

    let req$: Observable<LibrarySchema>;
    if (this.isEdit) {
      const before = this.current();
      req$ = this.api.put<LibrarySchema>(`${this.base}/${this.id}`, {
        name: v.name, slug: v.slug, description: v.description, category: v.category, tags,
        schema: parsed.schema, change_note: v.change_note || null,
      }).pipe(switchMap(s => !this.appSlug && before && before.visibility !== v.visibility
        ? this.api.put<LibrarySchema>(`${this.base}/${this.id}/visibility`, { visibility: v.visibility })
        : of(s)));
    } else {
      req$ = this.api.post<LibrarySchema>(this.base, {
        name: v.name, slug: v.slug, description: v.description || null, category: v.category || null, tags,
        visibility: this.appSlug ? null : v.visibility, schema: parsed.schema,
      });
    }
    req$.subscribe({
      next: s => {
        this.saving.set(false);
        if (this.isEdit) { this.reload(); return; }
        this.router.navigate(this.appSlug ? ['/apps', this.appSlug, 'schemas', s.id] : ['/admin/schemas', s.id]);
      },
      error: err => { this.error.set(apiErrorMessage(err, 'Failed to save schema.')); this.saving.set(false); },
    });
  }

  compare(version: number) {
    const s = this.current();
    if (!s) return;
    this.api.get<LibrarySchemaVersion>(`${this.base}/${this.id}/versions/${version}`).subscribe({
      next: old => {
        this.diffLabel.set(`v${version} → v${s.version}`);
        this.diff.set(lineDiff(prettyJson(old.schema), prettyJson(s.schema)));
      },
      error: err => this.error.set(apiErrorMessage(err, 'Failed to load version.')),
    });
  }

  toggleArchive() {
    const s = this.current();
    if (!s) return;
    this.api.post<LibrarySchema>(`${this.base}/${s.id}/${s.is_archived ? 'unarchive' : 'archive'}`).subscribe({
      next: updated => this.current.set(updated),
      error: err => this.error.set(apiErrorMessage(err, 'Failed to update schema.')),
    });
  }

  remove() {
    this.confirmingDelete.set(false);
    this.api.delete<void>(`${this.base}/${this.id}`).subscribe({
      next: () => this.router.navigate(this.listLink()),
      error: err => this.error.set(apiErrorMessage(err, 'Failed to delete schema.')),
    });
  }
}
