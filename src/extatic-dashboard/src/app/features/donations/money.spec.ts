import { describe, expect, it } from 'vitest';
import { formatMoney, fromMinor, toMinor } from './money';

describe('money', () => {
  it('converts two-decimal major units exactly (no float drift)', () => {
    expect(toMinor('19.99', 'usd')).toBe(1999);
    expect(toMinor('0.29', 'eur')).toBe(29);
    expect(toMinor('12.5', 'usd')).toBe(1250);
    expect(toMinor('10', 'gbp')).toBe(1000);
    expect(toMinor('1,000.10', 'usd')).toBe(100010);
  });

  it('treats zero-decimal currencies 1:1', () => {
    expect(toMinor('500', 'jpy')).toBe(500);
    expect(toMinor('500', 'JPY')).toBe(500);
    expect(toMinor('5.5', 'jpy')).toBeNull();
    expect(fromMinor(500, 'krw')).toBe('500');
    expect(formatMoney(500, 'jpy')).toBe('500 JPY');
  });

  it('rejects invalid input', () => {
    for (const bad of ['', 'abc', '-1', '1.234', '1e3', '.5']) {
      expect(toMinor(bad, 'usd')).toBeNull();
    }
    expect(toMinor(null, 'usd')).toBeNull();
  });

  it('formats minor units', () => {
    expect(fromMinor(1999, 'usd')).toBe('19.99');
    expect(fromMinor(5, 'usd')).toBe('0.05');
    expect(formatMoney(100000, 'eur')).toBe('1000.00 EUR');
    expect(fromMinor(null, 'usd')).toBe('');
  });
});
