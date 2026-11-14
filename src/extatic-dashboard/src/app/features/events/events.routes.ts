import { Routes } from '@angular/router';

/**
 * Events routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'apps/:appSlug/events',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Events' },
 *   },
 *   { path: 'apps/:appSlug/events/new', ... },   // literal segments before parameterised ones
 *   { path: 'apps/:appSlug/events/:id', ... },
 */
export const EVENTS_ROUTES: Routes = [];
