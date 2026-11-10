import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { App } from '../../../core/models/app.model';
import { Collection } from '../../../core/models/collection.model';
import { Item, ApiProblem } from '../../../core/models/item.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { SchemaFormComponent } from '../schema-form/schema-form.component';
import { FormDescription } from '../schema-form/schema-form.model';
import { describeSchema, initialDraft, isRecord } from '../schema-form/schema-form.util';

@Component({
  selector: 'app-item-editor',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent, SchemaFormComponent],
  template: `
    <app-page-header [title]="itemId ? 'Item' : 'New Item'" />
    <a [routerLink]="listLink" class="text-xs text-accent hover:underline">← Items</a>
    @if (loading()) { <p role="status" class="mt-5 text-sm">Loading item…</p> }
    @else if (loadError()) { <p role="alert" class="mt-5 text-error">{{ loadError() }}</p> }
    @else {
      @if (saved()) { <p role="status" class="mt-4 text-success">Item saved. ID: {{ saved()!.id }}</p> }
      @if (!app()?.can_manage_items) { <p role="status" class="mt-4 text-sm">You can view this Item, but your App role cannot change it.</p> }
      @if (item()) { <p class="mt-3 font-mono text-xs text-text-muted">{{ item()!.id }} · Version {{ item()!.version }}</p> }
      @if (description().supported && app()?.can_manage_items) {
        <div class="flex gap-3 mt-5" role="group" aria-label="Item entry mode">
          <button type="button" (click)="switchMode('form')" [attr.aria-pressed]="mode() === 'form'" class="text-sm text-accent">Form</button>
          <button type="button" (click)="switchMode('raw')" [attr.aria-pressed]="mode() === 'raw'" class="text-sm text-accent">Raw JSON</button>
        </div>
      } @else if (!description().supported && app()?.can_manage_items) {
        <p class="mt-4 text-xs text-text-muted">{{ fallbackReason() }}</p>
      }
      <div class="mt-5 max-w-2xl">
        @if (mode() === 'form' && description().supported && app()?.can_manage_items) {
          <app-schema-form [fields]="formFields()" [draft]="draft()" [errors]="fieldErrors()" (draftChange)="onFormChange($event)" />
        } @else {
          <label for="item-json" class="block text-sm font-medium mb-2">Item JSON data</label>
          <textarea id="item-json" rows="16" [value]="raw()" (input)="onRawInput($event)" [readOnly]="!app()?.can_manage_items"
            class="w-full bg-background border border-border px-3 py-2 font-mono text-sm text-text-primary"></textarea>
        }
        @if (inputError()) { <p role="alert" class="text-sm text-error mt-2">{{ inputError() }}</p> }
        @if (fieldErrors().length) {
          <ul role="alert" class="mt-3 text-sm text-error">
            @for (error of fieldErrors(); track $index) { <li>{{ error.pointer || 'Data' }}: {{ error.message }}</li> }
          </ul>
        }
        @if (saveError()) { <p role="alert" class="text-sm text-error mt-3">{{ saveError() }}</p> }
        @if (conflict()) {
          <p role="alert" class="text-sm text-error mt-3">This Item changed since you opened it. Your draft is preserved. Reload the latest Item before saving again.</p>
          <button type="button" (click)="reload()" class="text-sm text-accent mt-2">Reload Item</button>
        }
        @if (app()?.can_manage_items) {
          <button type="button" (click)="save()" [disabled]="saving() || conflict()" class="mt-5 bg-accent px-4 py-2 text-sm text-background disabled:opacity-50">
            {{ saving() ? 'Saving…' : itemId ? 'Save Item' : 'Create Item' }}
          </button>
        }
      </div>
    }
  `,
})
export class ItemEditorComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() collectionSlug!: string;
  @Input() itemId?: string;
  private api = inject(ApiService);
  private location = inject(Location);
  app = signal<App | null>(null);
  collection = signal<Collection | null>(null);
  item = signal<Item | null>(null);
  loading = signal(true);
  loadError = signal<string | null>(null);
  saving = signal(false);
  saved = signal<Item | null>(null);
  saveError = signal<string | null>(null);
  inputError = signal<string | null>(null);
  fieldErrors = signal<{ pointer: string; message: string }[]>([]);
  conflict = signal(false);
  description = signal<FormDescription>({ supported: false, reason: '' });
  mode = signal<'form' | 'raw'>('raw');
  draft = signal<Record<string, unknown>>({});
  raw = signal('{}');
  get base() { return `/apps/${this.appSlug}/collections/${this.collectionSlug}/items`; }
  get listLink() { return ['/apps', this.appSlug, 'collections', this.collectionSlug, 'items']; }
  formFields() { const result = this.description(); return result.supported ? result.fields : []; }
  fallbackReason() { const result = this.description(); return result.supported ? '' : result.reason; }

  ngOnInit() {
    forkJoin({
      app: this.api.get<App>(`/apps/${this.appSlug}`),
      collection: this.api.get<Collection>(`/apps/${this.appSlug}/collections/${this.collectionSlug}`),
      ...(this.itemId ? { item: this.api.get<Item>(`${this.base}/${this.itemId}`) } : {}),
    }).subscribe({
      next: result => {
        this.app.set(result.app);
        this.collection.set(result.collection);
        const description = describeSchema(result.collection.schema);
        this.description.set(description);
        this.mode.set(description.supported && result.app.can_manage_items ? 'form' : 'raw');
        if (!this.itemId && description.supported) {
          const initial = initialDraft(description.fields);
          this.draft.set(initial);
          this.raw.set(JSON.stringify(initial, null, 2));
        }
        if ('item' in result) this.setItem(result.item as Item);
        this.loading.set(false);
      },
      error: () => { this.loadError.set('Unable to load the Collection or Item.'); this.loading.set(false); },
    });
  }
  private setItem(item: Item) {
    this.item.set(item);
    if (isRecord(item.data)) {
      this.draft.set(structuredClone(item.data));
      this.raw.set(JSON.stringify(item.data, null, 2));
    } else {
      this.mode.set('raw');
      this.raw.set(JSON.stringify(item.data, null, 2));
    }
  }
  onRawInput(event: Event) { this.raw.set((event.target as HTMLTextAreaElement).value); this.inputError.set(null); }
  onFormChange(value: Record<string, unknown>) { this.draft.set(value); this.raw.set(JSON.stringify(value, null, 2)); this.fieldErrors.set([]); }
  switchMode(next: 'form' | 'raw') {
    if (next === 'form') {
      const parsed = this.parseRaw();
      if (!parsed) return;
      this.draft.set(parsed);
    } else this.raw.set(JSON.stringify(this.draft(), null, 2));
    this.mode.set(next);
  }
  private parseRaw(): Record<string, unknown> | null {
    try {
      if (!this.raw().trim()) throw new Error('Enter a JSON object.');
      const value: unknown = JSON.parse(this.raw());
      if (!isRecord(value)) throw new Error('Item data must be a JSON object.');
      this.inputError.set(null);
      return value;
    } catch (error) {
      this.inputError.set(error instanceof SyntaxError ? 'Enter valid JSON.' : (error as Error).message);
      return null;
    }
  }
  save() {
    if (!this.app()?.can_manage_items || this.saving() || this.conflict()) return;
    const data = this.mode() === 'form' ? this.draft() : this.parseRaw();
    if (!data) return;
    this.saveError.set(null); this.fieldErrors.set([]); this.saved.set(null); this.saving.set(true);
    const request = this.itemId
      ? this.api.put<Item>(`${this.base}/${this.itemId}`, { data, version: this.item()!.version })
      : this.api.post<Item>(this.base, { data });
    request.subscribe({
      next: item => {
        const wasNew = !this.itemId;
        this.setItem(item);
        this.saved.set(item);
        this.saving.set(false);
        if (wasNew) {
          this.itemId = item.id;
          this.location.replaceState(`${this.base}/${item.id}`);
        }
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        const problem = error.error as ApiProblem | undefined;
        this.fieldErrors.set(problem?.errors?.errors ?? []);
        if (error.status === 409) this.conflict.set(true);
        else if (error.status === 403) this.saveError.set('Your App role no longer allows changes.');
        else if (error.status === 422) this.saveError.set(problem?.detail ?? 'Item data does not match the current schema.');
        else this.saveError.set('Save failed. Your draft is preserved. Check the Item before retrying.');
      },
    });
  }
  reload() {
    if (!this.itemId) return;
    this.api.get<Item>(`${this.base}/${this.itemId}`).subscribe({
      next: item => { this.setItem(item); this.conflict.set(false); this.saveError.set(null); this.fieldErrors.set([]); },
      error: () => this.saveError.set('Unable to reload the Item.'),
    });
  }
}
