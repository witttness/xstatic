import { Routes } from '@angular/router';

/** Members routes, spread into the shell's children by app.routes.ts. Literal segments before :id. */
export const MEMBERS_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/members',
    loadComponent: () => import('./members-list/members-list.component').then(m => m.MembersListComponent),
    data: { title: 'Members' },
  },
  {
    path: 'apps/:appSlug/members/plans',
    loadComponent: () => import('./plans-list/plans-list.component').then(m => m.MembershipPlansListComponent),
    data: { title: 'Membership Plans' },
  },
  {
    path: 'apps/:appSlug/members/plans/new',
    loadComponent: () => import('./plan-editor/plan-editor.component').then(m => m.MembershipPlanEditorComponent),
    data: { title: 'New Plan' },
  },
  {
    path: 'apps/:appSlug/members/plans/:planId',
    loadComponent: () => import('./plan-editor/plan-editor.component').then(m => m.MembershipPlanEditorComponent),
    data: { title: 'Edit Plan' },
  },
  {
    path: 'apps/:appSlug/members/settings',
    loadComponent: () => import('./member-settings/member-settings.component').then(m => m.MemberSettingsComponent),
    data: { title: 'Member Settings' },
  },
  {
    path: 'apps/:appSlug/members/:id',
    loadComponent: () => import('./member-detail/member-detail.component').then(m => m.MemberDetailComponent),
    data: { title: 'Member' },
  },
];
