import { Routes } from '@angular/router';

/**
 * Members routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'apps/:appSlug/members',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Members' },
 *   },
 *   { path: 'apps/:appSlug/members/new', ... },   // literal segments before parameterised ones
 *   { path: 'apps/:appSlug/members/:id', ... },
 */
export const MEMBERS_ROUTES: Routes = [];
