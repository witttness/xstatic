import { Routes } from '@angular/router';

/**
 * Scheduling routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'apps/:appSlug/scheduling',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Scheduling' },
 *   },
 *   { path: 'apps/:appSlug/scheduling/new', ... },   // literal segments before parameterised ones
 *   { path: 'apps/:appSlug/scheduling/:id', ... },
 */
export const SCHEDULING_ROUTES: Routes = [];
