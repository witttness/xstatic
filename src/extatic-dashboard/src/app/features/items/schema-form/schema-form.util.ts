import { FormDescription, SchemaField, FieldType } from './schema-form.model';

const unsupported = ['oneOf', 'anyOf', 'allOf', 'not', 'if', 'then', 'else', '$ref', 'patternProperties', 'dependentSchemas', 'prefixItems', 'contains', 'unevaluatedProperties'];
const fieldTypes: FieldType[] = ['object', 'array', 'string', 'number', 'integer', 'boolean'];

export function describeSchema(raw: string | null): FormDescription {
  if (!raw) return { supported: false, reason: 'This collection has no JSON Schema. Enter raw JSON.' };
  try {
    const schema: unknown = JSON.parse(raw);
    if (!isRecord(schema) || schema['type'] !== 'object') throw new Error('The schema root must be an object.');
    const root = parseField('', schema, false, 0);
    if (!root.properties?.length) throw new Error('The schema has no form fields.');
    return { supported: true, fields: root.properties };
  } catch (error) {
    return { supported: false, reason: `A form cannot represent this schema: ${error instanceof Error ? error.message : 'unknown shape'}. Enter raw JSON.` };
  }
}

function parseField(key: string, value: unknown, required: boolean, depth: number): SchemaField {
  if (depth > 12 || !isRecord(value) || unsupported.some(k => k in value)) throw new Error('unsupported or deeply nested schema structure');
  const type = value['type'];
  if (typeof type !== 'string' || !fieldTypes.includes(type as FieldType)) throw new Error('unsupported field type');
  const field: SchemaField = {
    key, type: type as FieldType, title: typeof value['title'] === 'string' ? value['title'] : key || 'Value',
    required, description: typeof value['description'] === 'string' ? value['description'] : undefined,
  };
  if ('default' in value) field.defaultValue = value['default'];
  if ('enum' in value) {
    if (!Array.isArray(value['enum']) || !value['enum'].every(v => ['string', 'number', 'boolean'].includes(typeof v))) throw new Error('unsupported enum');
    field.enumValues = value['enum'];
  }
  if (type === 'object') {
    if (value['additionalProperties'] && typeof value['additionalProperties'] === 'object') throw new Error('dynamic object properties');
    const properties = value['properties'];
    if (properties !== undefined && !isRecord(properties)) throw new Error('invalid properties');
    const requiredKeys = Array.isArray(value['required']) ? value['required'] : [];
    field.properties = Object.entries(properties ?? {}).map(([name, child]) => parseField(name, child, requiredKeys.includes(name), depth + 1));
  }
  if (type === 'array') {
    if (!isRecord(value['items'])) throw new Error('array items must use one schema');
    field.items = parseField('', value['items'], false, depth + 1);
  }
  return field;
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function defaultFor(field: SchemaField): unknown {
  if (field.defaultValue !== undefined) return structuredClone(field.defaultValue);
  if (field.type === 'object') {
    const value: Record<string, unknown> = {};
    for (const child of field.properties ?? []) if (child.defaultValue !== undefined) value[child.key] = defaultFor(child);
    return value;
  }
  if (field.type === 'array') return [];
  if (field.type === 'boolean') return false;
  return '';
}

export function initialDraft(fields: SchemaField[]): Record<string, unknown> {
  const draft: Record<string, unknown> = {};
  for (const field of fields) {
    if (field.defaultValue !== undefined) draft[field.key] = defaultFor(field);
    else if (field.type === 'object') {
      const nested = initialDraft(field.properties ?? []);
      if (Object.keys(nested).length) draft[field.key] = nested;
    }
  }
  return draft;
}
