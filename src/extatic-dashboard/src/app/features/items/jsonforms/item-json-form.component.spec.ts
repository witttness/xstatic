import { describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { JsonSchema } from '@jsonforms/core';
import { ItemJsonFormComponent } from './item-json-form.component';

const schema: JsonSchema = {
  type: 'object',
  properties: {
    title: { type: 'string', title: 'Title' },
    count: { type: 'integer', title: 'Count' },
    active: { type: 'boolean', title: 'Active' },
    status: { type: 'string', enum: ['draft', 'published'], title: 'Status' },
    priority: { type: 'integer', enum: [1, 2], title: 'Priority' },
    visibility: { oneOf: [{ const: 'private', title: 'Private' }, { const: 'public', title: 'Public' }], title: 'Visibility' },
    publishDate: { type: 'string', format: 'date', title: 'Publish date' },
    tags: { type: 'array', items: { type: 'string' }, title: 'Tags' },
    authors: { type: 'array', items: { type: 'object', properties: { name: { type: 'string' } } }, title: 'Authors' },
    details: { type: 'object', properties: { summary: { type: 'string', title: 'Summary' } } },
  },
  required: ['title'],
};

describe('ItemJsonFormComponent', () => {
  function create(data?: unknown, selectedSchema: JsonSchema = schema) {
    const fixture = TestBed.createComponent(ItemJsonFormComponent);
    fixture.componentRef.setInput('schema', selectedSchema);
    if (data !== undefined) fixture.componentRef.setInput('data', data);
    // Mirror the editor's wiring: feed emitted data back into the form.
    fixture.componentInstance.dataChange.subscribe(value =>
      fixture.componentRef.setInput('data', value),
    );
    fixture.detectChanges();
    return fixture;
  }

  it('renders controls for each schema type', () => {
    const fixture = create({ title: 'Hello' });
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Title');
    expect(element.querySelector('input[type="text"]')).not.toBeNull();
    expect(element.querySelector('input[type="number"]')).not.toBeNull();
    expect(element.querySelector('input[type="checkbox"]')).not.toBeNull();
    expect(element.querySelector('select')).not.toBeNull();
    expect(element.querySelector('input[type="date"]')).not.toBeNull();
    expect(element.textContent).toContain('Summary');
    expect(element.textContent).toContain('No data');
  });

  it('emits changed data and preserves untouched properties', () => {
    const fixture = create({ title: 'Hello', extra: 7 });
    const emitted: unknown[] = [];
    fixture.componentInstance.dataChange.subscribe(value => emitted.push(value));
    const input = fixture.nativeElement.querySelector('input[type="text"]');
    input.value = 'Changed';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const last = emitted.at(-1) as Record<string, unknown>;
    expect(last['title']).toBe('Changed');
    expect(last['extra']).toBe(7);
  });

  it('selects typed enum values and boolean checkboxes', () => {
    const fixture = create({ title: 'Hello' });
    let latest: Record<string, unknown> | undefined;
    fixture.componentInstance.dataChange.subscribe(
      value => (latest = value as Record<string, unknown>),
    );
    const select = fixture.nativeElement.querySelector('select');
    select.value = 'published';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(latest?.['status']).toBe('published');
    const checkbox = fixture.nativeElement.querySelector(
      'input[type="checkbox"]',
    );
    checkbox.click();
    fixture.detectChanges();
    expect(latest?.['active']).toBe(true);
    const selects = Array.from(fixture.nativeElement.querySelectorAll('select')) as HTMLSelectElement[];
    const priority = selects.find(select => Array.from(select.options).some(option => option.value === '2'))!;
    priority.value = '2';
    priority.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(latest?.['priority']).toBe(2);
    const visibility = selects.find(select => Array.from(select.options).some(option => option.value === 'public'))!;
    visibility.value = 'public';
    visibility.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(latest?.['visibility']).toBe('public');
  });

  it('adds, edits, and removes primitive array items', () => {
    const fixture = create({ title: 'Hello' });
    let latest: unknown;
    fixture.componentInstance.dataChange.subscribe(value => (latest = value));
    const buttons = (): HTMLButtonElement[] =>
      Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const add = buttons().find(button => button.textContent?.includes('Add to Tags'));
    expect(add).toBeDefined();
    add!.click();
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('tbody input');
    expect(input).not.toBeNull();
    input.value = 'first';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect((latest as Record<string, unknown>)['tags']).toEqual(['first']);
    const remove = buttons().find(button => button.textContent?.includes('Delete'));
    remove!.click();
    fixture.detectChanges();
    expect((latest as Record<string, unknown>)['tags']).toEqual([]);
  });

  it('adds and edits object array items', () => {
    const fixture = create({ title: 'Hello' });
    let latest: Record<string, unknown> = {};
    fixture.componentInstance.dataChange.subscribe(value => latest = value as Record<string, unknown>);
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    buttons.find(button => button.textContent?.includes('Add to Authors'))!.click();
    fixture.detectChanges();
    const table = Array.from(fixture.nativeElement.querySelectorAll('table'))
      .find((element: unknown) => (element as HTMLElement).textContent?.includes('Name')) as HTMLTableElement;
    expect(table).toBeDefined();
    const name = table.querySelector('tbody input') as HTMLInputElement;
    name.value = 'Ada';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(latest['authors']).toEqual([{ name: 'Ada' }]);
  });

  it('shows server errors inline next to the affected field', () => {
    const fixture = create({ title: 'Hello' });
    fixture.componentRef.setInput('additionalErrors', [
      { instancePath: '/title', schemaPath: '', keyword: 'server', params: {}, message: 'Too short' },
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Too short');
  });

  it('renders client-side validation errors from the schema', () => {
    const fixture = create({ title: 'Hello' });
    const input = fixture.nativeElement.querySelector('input[type="text"]');
    input.value = '';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('is a required property');
  });

  it('renders and validates a draft 2020-12 schema', () => {
    const draft2020: JsonSchema = {
      $schema: 'https://json-schema.org/draft/2020-12/schema',
      type: 'object',
      properties: { title: { type: 'string', minLength: 3 } },
      required: ['title'],
    };
    const fixture = create({ title: 'valid' }, draft2020);
    const title = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    expect(title).not.toBeNull();
    title.value = 'x';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('must NOT have fewer than 3 characters');
  });

  it('renders a draft 2019-09 schema', () => {
    const draft2019: JsonSchema = {
      $schema: 'https://json-schema.org/draft/2019-09/schema',
      type: 'object',
      properties: { title: { type: 'string' } },
    };
    const fixture = create({ title: 'valid' }, draft2019);
    expect(fixture.nativeElement.querySelector('input[type="text"]')).not.toBeNull();
  });
});
