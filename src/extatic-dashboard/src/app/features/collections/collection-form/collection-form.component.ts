import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { Collection } from '../../../core/models/collection.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-collection-form',
  standalone: true,
  imports: [ReactiveFormsModule, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent],
  template: `
    <app-page-header [title]="isEdit ? 'Edit Collection' : 'New Collection'" />

    @if (loadingCollection()) {
      <app-loading-spinner />
    } @else {
      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="max-w-lg space-y-5">
        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Name</label>
          <input formControlName="name" type="text"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <p class="text-xs text-error mt-1">Name is required.</p>
          }
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Slug</label>
          <input formControlName="slug" type="text"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed" />
          @if (form.get('slug')?.invalid && form.get('slug')?.touched) {
            <p class="text-xs text-error mt-1">Slug is required.</p>
          }
        </div>

        <div>
          <div class="flex items-center justify-between mb-1">
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider">
              JSON Schema <span class="normal-case font-normal text-text-muted">(optional)</span>
            </label>
            <button type="button" (click)="validateSchema()"
              class="text-xs text-accent hover:underline">Validate JSON</button>
          </div>
          <textarea formControlName="schema" rows="8"
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
          this.loadingCollection.set(false);
        },
        error: () => this.loadingCollection.set(false),
      });
    }
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
    const body = {
      name: raw.name,
      slug: raw.slug,
      schema: raw.schema?.trim() || null,
      attachments_enabled: raw.attachments_enabled,
      allowed_attachment_types: types,
    };
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
