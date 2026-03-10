import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { Collaborator, CollaboratorRole, CollaboratorRoleLabel } from '../../../core/models/collaborator.model';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { BadgeComponent } from '../../../shared/badge/badge.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';

@Component({
  selector: 'app-collaborators-list',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    ButtonComponent,
    BadgeComponent,
    LoadingSpinnerComponent,
    EmptyStateComponent,
    ConfirmModalComponent,
  ],
  template: `
    <app-page-header title="Team" subtitle="Collaborators on this app.">
      <app-button (click)="showInvite = true">Invite</app-button>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else if (error()) {
      <p class="text-error text-sm">{{ error() }}</p>
    } @else if (collaborators().length === 0) {
      <app-empty-state message="No collaborators yet.">
        <app-button (click)="showInvite = true">Invite</app-button>
      </app-empty-state>
    } @else {
      <div class="border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border">
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Email</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Name</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Role</th>
              <th class="px-4 py-3 text-left text-xs font-medium text-text-muted uppercase tracking-wider">Status</th>
              <th class="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody>
            @for (c of collaborators(); track c.id) {
              <tr class="border-b border-border hover:bg-surface">
                <td class="px-4 py-3 text-text-primary">{{ c.user_email }}</td>
                <td class="px-4 py-3 text-text-muted text-xs">{{ c.user_name }}</td>
                <td class="px-4 py-3">
                  @if (c.accepted_at) {
                    <select
                      [value]="c.role"
                      (change)="updateRole(c, +($any($event.target).value))"
                      class="bg-background border border-border text-sm text-text-primary px-2 py-1 focus:border-accent focus:outline-none"
                    >
                      <option [value]="0">Viewer</option>
                      <option [value]="1">Editor</option>
                      <option [value]="2">Admin</option>
                    </select>
                  } @else {
                    <app-badge variant="muted">{{ roleLabel(c.role) }}</app-badge>
                  }
                </td>
                <td class="px-4 py-3">
                  @if (c.accepted_at) {
                    <app-badge variant="success">Accepted</app-badge>
                  } @else {
                    <app-badge variant="warning">Pending</app-badge>
                  }
                </td>
                <td class="px-4 py-3 text-right">
                  <button type="button" (click)="removingCollaborator.set(c)"
                    class="text-xs text-error hover:text-red-400">Remove</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    <!-- Invite Modal -->
    @if (showInvite) {
      <div class="fixed inset-0 z-50 flex items-center justify-center">
        <div class="absolute inset-0 bg-black/60" (click)="closeInvite()"></div>
        <div class="relative bg-surface border border-border w-full max-w-md p-6 z-10">
          <h3 class="text-base font-semibold text-text-primary mb-4">Invite Collaborator</h3>
          <form [formGroup]="inviteForm" (ngSubmit)="onInvite()">
            <div class="mb-4">
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Email</label>
              <input formControlName="email" type="email"
                class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
            </div>
            <div class="mb-6">
              <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Role</label>
              <select formControlName="role"
                class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none">
                <option [value]="0">Viewer</option>
                <option [value]="1">Editor</option>
                <option [value]="2">Admin</option>
              </select>
            </div>
            @if (inviteError()) { <p class="text-xs text-error mb-3">{{ inviteError() }}</p> }
            <div class="flex justify-end gap-2">
              <app-button variant="secondary" type="button" (click)="closeInvite()">Cancel</app-button>
              <app-button type="submit" [loading]="inviting()">Send Invite</app-button>
            </div>
          </form>
        </div>
      </div>
    }

    @if (removingCollaborator()) {
      <app-confirm-modal
        title="Remove Collaborator"
        [message]="'Remove ' + removingCollaborator()!.user_email + ' from this app?'"
        confirmLabel="Remove"
        (confirmed)="onRemove()"
        (cancelled)="removingCollaborator.set(null)"
      />
    }
  `,
})
export class CollaboratorsListComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);
  private fb = inject(FormBuilder);

  collaborators = signal<Collaborator[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  removingCollaborator = signal<Collaborator | null>(null);

  showInvite = false;
  inviting = signal(false);
  inviteError = signal<string | null>(null);

  inviteForm = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    role: [0 as CollaboratorRole, Validators.required],
  });

  ngOnInit() {
    this.api.get<Collaborator[]>(`/apps/${this.appSlug}/collaborators`).subscribe({
      next: cs => { this.collaborators.set(cs); this.loading.set(false); },
      error: () => { this.error.set('Failed to load collaborators.'); this.loading.set(false); },
    });
  }

  roleLabel(role: CollaboratorRole): string {
    return CollaboratorRoleLabel[role];
  }

  closeInvite() {
    this.showInvite = false;
    this.inviteForm.reset({ email: '', role: 0 });
    this.inviteError.set(null);
  }

  onInvite() {
    if (this.inviteForm.invalid) { this.inviteForm.markAllAsTouched(); return; }
    this.inviting.set(true);
    this.inviteError.set(null);
    const { email, role } = this.inviteForm.value;
    this.api.post<Collaborator>(`/apps/${this.appSlug}/collaborators`, { email, role: Number(role) }).subscribe({
      next: c => {
        this.collaborators.update(cs => [...cs, c]);
        this.inviting.set(false);
        this.closeInvite();
      },
      error: () => { this.inviteError.set('Failed to send invite.'); this.inviting.set(false); },
    });
  }

  updateRole(c: Collaborator, role: number) {
    this.api.put<Collaborator>(`/apps/${this.appSlug}/collaborators/${c.id}/role`, { role }).subscribe({
      next: updated => this.collaborators.update(cs => cs.map(x => x.id === updated.id ? updated : x)),
      error: () => {},
    });
  }

  onRemove() {
    const c = this.removingCollaborator()!;
    this.removingCollaborator.set(null);
    this.api.delete<void>(`/apps/${this.appSlug}/collaborators/${c.id}`).subscribe({
      next: () => this.collaborators.update(cs => cs.filter(x => x.id !== c.id)),
      error: () => {},
    });
  }
}
