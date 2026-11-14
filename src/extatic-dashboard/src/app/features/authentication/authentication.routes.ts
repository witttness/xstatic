import { Routes } from '@angular/router';

/** Authentication routes, spread into the shell's children by app.routes.ts. */
export const AUTHENTICATION_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/authentication',
    loadComponent: () => import('./auth-settings/auth-settings.component').then(m => m.AuthSettingsComponent),
    data: { title: 'Authentication' },
  },
];
