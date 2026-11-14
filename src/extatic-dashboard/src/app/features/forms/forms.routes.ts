import { Routes } from '@angular/router';

/** Forms routes, spread into the shell's children by app.routes.ts. Literal segments precede parameterised ones. */
export const FORMS_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/forms',
    loadComponent: () => import('./forms-list/forms-list.component').then(m => m.FormsListComponent),
    data: { title: 'Forms' },
  },
  {
    path: 'apps/:appSlug/forms/new',
    loadComponent: () => import('./form-editor/form-editor.component').then(m => m.FormEditorComponent),
    data: { title: 'New Form' },
  },
  {
    path: 'apps/:appSlug/forms/:id/submissions',
    loadComponent: () => import('./form-submissions/form-submissions.component').then(m => m.FormSubmissionsComponent),
    data: { title: 'Form Submissions' },
  },
  {
    path: 'apps/:appSlug/forms/:id/submissions/:submissionId',
    loadComponent: () => import('./submission-detail/submission-detail.component').then(m => m.SubmissionDetailComponent),
    data: { title: 'Submission' },
  },
  {
    path: 'apps/:appSlug/forms/:id',
    loadComponent: () => import('./form-editor/form-editor.component').then(m => m.FormEditorComponent),
    data: { title: 'Edit Form' },
  },
];
