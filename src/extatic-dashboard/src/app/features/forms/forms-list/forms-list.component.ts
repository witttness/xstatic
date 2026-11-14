import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { Form } from '../forms.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';

@Component({
  selector: 'app-forms-list',
  standalone: true,
  imports: [
    RouterLink,
    PageHeaderComponent,
    ButtonComponent,
    BadgeComponent,
    LoadingSpinnerComponent,
    EmptyStateComponent,
    ConfirmModalComponent,
  ],
  template: `
    <app-page-header title="Forms" subtitle="Collect submissions from plain HTML forms or fetch().">
      <a [routerLink]="['/apps', appSlug, 'forms', 'new']">
        <app-button>New Form</app-button>
      </a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (forms().length === 0) {
      <app-empty-state message="No forms yet." description="Create a form to accept contact, signup or feedback submissions.">
        <a [routerLink]="['/apps', appSlug, 'forms', 'new']">
          <app-button>New Form</app-button>
        </a>
      </app-empty-state>
    } @else {
      <div class="border border-border overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Name</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Slug</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Submissions</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (f of forms(); track f.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3 text-text-primary">
                  <a [routerLink]="['/apps', appSlug, 'forms', f.id, 'submissions']" class="hover:underline">{{ f.name }}</a>
                </td>
                <td class="px-4 py-3"><code class="font-mono text-xs text-text-muted">{{ f.slug }}</code></td>
                <td class="px-4 py-3">
                  <span class="text-text-primary" data-testid="count">{{ f.submission_count }}</span>
                  @if (f.unread_count > 0) {
                    <app-badge variant="info" class="ml-2">{{ f.unread_count }} unread</app-badge>
                  }
                </td>
                <td class="px-4 py-3">
                  @if (f.is_active) {
                    <app-badge variant="success">Active</app-badge>
                  } @else {
                    <app-badge variant="muted">Inactive</app-badge>
                  }
                </td>
                <td class="px-4 py-3 text-right">
                  <div class="flex items-center justify-end gap-3">
                    <a [routerLink]="['/apps', appSlug, 'forms', f.id, 'submissions']"
                      class="text-xs text-text-muted hover:text-text-primary">Submissions</a>
                    <a [routerLink]="['/apps', appSlug, 'forms', f.id]"
                      class="text-xs text-text-muted hover:text-text-primary">Edit</a>
                    <button type="button" (click)="deleting.set(f)"
                      class="text-xs text-error hover:text-red-400">Delete</button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (deleting()) {
      <app-confirm-modal
        title="Delete Form"
        [message]="'Delete form ' + deleting()!.name + ' and all of its submissions? This cannot be undone.'"
        [confirmWord]="deleting()!.slug"
        confirmLabel="Delete"
        (confirmed)="onDelete()"
        (cancelled)="deleting.set(null)"
      />
    }
  `,
})
export class FormsListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  forms = signal<Form[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  deleting = signal<Form | null>(null);

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<Form[]>(`/apps/${this.appSlug}/forms`).subscribe({
      next: fs => { this.forms.set(fs); this.loading.set(false); },
      error: () => { this.error.set('Failed to load forms.'); this.loading.set(false); },
    });
  }

  onDelete() {
    const f = this.deleting()!;
    this.deleting.set(null);
    this.api.delete<void>(`/apps/${this.appSlug}/forms/${f.id}`).subscribe({
      next: () => this.forms.update(fs => fs.filter(x => x.id !== f.id)),
      error: () => this.error.set('Failed to delete form.'),
    });
  }
}
