import { Routes } from '@angular/router';

/**
 * Scheduling routes, spread into the shell's children by app.routes.ts.
 * Literal segments come before parameterised ones.
 */
export const SCHEDULING_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/scheduling',
    loadComponent: () => import('./appointments-page/appointments-page.component').then(m => m.AppointmentsPageComponent),
    data: { title: 'Scheduling' },
  },
  {
    path: 'apps/:appSlug/scheduling/book',
    loadComponent: () => import('./manual-booking/manual-booking.component').then(m => m.ManualBookingComponent),
    data: { title: 'New booking' },
  },
  {
    path: 'apps/:appSlug/scheduling/calendars',
    loadComponent: () => import('./calendars-list/calendars-list.component').then(m => m.CalendarsListComponent),
    data: { title: 'Calendars' },
  },
  {
    path: 'apps/:appSlug/scheduling/calendars/new',
    loadComponent: () => import('./calendar-editor/calendar-editor.component').then(m => m.CalendarEditorComponent),
    data: { title: 'New calendar' },
  },
  {
    path: 'apps/:appSlug/scheduling/calendars/:id',
    loadComponent: () => import('./calendar-editor/calendar-editor.component').then(m => m.CalendarEditorComponent),
    data: { title: 'Calendar' },
  },
  {
    path: 'apps/:appSlug/scheduling/types',
    loadComponent: () => import('./types-list/types-list.component').then(m => m.TypesListComponent),
    data: { title: 'Appointment types' },
  },
  {
    path: 'apps/:appSlug/scheduling/types/new',
    loadComponent: () => import('./type-editor/type-editor.component').then(m => m.TypeEditorComponent),
    data: { title: 'New appointment type' },
  },
  {
    path: 'apps/:appSlug/scheduling/types/:id',
    loadComponent: () => import('./type-editor/type-editor.component').then(m => m.TypeEditorComponent),
    data: { title: 'Appointment type' },
  },
  {
    path: 'apps/:appSlug/scheduling/integration',
    loadComponent: () => import('./integration/integration.component').then(m => m.IntegrationComponent),
    data: { title: 'Scheduling integration' },
  },
];
