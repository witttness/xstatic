import { Component, Input } from '@angular/core';

export interface DiffLine {
  type: 'same' | 'add' | 'del';
  text: string;
}

const MAX_CELLS = 4_000_000;

/** Simple LCS line diff (old → new). Falls back to "all removed, all added" for very large inputs. */
export function lineDiff(oldText: string, newText: string): DiffLine[] {
  const a = oldText.split('\n');
  const b = newText.split('\n');
  if (a.length * b.length > MAX_CELLS) {
    return [...a.map(text => ({ type: 'del' as const, text })), ...b.map(text => ({ type: 'add' as const, text }))];
  }
  const lcs: number[][] = Array.from({ length: a.length + 1 }, () => new Array<number>(b.length + 1).fill(0));
  for (let i = a.length - 1; i >= 0; i--) {
    for (let j = b.length - 1; j >= 0; j--) {
      lcs[i][j] = a[i] === b[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
    }
  }
  const out: DiffLine[] = [];
  let i = 0;
  let j = 0;
  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) { out.push({ type: 'same', text: a[i] }); i++; j++; }
    else if (lcs[i + 1][j] >= lcs[i][j + 1]) { out.push({ type: 'del', text: a[i++] }); }
    else { out.push({ type: 'add', text: b[j++] }); }
  }
  while (i < a.length) out.push({ type: 'del', text: a[i++] });
  while (j < b.length) out.push({ type: 'add', text: b[j++] });
  return out;
}

@Component({
  selector: 'app-line-diff',
  standalone: true,
  template: `
    <pre class="border border-border bg-background text-xs font-mono overflow-auto max-h-96 p-2">@for (line of lines; track $index) {<div [class]="cls(line)">{{ prefix(line) }} {{ line.text }}</div>}</pre>
    @if (unchanged) { <p class="text-xs text-text-muted mt-1">No differences.</p> }
  `,
})
export class LineDiffComponent {
  @Input() lines: DiffLine[] = [];

  get unchanged() { return this.lines.every(l => l.type === 'same'); }

  prefix(line: DiffLine) { return line.type === 'add' ? '+' : line.type === 'del' ? '-' : ' '; }

  cls(line: DiffLine) {
    return line.type === 'add' ? 'text-success bg-success/10'
      : line.type === 'del' ? 'text-error bg-error/10'
      : 'text-text-muted';
  }
}
