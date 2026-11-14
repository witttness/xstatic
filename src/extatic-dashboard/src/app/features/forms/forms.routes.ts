import { Routes } from '@angular/router';

/**
 * Forms routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'apps/:appSlug/forms',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Forms' },
 *   },
 *   { path: 'apps/:appSlug/forms/new', ... },   // literal segments before parameterised ones
 *   { path: 'apps/:appSlug/forms/:id', ... },
 */
export const FORMS_ROUTES: Routes = [];
