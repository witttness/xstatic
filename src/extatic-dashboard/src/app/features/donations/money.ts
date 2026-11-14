/**
 * Money helpers. The API stores integer minor units; the dashboard lets people type major units.
 * Conversion is done on the decimal STRING (never via floating-point multiplication), so
 * "19.99" → 1999 exactly and zero-decimal currencies (JPY, KRW, ...) map 1:1.
 * Keep the currency lists in sync with Features/Donations/Currencies.cs.
 */
export const ZERO_DECIMAL_CURRENCIES: ReadonlySet<string> = new Set([
  'bif', 'clp', 'djf', 'gnf', 'jpy', 'kmf', 'krw', 'mga', 'pyg', 'rwf', 'vnd', 'vuv', 'xaf', 'xof', 'xpf',
]);

export const TWO_DECIMAL_CURRENCIES: ReadonlySet<string> = new Set([
  'aed', 'ars', 'aud', 'bdt', 'bgn', 'brl', 'cad', 'chf', 'cny', 'cop', 'czk', 'dkk', 'egp', 'eur',
  'gbp', 'ghs', 'hkd', 'idr', 'ils', 'inr', 'kes', 'lkr', 'mad', 'mxn', 'myr', 'ngn', 'nok', 'nzd',
  'pen', 'php', 'pkr', 'pln', 'qar', 'ron', 'rsd', 'sar', 'sek', 'sgd', 'thb', 'try', 'uah', 'usd',
  'uyu', 'zar',
]);

export const SUPPORTED_CURRENCIES: string[] = [...ZERO_DECIMAL_CURRENCIES, ...TWO_DECIMAL_CURRENCIES].sort();

export function currencyDecimals(currency: string): number {
  return ZERO_DECIMAL_CURRENCIES.has(currency.toLowerCase()) ? 0 : 2;
}

/**
 * "12.5" usd → 1250; "500" jpy → 500. Returns null for anything that isn't a non-negative amount
 * with at most the currency's number of decimals (e.g. "5.5" jpy, "1.234" usd, "1e3", "-1").
 */
export function toMinor(major: string | number | null | undefined, currency: string): number | null {
  if (major === null || major === undefined) return null;
  const text = String(major).trim().replace(/,/g, '');
  const decimals = currencyDecimals(currency);
  const pattern = decimals === 0 ? /^\d+$/ : /^\d+(\.\d{1,2})?$/;
  if (!pattern.test(text)) return null;
  const [whole, fraction = ''] = text.split('.');
  const minor = Number(whole) * 10 ** decimals + Number((fraction + '00').slice(0, decimals) || '0');
  return Number.isSafeInteger(minor) ? minor : null;
}

/** 1250 usd → "12.50"; 500 jpy → "500". No grouping, suitable for inputs. */
export function fromMinor(minor: number | null | undefined, currency: string): string {
  if (minor === null || minor === undefined) return '';
  const decimals = currencyDecimals(currency);
  if (decimals === 0) return String(minor);
  const sign = minor < 0 ? '-' : '';
  const abs = Math.abs(minor);
  return `${sign}${Math.trunc(abs / 100)}.${String(abs % 100).padStart(2, '0')}`;
}

/** Display string, e.g. "12.50 USD". */
export function formatMoney(minor: number, currency: string): string {
  return `${fromMinor(minor, currency)} ${currency.toUpperCase()}`;
}
