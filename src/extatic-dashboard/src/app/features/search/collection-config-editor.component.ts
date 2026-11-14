import { Component, DestroyRef, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import { Collection } from '../../core/models/collection.model';
import { ButtonComponent } from '../../shared/button/button.component';
import { BadgeComponent } from '../../shared/badge/badge.component';
import {
  CollectionConfig,
  CollectionConfigSaveResult,
  INPUT_CLASS,
  LABEL_CLASS,
  PutCollectionConfigRequest,
  ReindexJob,
  SEARCH_LANGUAGES,
} from './search.models';

/** Splits a textarea of pointers (one per line or comma-separated). */
export function parsePointerList(text: string | null | undefined): string[] {
  return (text ?? '').split(/[\n,]/).map(p => p.trim()).filter(p => p.length > 0);
}

/** RFC 6901 pointer: empty or starting with "/". */
export function isValidPointer(pointer: string): boolean {
  return pointer.length <= 200 && (pointer === '' || pointer.startsWith('/'));
}

/** "/path/{field}" or an http(s) URL, placeholders {name} only. Mirrors the API check. */
export function isValidUrlTemplate(template: string): boolean {
  if (template.length > 500) return false;
  const filled = template.replace(/\{[A-Za-z0-9_]{1,64}\}/g, 'x');
  if (/[{}]/.test(filled)) return false;
  if (filled.startsWith('/')) return !filled.startsWith('//');
  return /^https?:\/\/[^\s/]+/i.test(filled);
}

export interface ConfigFormValue {
  enabled: boolean;
  title_pointer: string;
  body_pointers: string;
  tags_pointer: string;
  url_template: string;
  section: string;
  language: string;
}

/** Returns field errors (keyed by form control) or the request body. */
export function toConfigRequest(value: ConfigFormValue):
  { errors: Record<string, string> } | { request: PutCollectionConfigRequest } {
  const errors: Record<string, string> = {};
  const title = value.title_pointer.trim();
  const body = parsePointerList(value.body_pointers);
  const tags = value.tags_pointer.trim();
  const url = value.url_template.trim();
  if (!isValidPointer(title)) errors['title_pointer'] = 'Use a JSON pointer such as /title.';
  if (body.length > 10) errors['body_pointers'] = 'At most 10 body pointers.';
  else if (body.some(p => !isValidPointer(p))) errors['body_pointers'] = 'Each pointer must start with /.';
  if (tags && !isValidPointer(tags)) errors['tags_pointer'] = 'Use a JSON pointer such as /tags.';
  if (url && !isValidUrlTemplate(url)) errors['url_template'] = 'Use a path such as /blog/{slug} or an http(s) URL.';
  if (Object.keys(errors).length > 0) return { errors };
  return {
    request: {
      enabled: value.enabled,
      title_pointer: title,
      body_pointers: body,
      tags_pointer: tags || null,
      url_template: url || null,
      section: value.section.trim() || null,
      language: value.language || null,
    },
  };
}

@Component({
  selector: 'app-collection-config-editor',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, BadgeComponent],
  template: `
    @if (loadError()) {
      <p role="alert" class="text-sm text-error">{{ loadError() }}</p>
    } @else {
      <div class="grid gap-6 md:grid-cols-[14rem_1fr]">
        <div>
          <p [class]="labelClass">Collections</p>
          <ul class="border border-border divide-y divide-border">
            @for (c of collections(); track c.id) {
              <li>
                <button type="button" (click)="select(c.slug)"
                  [class]="'w-full text-left px-3 py-2 text-sm hover:bg-surface ' + (selected() === c.slug ? 'bg-surface text-text-primary' : 'text-text-muted')">
                  <span class="block">{{ c.name }}</span>
                  <span class="text-xs">
                    @if (configFor(c.slug); as cfg) {
                      {{ cfg.enabled ? 'Indexed · ' + cfg.document_count + ' docs' : 'Disabled' }}
                    } @else { Not indexed }
                  </span>
                </button>
              </li>
            } @empty {
              <li class="px-3 py-2 text-xs text-text-muted">No collections yet.</li>
            }
          </ul>
        </div>

        @if (selected()) {
          <form [formGroup]="form" (ngSubmit)="save()" class="space-y-4 max-w-xl" aria-label="Collection search configuration">
            <label class="flex items-center gap-2 cursor-pointer">
              <input formControlName="enabled" type="checkbox" class="w-4 h-4 accent-accent" />
              <span class="text-sm text-text-primary">Index Items of “{{ selected() }}”</span>
            </label>

            <div>
              <label [class]="labelClass" for="title_pointer">Title pointer</label>
              <input id="title_pointer" formControlName="title_pointer" [class]="inputClass + ' font-mono'" placeholder="/title" />
              @if (fieldErrors()['title_pointer']) { <p class="text-xs text-error mt-1">{{ fieldErrors()['title_pointer'] }}</p> }
            </div>

            <div>
              <label [class]="labelClass" for="body_pointers">Body pointers <span class="normal-case font-normal">(one per line; strings or arrays of strings)</span></label>
              <textarea id="body_pointers" formControlName="body_pointers" rows="3" [class]="inputClass + ' font-mono'" placeholder="/body"></textarea>
              @if (fieldErrors()['body_pointers']) { <p class="text-xs text-error mt-1">{{ fieldErrors()['body_pointers'] }}</p> }
            </div>

            <div class="grid gap-4 sm:grid-cols-2">
              <div>
                <label [class]="labelClass" for="tags_pointer">Tags pointer</label>
                <input id="tags_pointer" formControlName="tags_pointer" [class]="inputClass + ' font-mono'" placeholder="/tags" />
                @if (fieldErrors()['tags_pointer']) { <p class="text-xs text-error mt-1">{{ fieldErrors()['tags_pointer'] }}</p> }
              </div>
              <div>
                <label [class]="labelClass" for="section">Section</label>
                <input id="section" formControlName="section" [class]="inputClass" placeholder="blog" />
              </div>
            </div>

            <div>
              <label [class]="labelClass" for="url_template">URL template</label>
              <input id="url_template" formControlName="url_template" [class]="inputClass + ' font-mono'" placeholder="/blog/{slug}" />
              <p class="text-xs text-text-muted mt-1">Placeholders: top-level fields like <code>{{ '{slug}' }}</code>, or <code>{{ '{id}' }}</code>.</p>
              @if (fieldErrors()['url_template']) { <p class="text-xs text-error mt-1">{{ fieldErrors()['url_template'] }}</p> }
            </div>

            <div>
              <label [class]="labelClass" for="language">Language</label>
              <select id="language" formControlName="language" [class]="inputClass">
                <option value="">App default</option>
                @for (l of languages; track l) { <option [value]="l">{{ l }}</option> }
              </select>
            </div>

            @if (error()) { <p role="alert" class="text-xs text-error">{{ error() }}</p> }

            <div class="flex items-center gap-3">
              <app-button type="submit" [loading]="saving()">Save &amp; reindex</app-button>
              @if (configFor(selected()!)) {
                <app-button variant="secondary" (click)="remove()">Remove config</app-button>
              }
            </div>

            @if (job(); as j) {
              <div class="flex items-center gap-2 text-xs text-text-muted" role="status">
                Reindex
                <app-badge [variant]="j.status === 'completed' ? 'success' : j.status === 'failed' ? 'error' : 'info'">{{ j.status }}</app-badge>
                @if (j.total > 0) { <span>{{ j.processed }} / {{ j.total }}</span> }
                @if (j.error) { <span class="text-error">{{ j.error }}</span> }
              </div>
            }
          </form>
        } @else {
          <p class="text-sm text-text-muted">Pick a collection to configure how its Items are indexed.</p>
        }
      </div>
    }
  `,
})
export class CollectionConfigEditorComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);
  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  readonly inputClass = INPUT_CLASS;
  readonly labelClass = LABEL_CLASS;
  readonly languages = SEARCH_LANGUAGES;

  collections = signal<Collection[]>([]);
  configs = signal<CollectionConfig[]>([]);
  selected = signal<string | null>(null);
  saving = signal(false);
  error = signal<string | null>(null);
  loadError = signal<string | null>(null);
  fieldErrors = signal<Record<string, string>>({});
  job = signal<ReindexJob | null>(null);

  form = this.fb.nonNullable.group({
    enabled: true,
    title_pointer: '/title',
    body_pointers: '/body',
    tags_pointer: '',
    url_template: '',
    section: '',
    language: '',
  });

  ngOnInit() {
    this.destroyRef.onDestroy(() => this.stopPolling());
    this.api.get<Collection[]>(`/apps/${this.appSlug}/collections`).subscribe({
      next: cs => this.collections.set(cs),
      error: () => this.loadError.set('Failed to load collections.'),
    });
    this.loadConfigs();
  }

  loadConfigs() {
    this.api.get<CollectionConfig[]>(`/apps/${this.appSlug}/search/collections`).subscribe({
      next: cs => this.configs.set(cs),
      error: (e: HttpErrorResponse) => this.loadError.set(e.status === 403
        ? 'Only owners and admins can configure collection indexing.'
        : 'Failed to load search configuration.'),
    });
  }

  configFor(slug: string): CollectionConfig | undefined {
    return this.configs().find(c => c.collection_slug === slug);
  }

  select(slug: string) {
    this.selected.set(slug);
    this.error.set(null);
    this.fieldErrors.set({});
    this.job.set(null);
    this.stopPolling();
    const cfg = this.configFor(slug);
    this.form.reset({
      enabled: cfg?.enabled ?? true,
      title_pointer: cfg?.title_pointer ?? '/title',
      body_pointers: (cfg?.body_pointers ?? ['/body']).join('\n'),
      tags_pointer: cfg?.tags_pointer ?? '',
      url_template: cfg?.url_template ?? '',
      section: cfg?.section ?? '',
      language: cfg?.language ?? '',
    });
  }

  save() {
    const slug = this.selected();
    if (!slug) return;
    const result = toConfigRequest(this.form.getRawValue());
    if ('errors' in result) { this.fieldErrors.set(result.errors); return; }
    this.fieldErrors.set({});
    this.error.set(null);
    this.saving.set(true);
    this.api.put<CollectionConfigSaveResult>(`/apps/${this.appSlug}/search/collections/${slug}`, result.request).subscribe({
      next: saved => {
        this.saving.set(false);
        this.configs.update(cs => [...cs.filter(c => c.collection_slug !== slug), saved.config]);
        this.job.set(saved.reindex_job);
        if (saved.reindex_job) this.schedulePoll(saved.reindex_job.id);
      },
      error: (e: HttpErrorResponse) => {
        this.saving.set(false);
        const errors = e.error?.errors as { pointer: string; message: string }[] | undefined;
        this.error.set(errors?.length ? errors.map(x => `${x.pointer}: ${x.message}`).join(' ') : 'Failed to save configuration.');
      },
    });
  }

  remove() {
    const slug = this.selected();
    if (!slug) return;
    this.api.delete<void>(`/apps/${this.appSlug}/search/collections/${slug}`).subscribe({
      next: () => { this.configs.update(cs => cs.filter(c => c.collection_slug !== slug)); this.select(slug); },
      error: () => this.error.set('Failed to remove configuration.'),
    });
  }

  /** Fetches the job once and schedules another poll while it is pending/running. */
  pollJob(id: string) {
    this.api.get<ReindexJob>(`/apps/${this.appSlug}/search/reindex/${id}`).subscribe({
      next: job => {
        this.job.set(job);
        if (job.status === 'pending' || job.status === 'running') this.schedulePoll(id);
        else this.loadConfigs();
      },
      error: () => this.stopPolling(),
    });
  }

  private schedulePoll(id: string) {
    this.stopPolling();
    this.pollTimer = setTimeout(() => this.pollJob(id), 1500);
  }

  private stopPolling() {
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }
}
