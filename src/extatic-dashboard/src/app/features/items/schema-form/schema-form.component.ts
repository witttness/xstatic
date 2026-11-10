import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SchemaField } from './schema-form.model';
import { defaultFor, isRecord } from './schema-form.util';

@Component({
  selector: 'app-schema-form',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="space-y-4">
      @for (field of fields; track field.key) {
        <ng-container *ngTemplateOutlet="control; context: { field: field, path: [field.key] }" />
      }
    </div>

    <ng-template #control let-field="field" let-path="path">
      <div class="space-y-1">
        <label [attr.for]="id(path)" class="block text-sm font-medium text-text-primary">
          {{ field.title }} @if (field.required) { <span aria-label="required" class="text-error">*</span> }
        </label>
        @if (field.description) { <p class="text-xs text-text-muted">{{ field.description }}</p> }
        @if (field.type === 'object') {
          <fieldset class="border border-border p-3 space-y-3">
            <legend class="sr-only">{{ field.title }}</legend>
            @for (child of field.properties ?? []; track child.key) {
              <ng-container *ngTemplateOutlet="control; context: { field: child, path: path.concat(child.key) }" />
            }
          </fieldset>
        } @else if (field.type === 'array') {
          <div class="space-y-2 border border-border p-3">
            @for (entry of arrayAt(path); track $index) {
              <div class="border-b border-border pb-2">
                <div class="flex justify-between"><span class="text-xs text-text-muted">Entry {{ $index + 1 }}</span>
                  <button type="button" class="text-xs text-error" (click)="remove(path, $index)" [attr.aria-label]="'Remove ' + field.title + ' entry ' + ($index + 1)">Remove</button></div>
                <ng-container *ngTemplateOutlet="control; context: { field: field.items, path: path.concat($index) }" />
              </div>
            }
            <button type="button" class="text-xs text-accent" (click)="add(path, field.items)">Add {{ field.title }} entry</button>
          </div>
        } @else if (field.enumValues) {
          <select [id]="id(path)" [value]="asText(at(path))" (change)="change(field, path, $event)" class="w-full bg-background border border-border px-3 py-2 text-sm">
            <option value="">Select a value</option>
            @for (value of field.enumValues; track $index) { <option [value]="asText(value)">{{ value }}</option> }
          </select>
        } @else if (field.type === 'boolean') {
          <input [id]="id(path)" type="checkbox" [checked]="at(path) === true" (change)="change(field, path, $event)" />
        } @else {
          <input [id]="id(path)" [type]="field.type === 'string' ? 'text' : 'number'" [step]="field.type === 'integer' ? '1' : 'any'"
            [value]="asText(at(path))" (input)="change(field, path, $event)" [attr.aria-describedby]="errorsFor(path).length ? id(path) + '-error' : null"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary" />
        }
        @if (errorsFor(path).length) { <p [id]="id(path) + '-error'" role="alert" class="text-xs text-error">{{ errorsFor(path).join('; ') }}</p> }
      </div>
    </ng-template>
  `,
})
export class SchemaFormComponent {
  @Input({ required: true }) fields!: SchemaField[];
  @Input({ required: true }) draft!: Record<string, unknown>;
  @Input() errors: { pointer: string; message: string }[] = [];
  @Output() draftChange = new EventEmitter<Record<string, unknown>>();

  id(path: (string | number)[]) { return `field-${path.join('-')}`; }
  asText(value: unknown) { return value == null ? '' : String(value); }
  at(path: (string | number)[]): unknown { return path.reduce<unknown>((value, part) => isRecord(value) || Array.isArray(value) ? (value as Record<string | number, unknown>)[part] : undefined, this.draft); }
  arrayAt(path: (string | number)[]): unknown[] { const value = this.at(path); return Array.isArray(value) ? value : []; }
  errorsFor(path: (string | number)[]) { const pointer = '/' + path.join('/'); return this.errors.filter(e => e.pointer === pointer).map(e => e.message); }

  change(field: SchemaField, path: (string | number)[], event: Event) {
    const target = event.target as HTMLInputElement | HTMLSelectElement;
    let value: unknown = target.value;
    if (field.type === 'boolean') value = (target as HTMLInputElement).checked;
    else if (field.type === 'number' || field.type === 'integer') value = target.value === '' ? undefined : Number(target.value);
    else if (field.enumValues) value = field.enumValues.find(v => String(v) === target.value);
    this.set(path, value);
  }
  add(path: (string | number)[], field: SchemaField) { this.set(path, [...this.arrayAt(path), defaultFor(field)]); }
  remove(path: (string | number)[], index: number) { this.set(path, this.arrayAt(path).filter((_, i) => i !== index)); }
  private set(path: (string | number)[], value: unknown) {
    const next = structuredClone(this.draft);
    let target: Record<string | number, unknown> = next;
    for (let index = 0; index < path.length - 1; index++) {
      const part = path[index];
      if (!isRecord(target[part]) && !Array.isArray(target[part])) target[part] = typeof path[index + 1] === 'number' ? [] : {};
      target = target[part] as Record<string | number, unknown>;
    }
    if (value === undefined) delete target[path[path.length - 1]];
    else target[path[path.length - 1]] = value;
    this.draftChange.emit(next);
  }
}
