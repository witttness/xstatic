import {
  ChangeDetectionStrategy,
  Component,
  Input,
  Output,
  EventEmitter,
} from '@angular/core';
import { JsonFormsModule } from '@jsonforms/angular';
import { JsonSchema } from '@jsonforms/core';
import type Ajv from 'ajv';
import type { ErrorObject } from 'ajv';
import { tailwindRenderers } from './renderers';
import { createFormAjv } from './form-ajv';

/**
 * Renders JSON Forms with the dashboard's Tailwind renderers for editing a
 * collection Item's data. Server validation errors can be passed in as
 * `additionalErrors` to be shown inline next to the affected fields.
 */
@Component({
  selector: 'app-item-json-form',
  standalone: true,
  imports: [JsonFormsModule],
  template: `
    <jsonforms
      [schema]="schema"
      [ajv]="ajv"
      [data]="data"
      [renderers]="renderers"
      [readonly]="readonly"
      [additionalErrors]="additionalErrors"
      (dataChange)="dataChange.emit($event)"
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ItemJsonFormComponent {
  private currentSchema!: JsonSchema;
  @Input({ required: true }) set schema(value: JsonSchema) {
    this.currentSchema = value;
    this.ajv = createFormAjv(value);
  }
  get schema(): JsonSchema { return this.currentSchema; }
  ajv!: Ajv;
  @Input() data?: unknown;
  @Input() readonly = false;
  @Input() additionalErrors: ErrorObject[] = [];
  @Output() dataChange = new EventEmitter<unknown>();
  renderers = tailwindRenderers;
}
