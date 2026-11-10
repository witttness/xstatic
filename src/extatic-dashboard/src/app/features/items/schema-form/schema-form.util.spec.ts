import { describe, expect, it } from 'vitest';
import { defaultFor, describeSchema } from './schema-form.util';

describe('describeSchema', () => {
  it('uses raw mode when a schema is absent or has unsupported composition', () => {
    expect(describeSchema(null).supported).toBe(false);
    expect(describeSchema('{"oneOf":[],"type":"object"}').supported).toBe(false);
    expect(describeSchema('{"type":"array","items":{"type":"string"}}').supported).toBe(false);
  });

  it('describes nested objects, arrays, required fields, and defaults', () => {
    const result = describeSchema(JSON.stringify({
      type: 'object', required: ['title'], properties: {
        title: { type: 'string', default: 'Untitled', description: 'Heading' },
        details: { type: 'object', properties: { active: { type: 'boolean' } } },
        tags: { type: 'array', items: { type: 'string' } },
      },
    }));
    expect(result.supported).toBe(true);
    if (!result.supported) return;
    expect(result.fields[0]).toMatchObject({ key: 'title', required: true, defaultValue: 'Untitled' });
    expect(result.fields[1].properties?.[0].key).toBe('active');
    expect(defaultFor(result.fields[2])).toEqual([]);
  });
});
