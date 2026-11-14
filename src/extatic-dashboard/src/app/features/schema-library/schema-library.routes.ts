import { Routes } from '@angular/router';
import { platformAdminGuard } from '../../core/guards/platform-admin.guard';

const list = () => import('./schema-list/schema-list.component').then(m => m.SchemaListComponent);
const editor = () => import('./schema-editor/schema-editor.component').then(m => m.SchemaEditorComponent);

/**
 * Schema Library: the global library (platform admins, `admin/schemas`) and each App's private
 * library (`apps/:appSlug/schemas`, owner/admin write, editors read). The API enforces both.
 */
export const SCHEMA_LIBRARY_ROUTES: Routes = [
  { path: 'admin/schemas', loadComponent: list, canActivate: [platformAdminGuard], data: { title: 'Schema Library' } },
  { path: 'admin/schemas/new', loadComponent: editor, canActivate: [platformAdminGuard], data: { title: 'New Schema' } },
  { path: 'admin/schemas/:id', loadComponent: editor, canActivate: [platformAdminGuard], data: { title: 'Schema' } },
  { path: 'apps/:appSlug/schemas', loadComponent: list, data: { title: 'Schemas' } },
  { path: 'apps/:appSlug/schemas/new', loadComponent: editor, data: { title: 'New Schema' } },
  { path: 'apps/:appSlug/schemas/:id', loadComponent: editor, data: { title: 'Schema' } },
];
