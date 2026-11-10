import { describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { SchemaFormComponent } from './schema-form.component';
import { describeSchema } from './schema-form.util';

describe('SchemaFormComponent', () => {
  it('renders nested controls and permits array add and remove without dropping other data', () => {
    TestBed.configureTestingModule({ imports: [SchemaFormComponent] });
    const schema = describeSchema(JSON.stringify({ type: 'object', properties: {
      details: { type: 'object', properties: { summary: { type: 'string' } } },
      tags: { type: 'array', items: { type: 'string' } },
    } }));
    expect(schema.supported).toBe(true);
    if (!schema.supported) return;
    const fixture = TestBed.createComponent(SchemaFormComponent);
    fixture.componentRef.setInput('fields', schema.fields);
    fixture.componentRef.setInput('draft', { details: { summary: 'Hello' }, extra: 4, tags: [] });
    fixture.componentInstance.draftChange.subscribe(value => fixture.componentRef.setInput('draft', value));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#field-details-summary')).not.toBeNull();
    const add = Array.from(fixture.nativeElement.querySelectorAll('button')).find((button: unknown) => (button as HTMLButtonElement).textContent?.includes('Add tags')) as HTMLButtonElement;
    add.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.draft['extra']).toBe(4);
    expect(fixture.componentInstance.draft['tags']).toEqual(['']);
    const remove = Array.from(fixture.nativeElement.querySelectorAll('button')).find((button: unknown) => (button as HTMLButtonElement).textContent?.includes('Remove')) as HTMLButtonElement;
    remove.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.draft['tags']).toEqual([]);
  });
});
