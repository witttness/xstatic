import { Routes } from '@angular/router';

/** Donations routes, spread into the shell's children by app.routes.ts. Literal segments before parameterised ones. */
export const DONATIONS_ROUTES: Routes = [
  {
    path: 'apps/:appSlug/donations',
    loadComponent: () => import('./donations-overview/donations-overview.component').then(m => m.DonationsOverviewComponent),
    data: { title: 'Donations' },
  },
  {
    path: 'apps/:appSlug/donations/setup',
    loadComponent: () => import('./provider-setup/provider-setup.component').then(m => m.ProviderSetupComponent),
    data: { title: 'Payment provider' },
  },
  {
    path: 'apps/:appSlug/donations/campaigns',
    loadComponent: () => import('./campaigns-list/campaigns-list.component').then(m => m.CampaignsListComponent),
    data: { title: 'Donation campaigns' },
  },
  {
    path: 'apps/:appSlug/donations/campaigns/new',
    loadComponent: () => import('./campaign-editor/campaign-editor.component').then(m => m.CampaignEditorComponent),
    data: { title: 'New campaign' },
  },
  {
    path: 'apps/:appSlug/donations/campaigns/:id',
    loadComponent: () => import('./campaign-editor/campaign-editor.component').then(m => m.CampaignEditorComponent),
    data: { title: 'Edit campaign' },
  },
];
