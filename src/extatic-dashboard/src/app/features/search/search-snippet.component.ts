import { Component, computed, input } from '@angular/core';

export interface SnippetSegment {
  text: string;
  mark: boolean;
}

const ENTITY = /&(#x[0-9a-fA-F]{1,6}|#[0-9]{1,7}|lt|gt|amp|quot|apos);/g;
const NAMED: Record<string, string> = { lt: '<', gt: '>', amp: '&', quot: '"', apos: "'" };

/** Decodes the entities the API's HTML encoder produces. Unknown entities are left as typed. */
export function decodeEntities(value: string): string {
  return value.replace(ENTITY, (match, entity: string) => {
    if (entity[0] !== '#') return NAMED[entity] ?? match;
    const code = entity[1] === 'x' ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10);
    return Number.isFinite(code) && code > 0 && code <= 0x10ffff ? String.fromCodePoint(code) : match;
  });
}

/**
 * Splits an API snippet (escaped text + <mark> tags) into plain-text segments. The result is only
 * ever rendered as text nodes, so even a snippet containing other markup cannot inject HTML.
 */
export function parseSnippet(html: string | null | undefined): SnippetSegment[] {
  const segments: SnippetSegment[] = [];
  let mark = false;
  for (const part of (html ?? '').split(/(<\/?mark>)/)) {
    if (part === '<mark>') { mark = true; continue; }
    if (part === '</mark>') { mark = false; continue; }
    if (part) segments.push({ text: decodeEntities(part), mark });
  }
  return segments;
}

/** Renders a search snippet safely: no innerHTML, only text nodes inside <mark> elements. */
@Component({
  selector: 'app-search-snippet',
  standalone: true,
  template: `@for (segment of segments(); track $index) {@if (segment.mark) {<mark class="bg-warning/30 text-text-primary px-0.5">{{ segment.text }}</mark>} @else {<span>{{ segment.text }}</span>}}`,
})
export class SearchSnippetComponent {
  snippet = input<string | null | undefined>('');
  segments = computed(() => parseSnippet(this.snippet()));
}
