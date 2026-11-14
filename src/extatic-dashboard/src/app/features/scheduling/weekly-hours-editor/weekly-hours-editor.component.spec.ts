import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { WeeklyHoursEditorComponent } from './weekly-hours-editor.component';
import { AvailabilityRule } from '../scheduling.models';
import { addRange, validateRules } from '../scheduling.util';

describe('weekly hours helpers', () => {
  it('adds 09:00–17:00 to an empty day, then a later range after the last one', () => {
    let rules = addRange([], 1);
    expect(rules).toEqual([{ day_of_week: 1, start_time: '09:00', end_time: '17:00' }]);
    rules = addRange(rules, 1);
    expect(rules[1]).toEqual({ day_of_week: 1, start_time: '18:00', end_time: '19:00' });
    expect(validateRules(rules)).toEqual([]);
  });

  it('flags overlaps and inverted ranges, allows adjacent and midnight ends', () => {
    expect(validateRules([
      { day_of_week: 1, start_time: '09:00', end_time: '12:00' },
      { day_of_week: 1, start_time: '11:00', end_time: '13:00' },
    ])).toEqual(['Monday: 09:00–12:00 overlaps 11:00–13:00']);
    expect(validateRules([{ day_of_week: 2, start_time: '12:00', end_time: '09:00' }])[0]).toContain('ends before it starts');
    expect(validateRules([
      { day_of_week: 3, start_time: '09:00', end_time: '12:00' },
      { day_of_week: 3, start_time: '12:00', end_time: '24:00' },
      { day_of_week: 4, start_time: '22:00', end_time: '00:00' },
    ])).toEqual([]);
  });
});

describe('WeeklyHoursEditorComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [WeeklyHoursEditorComponent] }));

  function render(rules: AvailabilityRule[]) {
    const fixture = TestBed.createComponent(WeeklyHoursEditorComponent);
    fixture.componentRef.setInput('rules', rules);
    fixture.componentRef.setInput('timezone', 'Europe/Berlin');
    const emitted: AvailabilityRule[][] = [];
    fixture.componentInstance.rulesChange.subscribe(r => emitted.push(r));
    fixture.detectChanges();
    return { fixture, emitted, el: fixture.nativeElement as HTMLElement };
  }

  it('renders days Monday→Sunday with ranges, unavailable days, and the zone label', () => {
    const { el } = render([{ day_of_week: 1, start_time: '09:00', end_time: '17:00' }]);
    const rows = Array.from(el.querySelectorAll('[data-day]'));
    expect(rows.map(r => r.getAttribute('data-day'))).toEqual(['1', '2', '3', '4', '5', '6', '0']);
    expect(rows[0].querySelectorAll('[data-range]').length).toBe(1);
    expect(rows[1].textContent).toContain('Unavailable');
    expect(el.textContent).toContain('Europe/Berlin');
  });

  it('adds and removes ranges and emits the new rules', () => {
    const { fixture, emitted, el } = render([{ day_of_week: 1, start_time: '09:00', end_time: '12:00' }]);
    (el.querySelector('[aria-label="Add hours on Tuesday"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(emitted.at(-1)).toContainEqual({ day_of_week: 2, start_time: '09:00', end_time: '17:00' });
    expect(el.querySelector('[data-day="2"]')!.querySelectorAll('[data-range]').length).toBe(1);

    (el.querySelector('[aria-label="Remove Monday 09:00"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(emitted.at(-1)).toEqual([{ day_of_week: 2, start_time: '09:00', end_time: '17:00' }]);
    expect(el.querySelector('[data-day="1"]')!.textContent).toContain('Unavailable');
  });

  it('edits a time, maps 00:00 end to 24:00, and shows validation errors', () => {
    const { fixture, emitted, el } = render([
      { day_of_week: 1, start_time: '09:00', end_time: '12:00' },
      { day_of_week: 1, start_time: '13:00', end_time: '17:00' },
    ]);
    const start = el.querySelectorAll<HTMLInputElement>('[aria-label="Monday start"]')[1];
    start.value = '11:00';
    start.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.componentInstance.valid).toBe(false);
    expect(el.textContent).toContain('overlaps');

    const end = el.querySelectorAll<HTMLInputElement>('[aria-label="Monday end"]')[1];
    end.value = '00:00';
    end.dispatchEvent(new Event('change'));
    expect(emitted.at(-1)![1].end_time).toBe('24:00');
  });

  it('hides edit controls when disabled', () => {
    const fixture = TestBed.createComponent(WeeklyHoursEditorComponent);
    fixture.componentRef.setInput('rules', [{ day_of_week: 1, start_time: '09:00', end_time: '17:00' }]);
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Add hours');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Remove');
  });
});
