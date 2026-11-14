import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { App } from '../../../core/models/app.model';
import { Form, SaveFormRequest } from '../forms.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { FormEmbedComponent } from '../form-embed/form-embed.component';

const EMAIL = /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/;

@Component({
  selector: 'app-form-editor',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent, FormEmbedComponent],
  template: `
    <app-page-header [title]="isEdit ? 'Edit Form' : 'New Form'">
      @if (isEdit) {
        <a [routerLink]="['/apps', appSlug, 'forms', id, 'submissions']">
          <app-button variant="secondary">Submissions</app-button>
        </a>
      }
    </app-page-header>

    @if (loadingForm()) {
      <app-loading-spinner />
    } @else {
      <div class="grid gap-8 lg:grid-cols-2 items-start">
        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="space-y-5 min-w-0">
          <div>
            <label for="form-name" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Name</label>
            <input id="form-name" formControlName="name" type="text" (input)="autoSlug()" placeholder="Contact"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
            @if (form.get('name')?.invalid && form.get('name')?.touched) {
              <p class="text-xs text-error mt-1">Name is required.</p>
            }
          </div>

          <div>
            <label for="form-slug" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Slug</label>
            <input id="form-slug" formControlName="slug" type="text" placeholder="contact"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
            @if (form.get('slug')?.invalid && form.get('slug')?.touched) {
              <p class="text-xs text-error mt-1">Lowercase letters, digits and hyphens only.</p>
            }
          </div>

          <div>
            <div class="flex items-center justify-between mb-1">
              <label for="form-schema" class="block text-xs font-medium text-text-muted uppercase tracking-wider">
                JSON Schema <span class="normal-case font-normal">(optional)</span>
              </label>
              <button type="button" (click)="validateSchema()" class="text-xs text-accent hover:underline">Validate JSON</button>
            </div>
            <textarea id="form-schema" formControlName="schema" rows="8"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none resize-y"
              placeholder='{ "type": "object", "required": ["email"], "properties": { "email": { "type": "string" } } }'></textarea>
            @if (schemaError()) { <p class="text-xs text-error mt-1">{{ schemaError() }}</p> }
            @if (schemaValid()) { <p class="text-xs text-success mt-1">Valid JSON.</p> }
          </div>

          <div>
            <label for="form-emails" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">
              Notification emails <span class="normal-case font-normal">(comma-separated, max 10)</span>
            </label>
            <input id="form-emails" formControlName="notification_emails" type="text" placeholder="you@example.com"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
            @if (emailsError()) { <p class="text-xs text-error mt-1">{{ emailsError() }}</p> }
          </div>

          <div>
            <label for="form-redirect" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">
              Success redirect URL <span class="normal-case font-normal">(optional, https)</span>
            </label>
            <input id="form-redirect" formControlName="success_redirect_url" type="url" placeholder="https://example.com/thanks"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          </div>

          <div>
            <label for="form-honeypot" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">
              Honeypot field <span class="normal-case font-normal">(optional — a hidden field bots fill in)</span>
            </label>
            <input id="form-honeypot" formControlName="honeypot_field" type="text" placeholder="website"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
          </div>

          <div class="space-y-2">
            <label class="flex items-center gap-2 cursor-pointer">
              <input formControlName="is_active" type="checkbox" class="w-4 h-4 border border-border bg-background accent-accent" />
              <span class="text-sm text-text-primary">Active (accepting submissions)</span>
            </label>
            <label class="flex items-center gap-2 cursor-pointer">
              <input formControlName="store_submissions" type="checkbox" class="w-4 h-4 border border-border bg-background accent-accent" />
              <span class="text-sm text-text-primary">Store submissions</span>
            </label>
            <label class="flex items-center gap-2 cursor-pointer">
              <input formControlName="require_app_user" type="checkbox" class="w-4 h-4 border border-border bg-background accent-accent" />
              <span class="text-sm text-text-primary">Require signed-in AppUser</span>
            </label>
          </div>

          @if (serverErrors().length) {
            <ul class="text-xs text-error space-y-0.5">
              @for (e of serverErrors(); track $index) { <li>{{ e }}</li> }
            </ul>
          }
          @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }

          <div class="flex gap-3 pt-2">
            <app-button type="submit" [loading]="saving()">{{ isEdit ? 'Save Changes' : 'Create Form' }}</app-button>
            <app-button variant="secondary" type="button" (click)="cancel()">Cancel</app-button>
          </div>
        </form>

        @if (isEdit && publicId() && savedSlug()) {
          <app-form-embed class="min-w-0" [publicId]="publicId()!" [form]="{ slug: savedSlug()!, honeypot_field: savedHoneypot() }" />
        }
      </div>
    }
  `,
})
export class FormEditorComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() id?: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  get isEdit() { return !!this.id; }

  form = this.fb.group({
    name: ['', Validators.required],
    slug: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]{1,100}$/)]],
    schema: [''],
    notification_emails: [''],
    success_redirect_url: [''],
    honeypot_field: [''],
    is_active: [true],
    store_submissions: [true],
    require_app_user: [false],
  });

  loadingForm = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);
  serverErrors = signal<string[]>([]);
  schemaError = signal<string | null>(null);
  schemaValid = signal(false);
  emailsError = signal<string | null>(null);
  publicId = signal<string | null>(null);
  savedSlug = signal<string | null>(null);
  savedHoneypot = signal<string | null>(null);

  ngOnInit() {
    if (!this.isEdit) return;
    this.loadingForm.set(true);
    this.api.get<App>(`/apps/${this.appSlug}`).subscribe({ next: app => this.publicId.set(app.public_id), error: () => {} });
    this.api.get<Form>(`/apps/${this.appSlug}/forms/${this.id}`).subscribe({
      next: f => {
        this.form.patchValue({
          name: f.name,
          slug: f.slug,
          schema: f.schema === null ? '' : JSON.stringify(f.schema, null, 2),
          notification_emails: f.notification_emails.join(', '),
          success_redirect_url: f.success_redirect_url ?? '',
          honeypot_field: f.honeypot_field ?? '',
          is_active: f.is_active,
          store_submissions: f.store_submissions,
          require_app_user: f.require_app_user,
        });
        this.savedSlug.set(f.slug);
        this.savedHoneypot.set(f.honeypot_field);
        this.loadingForm.set(false);
      },
      error: () => { this.error.set('Failed to load form.'); this.loadingForm.set(false); },
    });
  }

  autoSlug() {
    if (this.isEdit) return;
    const name = this.form.get('name')!.value ?? '';
    const slug = name.toLowerCase().trim().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, '').slice(0, 100);
    this.form.get('slug')!.setValue(slug, { emitEvent: false });
  }

  /** Returns the parsed schema, null when empty, or undefined (with an error shown) when invalid JSON. */
  validateSchema(): unknown | null | undefined {
    const val = (this.form.get('schema')!.value ?? '').trim();
    this.schemaError.set(null);
    this.schemaValid.set(false);
    if (!val) return null;
    try {
      const parsed = JSON.parse(val);
      if (typeof parsed !== 'object' && typeof parsed !== 'boolean' || Array.isArray(parsed) || parsed === null) {
        this.schemaError.set('Schema must be a JSON object.');
        return undefined;
      }
      this.schemaValid.set(true);
      return parsed;
    } catch {
      this.schemaError.set('Invalid JSON.');
      return undefined;
    }
  }

  parseEmails(): string[] | undefined {
    this.emailsError.set(null);
    const emails = (this.form.get('notification_emails')!.value ?? '')
      .split(/[,\s]+/).map(s => s.trim()).filter(Boolean);
    const bad = emails.filter(e => !EMAIL.test(e));
    if (bad.length) { this.emailsError.set(`Invalid email: ${bad.join(', ')}`); return undefined; }
    if (emails.length > 10) { this.emailsError.set('At most 10 notification emails.'); return undefined; }
    return emails;
  }

  onSubmit() {
    const schema = this.validateSchema();
    const emails = this.parseEmails();
    if (this.form.invalid || schema === undefined || emails === undefined) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const body: SaveFormRequest = {
      name: raw.name!.trim(),
      slug: raw.slug!.trim(),
      schema,
      notification_emails: emails,
      success_redirect_url: raw.success_redirect_url?.trim() || null,
      honeypot_field: raw.honeypot_field?.trim() || null,
      is_active: !!raw.is_active,
      store_submissions: !!raw.store_submissions,
      require_app_user: !!raw.require_app_user,
    };
    this.saving.set(true);
    this.error.set(null);
    this.serverErrors.set([]);

    const req$ = this.isEdit
      ? this.api.put<Form>(`/apps/${this.appSlug}/forms/${this.id}`, body)
      : this.api.post<Form>(`/apps/${this.appSlug}/forms`, body);

    req$.subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'forms']),
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        if (err.status === 422 && Array.isArray(err.error?.errors)) {
          this.serverErrors.set(err.error.errors.map((e: { pointer: string; message: string }) => `${e.pointer}: ${e.message}`));
        } else if (err.status === 409) {
          this.error.set('A form with this slug already exists.');
        } else {
          this.error.set('Failed to save form.');
        }
      },
    });
  }

  cancel() {
    this.router.navigate(['/apps', this.appSlug, 'forms']);
  }
}
