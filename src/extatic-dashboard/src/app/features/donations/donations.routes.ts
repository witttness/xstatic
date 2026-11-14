import { Routes } from '@angular/router';

/**
 * Donations routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'apps/:appSlug/donations',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Donations' },
 *   },
 *   { path: 'apps/:appSlug/donations/new', ... },   // literal segments before parameterised ones
 *   { path: 'apps/:appSlug/donations/:id', ... },
 */
export const DONATIONS_ROUTES: Routes = [];
