import { Routes } from '@angular/router';

/**
 * Comments routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'apps/:appSlug/comments',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Comments' },
 *   },
 *   { path: 'apps/:appSlug/comments/new', ... },   // literal segments before parameterised ones
 *   { path: 'apps/:appSlug/comments/:id', ... },
 */
export const COMMENTS_ROUTES: Routes = [];
