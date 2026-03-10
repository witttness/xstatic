import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { ShellComponent } from './layout/shell/shell.component';
import { UnauthorizedComponent } from './features/unauthorized/unauthorized.component';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'apps',
    pathMatch: 'full',
  },
  {
    path: 'unauthorized',
    component: UnauthorizedComponent,
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'apps',
        loadComponent: () =>
          import('./features/apps/apps-list/apps-list.component').then(m => m.AppsListComponent),
        data: { title: 'Apps' },
      },
      {
        path: 'apps/:appSlug',
        redirectTo: 'apps/:appSlug/collections',
        pathMatch: 'full',
      },
      {
        path: 'apps/:appSlug/collections',
        loadComponent: () =>
          import('./features/collections/collections-list/collections-list.component').then(m => m.CollectionsListComponent),
        data: { title: 'Collections' },
      },
      {
        path: 'apps/:appSlug/collections/new',
        loadComponent: () =>
          import('./features/collections/collection-form/collection-form.component').then(m => m.CollectionFormComponent),
        data: { title: 'New Collection' },
      },
      {
        path: 'apps/:appSlug/collections/:slug',
        loadComponent: () =>
          import('./features/collections/collection-form/collection-form.component').then(m => m.CollectionFormComponent),
        data: { title: 'Edit Collection' },
      },
      {
        path: 'apps/:appSlug/collaborators',
        loadComponent: () =>
          import('./features/collaborators/collaborators-list/collaborators-list.component').then(m => m.CollaboratorsListComponent),
        data: { title: 'Team' },
      },
      {
        path: 'apps/:appSlug/appusers',
        loadComponent: () =>
          import('./features/appusers/appusers-list/appusers-list.component').then(m => m.AppUsersListComponent),
        data: { title: 'Users' },
      },
      {
        path: 'apps/:appSlug/webhooks',
        loadComponent: () =>
          import('./features/webhooks/webhooks-list/webhooks-list.component').then(m => m.WebhooksListComponent),
        data: { title: 'Webhooks' },
      },
      {
        path: 'apps/:appSlug/webhooks/new',
        loadComponent: () =>
          import('./features/webhooks/webhook-form/webhook-form.component').then(m => m.WebhookFormComponent),
        data: { title: 'New Webhook' },
      },
      {
        path: 'apps/:appSlug/webhooks/:id',
        loadComponent: () =>
          import('./features/webhooks/webhook-detail/webhook-detail.component').then(m => m.WebhookDetailComponent),
        data: { title: 'Webhook' },
      },
      {
        path: 'apps/:appSlug/webhooks/:id/logs',
        loadComponent: () =>
          import('./features/webhooks/webhook-logs/webhook-logs.component').then(m => m.WebhookLogsComponent),
        data: { title: 'Webhook Logs' },
      },
      {
        path: 'apps/:appSlug/settings',
        loadComponent: () =>
          import('./features/apps/app-settings/app-settings.component').then(m => m.AppSettingsComponent),
        data: { title: 'Settings' },
      },
    ],
  },
  {
    path: '**',
    redirectTo: 'apps',
  },
];
