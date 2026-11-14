import { describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { SearchSnippetComponent, decodeEntities, parseSnippet } from './search-snippet.component';

describe('parseSnippet', () => {
  it('splits on mark tags and decodes entities', () => {
    expect(parseSnippet('5 &lt; 6 &amp; <mark>hit</mark> &quot;x&quot; &#39;y&#39;')).toEqual([
      { text: '5 < 6 & ', mark: false },
      { text: 'hit', mark: true },
      { text: ' "x" \'y\'', mark: false },
    ]);
  });

  it('handles empty and missing snippets', () => {
    expect(parseSnippet('')).toEqual([]);
    expect(parseSnippet(null)).toEqual([]);
  });

  it('keeps unknown entities and decodes numeric ones', () => {
    expect(decodeEntities('&nbsp;&#233;&#x1F600;&#0;')).toBe('&nbsp;é😀&#0;');
  });
});

describe('SearchSnippetComponent', () => {
  function render(snippet: string): HTMLElement {
    TestBed.configureTestingModule({ imports: [SearchSnippetComponent] });
    const fixture = TestBed.createComponent(SearchSnippetComponent);
    fixture.componentRef.setInput('snippet', snippet);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders highlights as <mark> elements', () => {
    const el = render('before <mark>findme</mark> after');
    const marks = el.querySelectorAll('mark');
    expect(marks.length).toBe(1);
    expect(marks[0].textContent).toBe('findme');
    expect(el.textContent).toBe('before findme after');
  });

  it('never turns escaped or stray markup into elements', () => {
    const el = render('&lt;img src=x onerror=alert(1)&gt; <script>alert(2)</script> <b>bold</b> <mark>ok</mark>');
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('script')).toBeNull();
    expect(el.querySelector('b')).toBeNull();
    expect(el.querySelectorAll('mark').length).toBe(1);
    expect(el.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(el.textContent).toContain('<script>alert(2)</script>');
  });
});
