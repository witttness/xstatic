import { describe, expect, it } from 'vitest';
import { JsonSchema } from '@jsonforms/core';
import {
  initialDataFor,
  parseItemSchema,
} from './item-schema';

describe('parseItemSchema', () => {
  it('rejects missing, invalid, and non-object schemas with a reason', () => {
    expect(parseItemSchema(null).schema).toBeNull();
    expect(parseItemSchema(null).reason).toContain('no JSON Schema');
    expect(parseItemSchema('{bad').reason).toContain('A form cannot represent this schema');
    expect(parseItemSchema('{"type":"array","items":{"type":"string"}}').reason).toContain('root must be an object');
    expect(parseItemSchema('{"type":"object","properties":{}}').reason).toContain('no form fields');
    expect(parseItemSchema(JSON.stringify({
      $schema: 'http://json-schema.org/draft-06/schema#',
      type: 'object', properties: { title: { type: 'string' } },
    })).reason).toContain('dialect is unsupported');
    expect(parseItemSchema(JSON.stringify({
      type: 'object', properties: { title: { $ref: '#/$defs/missing' } },
    })).schema).toBeNull();
  });

  it('accepts object schemas with renderable properties and falls back for root composition', () => {
    const parsed = parseItemSchema(
      JSON.stringify({
        type: 'object',
        properties: { title: { type: 'string' } },
      }),
    );
    expect(parsed.schema).not.toBeNull();
    expect(parsed.reason).toBeNull();
    const combinator = parseItemSchema(
      JSON.stringify({
        type: 'object',
        oneOf: [{ title: 'A', properties: { a: { type: 'string' } } }, { title: 'B', properties: { b: { type: 'string' } } }],
      }),
    );
    expect(combinator.schema).toBeNull();
    expect(combinator.reason).toContain('no form fields');
    expect(parseItemSchema(JSON.stringify({
      type: 'object', properties: { details: { oneOf: [
        { type: 'object', properties: { a: { type: 'string' } } },
        { type: 'object', properties: { b: { type: 'string' } } },
      ] } },
    })).schema).toBeNull();
    expect(parseItemSchema(JSON.stringify({
      type: 'object', properties: { status: { oneOf: [
        { const: 'draft', title: 'Draft' }, { const: 'live', title: 'Live' },
      ] } },
    })).schema).not.toBeNull();
    expect(parseItemSchema(JSON.stringify({
      type: 'object', properties: { flag: { type: ['string', 'null'] } },
    })).schema).toBeNull();
  });

  it('recognizes draft 2020-12 and normalizes draft 7 aliases', () => {
    const modern = parseItemSchema(JSON.stringify({
      $schema: 'https://json-schema.org/draft/2020-12/schema',
      type: 'object', properties: { title: { type: 'string' } },
    }));
    expect(modern.schema?.$schema).toBe('https://json-schema.org/draft/2020-12/schema');
    const older = parseItemSchema(JSON.stringify({
      $schema: 'https://json-schema.org/draft-07/schema',
      type: 'object', properties: { title: { type: 'string' } },
    }));
    expect(older.schema?.$schema).toBe('http://json-schema.org/draft-07/schema#');
  });
});

describe('initialDataFor', () => {
  it('seeds schema defaults recursively without inventing values', () => {
    const schema: JsonSchema = {
      type: 'object',
      properties: {
        title: { type: 'string', default: 'Untitled' },
        count: { type: 'integer', default: 3 },
        nested: {
          type: 'object',
          properties: { flag: { type: 'boolean', default: true } },
        },
        plain: { type: 'string' },
        tags: { type: 'array', items: { type: 'string' }, default: ['a'] },
      },
    };
    expect(initialDataFor(schema)).toEqual({
      title: 'Untitled',
      count: 3,
      nested: { flag: true },
      tags: ['a'],
    });
  });
});
