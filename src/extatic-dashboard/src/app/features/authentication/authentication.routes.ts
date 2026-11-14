import { Routes } from '@angular/router';

/**
 * Authentication routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'apps/:appSlug/authentication',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Authentication' },
 *   },
 *   { path: 'apps/:appSlug/authentication/new', ... },   // literal segments before parameterised ones
 *   { path: 'apps/:appSlug/authentication/:id', ... },
 */
export const AUTHENTICATION_ROUTES: Routes = [];
