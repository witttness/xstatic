export type FieldType = 'object' | 'array' | 'string' | 'number' | 'integer' | 'boolean';

export interface SchemaField {
  key: string;
  type: FieldType;
  title: string;
  description?: string;
  required: boolean;
  defaultValue?: unknown;
  enumValues?: (string | number | boolean)[];
  properties?: SchemaField[];
  items?: SchemaField;
}

export type FormDescription =
  | { supported: true; fields: SchemaField[] }
  | { supported: false; reason: string };
