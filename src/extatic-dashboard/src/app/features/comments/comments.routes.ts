import { Routes } from '@angular/router';

/** Comments routes, spread into the shell's children by app.routes.ts. */
export const COMMENTS_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/comments',
    loadComponent: () =>
      import('./comments-queue/comments-queue.component').then(m => m.CommentsQueueComponent),
    data: { title: 'Comments' },
  },
  {
    path: 'apps/:appSlug/comments/pages',
    loadComponent: () =>
      import('./comments-pages/comments-pages.component').then(m => m.CommentsPagesComponent),
    data: { title: 'Comment pages' },
  },
  {
    path: 'apps/:appSlug/comments/settings',
    loadComponent: () =>
      import('./comments-settings/comments-settings.component').then(m => m.CommentsSettingsComponent),
    data: { title: 'Comment settings' },
  },
];
