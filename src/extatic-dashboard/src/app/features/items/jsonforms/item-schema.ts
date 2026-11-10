import { JsonSchema } from '@jsonforms/core';
import { createFormAjv } from './form-ajv';

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export interface ParsedItemSchema {
  /** The parsed schema, or null when a form cannot represent it. */
  schema: JsonSchema | null;
  /** Why the schema is not form-compatible, or null when it is. */
  reason: string | null;
}

/**
 * Parses a collection's JSON Schema for JSON Forms.
 *
 * The registered renderer set needs ordinary object properties at the root.
 * Schemas without them remain editable through raw JSON.
 */
export function parseItemSchema(raw: string | null): ParsedItemSchema {
  if (!raw) {
    return { schema: null, reason: 'This collection has no JSON Schema. Enter raw JSON.' };
  }
  try {
    const schema: unknown = JSON.parse(raw);
    if (!isRecord(schema) || schema['type'] !== 'object') {
      throw new Error('The schema root must be an object.');
    }
    const properties = schema['properties'];
    if (properties !== undefined && !isRecord(properties)) {
      throw new Error('The schema has invalid properties.');
    }
    if (!properties || Object.keys(properties).length === 0) {
      throw new Error('The schema has no form fields.');
    }
    normalizeDialect(schema);
    assertRenderable(schema, 0);
    createFormAjv(schema as JsonSchema).compile(schema);
    return { schema: schema as JsonSchema, reason: null };
  } catch (error) {
    return {
      schema: null,
      reason: `A form cannot represent this schema: ${error instanceof Error ? error.message : 'unknown shape'}. Enter raw JSON.`,
    };
  }
}

function normalizeDialect(schema: Record<string, unknown>): void {
  const declared = schema['$schema'];
  if (declared === undefined) return;
  if (typeof declared !== 'string') throw new Error('The schema dialect is invalid.');
  const uri = declared.replace(/#$/, '');
  if (/^https?:\/\/json-schema\.org\/draft\/2020-12\/schema$/.test(uri)) {
    schema['$schema'] = 'https://json-schema.org/draft/2020-12/schema';
  } else if (/^https?:\/\/json-schema\.org\/draft\/2019-09\/schema$/.test(uri)) {
    schema['$schema'] = 'https://json-schema.org/draft/2019-09/schema';
  } else if (/^https?:\/\/json-schema\.org\/draft-07\/schema$/.test(uri)) {
    schema['$schema'] = 'http://json-schema.org/draft-07/schema#';
  } else {
    throw new Error('The schema dialect is unsupported by the form.');
  }
}

function assertRenderable(schema: Record<string, unknown>, depth: number): void {
  if (depth > 12) throw new Error('The schema is too deeply nested for the form.');
  const type = schema['type'];
  if (type !== undefined && !['object', 'array', 'string', 'number', 'integer', 'boolean'].includes(String(type))) {
    throw new Error('The schema uses a field type this form cannot display.');
  }
  if ('const' in schema) throw new Error('The schema uses a fixed value this form cannot display.');
  if (['anyOf', 'allOf', 'not', 'if', 'then', 'else', 'patternProperties', 'prefixItems', 'contains']
    .some(key => key in schema)) {
    throw new Error('The schema uses a structure this form cannot display.');
  }
  if ('oneOf' in schema) {
    const variants = schema['oneOf'];
    const enumChoices = Array.isArray(variants) && variants.length > 0 && variants.every(
      value => isRecord(value) && 'const' in value &&
        ['string', 'number', 'boolean'].includes(typeof value['const']),
    );
    if (!enumChoices) throw new Error('The schema uses a structure this form cannot display.');
  }
  if (isRecord(schema['additionalProperties']) || 'unevaluatedProperties' in schema) {
    throw new Error('The schema has dynamic properties this form cannot display.');
  }
  if (schema['type'] === 'array') {
    if (!isRecord(schema['items'])) throw new Error('Array items need one schema for the form.');
    assertRenderable(schema['items'], depth + 1);
  }
  if (isRecord(schema['properties'])) {
    for (const child of Object.values(schema['properties'])) {
      if (!isRecord(child)) throw new Error('The schema has an invalid property.');
      assertRenderable(child, depth + 1);
    }
  }
}

/**
 * Builds the initial item data from schema defaults, so schemas with
 * `default` values start populated like the previous custom form did.
 */
export function initialDataFor(schema: JsonSchema): Record<string, unknown> {
  const draft: Record<string, unknown> = {};
  const properties = isRecord(schema.properties) ? schema.properties : {};
  for (const [key, child] of Object.entries(properties)) {
    const value = defaultValueFor(child);
    if (value !== undefined) {
      draft[key] = value;
    }
  }
  return draft;
}

function defaultValueFor(schema: unknown): unknown {
  if (!isRecord(schema)) {
    return undefined;
  }
  if ('default' in schema) {
    return structuredClone(schema['default']);
  }
  const type = Array.isArray(schema['type']) ? schema['type'][0] : schema['type'];
  if (type === 'object') {
    const nested = initialDataFor(schema as JsonSchema);
    return Object.keys(nested).length ? nested : undefined;
  }
  return undefined;
}
