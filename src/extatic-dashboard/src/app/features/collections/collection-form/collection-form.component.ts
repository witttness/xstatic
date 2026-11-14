import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { Collection } from '../../../core/models/collection.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { PickedSchema, SchemaPickerComponent } from '../../schema-library/schema-picker/schema-picker.component';
import { DiffLine, LineDiffComponent, lineDiff } from '../../schema-library/line-diff';
import { AvailableSchemaDetail, LibrarySchemaVersion, prettyJson, sameJson } from '../../schema-library/schema-library.models';

/** Schema Library provenance shown on the form (the content itself is always a copy). */
export interface SchemaSource {
  id: string;
  /** Null when the source schema is no longer visible to this app (or was deleted). */
  name: string | null;
  version: number;
  latestVersion: number | null;
}

@Component({
  selector: 'app-collection-form',
  standalone: true,
  imports: [ReactiveFormsModule, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent, BadgeComponent,
    SchemaPickerComponent, LineDiffComponent],
  template: `
    <app-page-header [title]="isEdit ? 'Edit Collection' : 'New Collection'" />

    @if (loadingCollection()) {
      <app-loading-spinner />
    } @else {
      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="max-w-lg space-y-5">
        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Name</label>
          <input 
            formControlName="name" 
            type="text"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"
            placeholder="My Collection"
            (input)="autoSlug()" />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <p class="text-xs text-error mt-1">Name is required.</p>
          }
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Slug</label>
          <input 
            formControlName="slug" 
            type="text"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed"
            placeholder="my-collection" />
          @if (form.get('slug')?.invalid && form.get('slug')?.touched) {
            <p class="text-xs text-error mt-1">Slug is required.</p>
          }
        </div>

        <div>
          <div class="flex items-center justify-between mb-1">
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider">
              JSON Schema <span class="normal-case font-normal text-text-muted">(optional)</span>
            </label>
            <div class="flex items-center gap-3">
              <button type="button" (click)="pickerOpen.set(true)"
                class="text-xs text-accent hover:underline">Choose from library</button>
              <button type="button" (click)="validateSchema()"
                class="text-xs text-accent hover:underline">Validate JSON</button>
            </div>
          </div>
          @if (source(); as src) {
            <div class="flex flex-wrap items-center gap-2 mb-2 text-xs" data-testid="schema-source">
              @if (src.name) {
                <span class="text-text-muted">Based on <span class="text-text-primary">{{ src.name }}</span> v{{ src.version }}</span>
              } @else {
                <span class="text-text-muted">Based on a library schema (v{{ src.version }}) that is no longer available</span>
              }
              @if (sourceModified()) { <app-badge variant="warning">modified</app-badge> }
              @if (src.latestVersion !== null && src.latestVersion > src.version) {
                <button type="button" (click)="reviewUpdate()" class="text-accent hover:underline">Update available (v{{ src.latestVersion }})</button>
              }
              <button type="button" (click)="detach()" class="text-text-muted hover:text-text-primary underline">Detach</button>
            </div>
          }
          <textarea formControlName="schema" (input)="onSchemaEdited()" rows="8"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none resize-y"
            placeholder='{ "type": "object", "properties": { ... } }'></textarea>
          @if (schemaError()) { <p class="text-xs text-error mt-1">{{ schemaError() }}</p> }
          @if (schemaValid()) { <p class="text-xs text-success mt-1">Valid JSON.</p> }
        </div>

        <div>
          <label class="flex items-center gap-2 cursor-pointer">
            <input formControlName="attachments_enabled" type="checkbox"
              class="w-4 h-4 border border-border bg-background accent-accent" />
            <span class="text-sm text-text-primary">Enable Attachments</span>
          </label>
        </div>

        @if (form.get('attachments_enabled')?.value) {
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">
              Allowed Types <span class="normal-case font-normal">(comma-separated MIME types)</span>
            </label>
            <input formControlName="allowed_attachment_types" type="text"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"
              placeholder="image/jpeg, image/png, application/pdf" />
          </div>
        }

        @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }

        <div class="flex gap-3 pt-2">
          <app-button type="submit" [loading]="saving()">{{ isEdit ? 'Save Changes' : 'Create Collection' }}</app-button>
          <app-button variant="secondary" type="button" (click)="cancel()">Cancel</app-button>
        </div>
      </form>
    }

    @if (pickerOpen()) {
      <app-schema-picker [appSlug]="appSlug" (picked)="usePicked($event)" (cancelled)="pickerOpen.set(false)" />
    }

    @if (pendingUpdate(); as upd) {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div class="absolute inset-0 bg-black/60" (click)="pendingUpdate.set(null)"></div>
        <div class="relative bg-surface border border-border w-full max-w-3xl p-6 z-10" role="dialog" aria-label="Review schema update">
          <h3 class="text-base font-semibold text-text-primary mb-1">Update to {{ upd.name }} v{{ upd.version }}</h3>
          <p class="text-sm text-text-muted mb-3">Changes from the current schema in this form. Applying replaces the schema text; it only affects Items saved afterwards.</p>
          <app-line-diff [lines]="updateDiff()" />
          <div class="flex justify-end gap-2 mt-4">
            <app-button variant="secondary" (click)="pendingUpdate.set(null)">Cancel</app-button>
            <app-button (click)="applyUpdate()">Apply update</app-button>
          </div>
        </div>
      </div>
    }
  `,
})
export class CollectionFormComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() slug?: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  get isEdit() { return !!this.slug; }

  form = this.fb.group({
    name: ['', Validators.required],
    slug: ['', Validators.required],
    schema: [''],
    attachments_enabled: [false],
    allowed_attachment_types: [''],
  });

  loadingCollection = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);
  schemaError = signal<string | null>(null);
  schemaValid = signal(false);

  // Schema Library provenance
  pickerOpen = signal(false);
  source = signal<SchemaSource | null>(null);
  sourceModified = signal(false);
  pendingUpdate = signal<AvailableSchemaDetail | null>(null);
  updateDiff = signal<DiffLine[]>([]);
  /** The loaded collection had provenance; saving without one must explicitly detach. */
  private hadSource = false;

  ngOnInit() {
    if (this.isEdit) {
      this.loadingCollection.set(true);
      this.api.get<Collection>(`/apps/${this.appSlug}/collections/${this.slug}`).subscribe({
        next: col => {
          this.form.patchValue({
            name: col.name,
            slug: col.slug,
            schema: col.schema ?? '',
            attachments_enabled: col.attachments_enabled,
            allowed_attachment_types: col.allowed_attachment_types.join(', '),
          });
          this.form.get('slug')!.disable();
          this.loadSource(col);
          this.loadingCollection.set(false);
        },
        error: () => this.loadingCollection.set(false),
      });
    }
  }

  private loadSource(col: Collection) {
    if (!col.source_schema_id || col.source_schema_version == null) return;
    this.hadSource = true;
    this.source.set({
      id: col.source_schema_id,
      name: col.source_schema_name ?? null,
      version: col.source_schema_version,
      latestVersion: col.source_schema_latest_version ?? null,
    });
    if (!col.source_schema_name) return;
    // Mark "modified" when the stored schema differs from the version it was copied from.
    this.api.get<LibrarySchemaVersion>(
      `/apps/${this.appSlug}/schemas/available/${col.source_schema_id}/versions/${col.source_schema_version}`,
    ).subscribe({
      next: v => this.sourceModified.set(!sameJson(this.form.get('schema')!.value ?? '', v.schema)),
      error: () => {},
    });
  }

  usePicked(p: PickedSchema) {
    this.pickerOpen.set(false);
    this.form.get('schema')!.setValue(p.schemaText);
    this.source.set({ id: p.id, name: p.name, version: p.version, latestVersion: p.version });
    this.sourceModified.set(false);
    this.schemaError.set(null);
    this.schemaValid.set(false);
  }

  onSchemaEdited() {
    if (this.source()) this.sourceModified.set(true);
  }

  detach() {
    this.source.set(null);
    this.sourceModified.set(false);
  }

  reviewUpdate() {
    const src = this.source();
    if (!src) return;
    this.api.get<AvailableSchemaDetail>(`/apps/${this.appSlug}/schemas/available/${src.id}`).subscribe({
      next: latest => {
        this.updateDiff.set(lineDiff(this.form.get('schema')!.value ?? '', prettyJson(latest.schema)));
        this.pendingUpdate.set(latest);
      },
      error: () => this.error.set('Failed to load the latest library schema.'),
    });
  }

  applyUpdate() {
    const latest = this.pendingUpdate();
    if (!latest) return;
    this.pendingUpdate.set(null);
    this.usePicked({ id: latest.id, name: latest.name, version: latest.version, schemaText: prettyJson(latest.schema) });
  }

  autoSlug() {
    const name = this.form.get('name')!.value ?? '';
    const slug = name.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, '');
    this.form.get('slug')!.setValue(slug, { emitEvent: false });
  }

  validateSchema() {
    const val = this.form.get('schema')!.value ?? '';
    this.schemaError.set(null);
    this.schemaValid.set(false);
    if (!val.trim()) return;
    try {
      JSON.parse(val);
      this.schemaValid.set(true);
    } catch {
      this.schemaError.set('Invalid JSON.');
    }
  }

  onSubmit() {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const raw = this.form.getRawValue();
    const types = (raw.allowed_attachment_types ?? '').split(',').map(s => s.trim()).filter(Boolean);
    const src = this.source();
    const body: Record<string, unknown> = {
      name: raw.name,
      slug: raw.slug,
      schema: raw.schema?.trim() || null,
      attachments_enabled: raw.attachments_enabled,
      allowed_attachment_types: types,
    };
    if (src) {
      body['source_schema_id'] = src.id;
      body['source_schema_version'] = src.version;
    } else if (this.isEdit && this.hadSource) {
      body['detach_source_schema'] = true;
    }
    this.saving.set(true);
    this.error.set(null);

    const req$ = this.isEdit
      ? this.api.put<Collection>(`/apps/${this.appSlug}/collections/${this.slug}`, body)
      : this.api.post<Collection>(`/apps/${this.appSlug}/collections`, body);

    req$.subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'collections']),
      error: () => { this.error.set('Failed to save collection.'); this.saving.set(false); },
    });
  }

  cancel() {
    this.router.navigate(['/apps', this.appSlug, 'collections']);
  }
}
