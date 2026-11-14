import { Routes } from '@angular/router';

/** Search routes, spread into the shell's children by app.routes.ts. */
export const SEARCH_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/search',
    loadComponent: () => import('./search-page.component').then(m => m.SearchPageComponent),
    data: { title: 'Search' },
  },
];
