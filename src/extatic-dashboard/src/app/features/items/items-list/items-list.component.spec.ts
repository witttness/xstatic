import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ItemsListComponent } from './items-list.component';

describe('ItemsListComponent', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ItemsListComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
  });

  function open(canManage = true) {
    const fixture = TestBed.createComponent(ItemsListComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    fixture.componentRef.setInput('collectionSlug', 'posts');
    fixture.detectChanges();
    http.expectOne('/api/apps/app').flush({ can_manage_items: canManage });
    http.expectOne('/api/apps/app/collections/posts').flush({ name: 'Posts' });
    fixture.detectChanges();
    return fixture;
  }

  it('shows loading, empty, and error states', () => {
    const fixture = open();
    expect(fixture.nativeElement.textContent).toContain('Loading Items');
    http.expectOne('/api/apps/app/collections/posts/items?page=1&page_size=20').flush({ items: [], total: 0, page: 1, page_size: 20 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No Items');
    fixture.componentInstance.load();
    http.expectOne('/api/apps/app/collections/posts/items?page=1&page_size=20').flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to load Items');
  });

  it('pages 100 Items and hides write controls for a viewer', () => {
    const fixture = open(false);
    http.expectOne('/api/apps/app/collections/posts/items?page=1&page_size=20').flush({
      items: [{ id: 'first', data: { title: 'First item' }, created_at: '2026-01-01', updated_at: '2026-01-01' }],
      total: 100, page: 1, page_size: 20,
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('First item');
    expect(fixture.nativeElement.textContent).not.toContain('New Item');
    expect(fixture.nativeElement.textContent).not.toContain('Edit');
    fixture.componentInstance.go(2);
    http.expectOne('/api/apps/app/collections/posts/items?page=2&page_size=20').flush({ items: [], total: 100, page: 2, page_size: 20 });
    expect(fixture.componentInstance.page()).toBe(2);
  });
});
