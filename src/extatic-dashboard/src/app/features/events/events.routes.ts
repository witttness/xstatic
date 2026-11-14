import { Routes } from '@angular/router';

/** Events routes, spread into the shell's children by app.routes.ts. */
export const EVENTS_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/events',
    loadComponent: () => import('./events-list/events-list.component').then(m => m.EventsListComponent),
    data: { title: 'Events' },
  },
  {
    path: 'apps/:appSlug/events/new',
    loadComponent: () => import('./event-editor/event-editor.component').then(m => m.EventEditorComponent),
    data: { title: 'New Event' },
  },
  {
    path: 'apps/:appSlug/events/:id/edit',
    loadComponent: () => import('./event-editor/event-editor.component').then(m => m.EventEditorComponent),
    data: { title: 'Edit Event' },
  },
  {
    path: 'apps/:appSlug/events/:id',
    loadComponent: () => import('./event-detail/event-detail.component').then(m => m.EventDetailComponent),
    data: { title: 'Event' },
  },
];
