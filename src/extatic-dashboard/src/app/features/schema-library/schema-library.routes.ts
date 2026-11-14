import { Routes } from '@angular/router';

/**
 * Schema Library routes, spread into the shell's children by app.routes.ts (already wired up —
 * do not edit app.routes.ts; add routes here).
 *
 * Paths are children of the shell, for example:
 *   {
 *     path: 'admin/schemas',
 *     loadComponent: () => import('./x/x.component').then(m => m.XComponent),
 *     data: { title: 'Schema Library' },
 *   },
 *   { path: 'admin/schemas/new', ... },   // literal segments before parameterised ones
 *   { path: 'admin/schemas/:id', ... },
 *
 * Platform-admin only and NOT app-scoped: add `canActivate: [platformAdminGuard]`
 * (core/guards/platform-admin.guard.ts) to each route. The API enforces PolicyNames.PlatformAdmin regardless.
 */
export const SCHEMA_LIBRARY_ROUTES: Routes = [];
