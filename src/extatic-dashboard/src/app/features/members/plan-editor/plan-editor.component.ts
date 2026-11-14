import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { MembershipPlan, MembershipPlanRequest } from '../members.model';

interface PlanForm {
  name: string;
  slug: string;
  description: string;
  price_label: string;
  duration_days: number | null;
  requires_approval: boolean;
  self_signup_enabled: boolean;
  max_members: number | null;
  benefits: string;
  is_active: boolean;
  sort_order: number;
}

export function slugify(value: string): string {
  return value.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 100);
}

@Component({
  selector: 'app-membership-plan-editor',
  standalone: true,
  imports: [FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent],
  template: `
    <a [routerLink]="['/apps', appSlug, 'members', 'plans']" class="text-xs text-text-muted hover:text-text-primary">&larr; Plans</a>
    <app-page-header [title]="planId ? 'Edit plan' : 'New plan'" />

    @if (loading()) {
      <app-loading-spinner />
    } @else {
      <form (ngSubmit)="save()" class="max-w-lg space-y-5">
        <div>
          <label for="plan-name" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Name</label>
          <input id="plan-name" name="name" [(ngModel)]="form.name" (ngModelChange)="onNameChange()"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          @if (errors()['name']) { <p class="text-xs text-error mt-1">{{ errors()['name'] }}</p> }
        </div>
        <div>
          <label for="plan-slug" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Slug</label>
          <input id="plan-slug" name="slug" [(ngModel)]="form.slug" (ngModelChange)="slugTouched = true"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
          @if (errors()['slug']) { <p class="text-xs text-error mt-1">{{ errors()['slug'] }}</p> }
        </div>
        <div>
          <label for="plan-description" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Description</label>
          <textarea id="plan-description" name="description" rows="3" [(ngModel)]="form.description"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"></textarea>
        </div>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label for="plan-price" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Price label</label>
            <input id="plan-price" name="price_label" [(ngModel)]="form.price_label" placeholder="$40 / year"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
            <p class="text-xs text-text-muted mt-1">Display only — no payment is taken.</p>
          </div>
          <div>
            <label for="plan-duration" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Term (days)</label>
            <input id="plan-duration" name="duration_days" type="number" min="1" [(ngModel)]="form.duration_days" placeholder="No expiry"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
            @if (errors()['duration_days']) { <p class="text-xs text-error mt-1">{{ errors()['duration_days'] }}</p> }
          </div>
          <div>
            <label for="plan-max" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Max active members</label>
            <input id="plan-max" name="max_members" type="number" min="1" [(ngModel)]="form.max_members" placeholder="Unlimited"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
            @if (errors()['max_members']) { <p class="text-xs text-error mt-1">{{ errors()['max_members'] }}</p> }
          </div>
          <div>
            <label for="plan-sort" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Sort order</label>
            <input id="plan-sort" name="sort_order" type="number" [(ngModel)]="form.sort_order"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none" />
          </div>
        </div>
        <div>
          <label for="plan-benefits" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Benefits (one per line)</label>
          <textarea id="plan-benefits" name="benefits" rows="4" [(ngModel)]="form.benefits"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"></textarea>
        </div>
        <div class="space-y-2 text-sm text-text-primary">
          <label class="flex items-center gap-2"><input type="checkbox" name="requires_approval" [(ngModel)]="form.requires_approval" /> Applications require approval</label>
          <label class="flex items-center gap-2"><input type="checkbox" name="self_signup_enabled" [(ngModel)]="form.self_signup_enabled" /> Users can sign up from the site</label>
          <label class="flex items-center gap-2"><input type="checkbox" name="is_active" [(ngModel)]="form.is_active" /> Active (listed on the site)</label>
        </div>

        @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }
        <div class="flex gap-3 pt-2">
          <app-button type="submit" [loading]="saving()">{{ planId ? 'Save plan' : 'Create plan' }}</app-button>
          <app-button variant="secondary" type="button" (click)="cancel()">Cancel</app-button>
        </div>
      </form>
    }
  `,
})
export class MembershipPlanEditorComponent implements OnInit {
  @Input() appSlug!: string;
  @Input() planId?: string;

  private api = inject(ApiService);
  private router = inject(Router);

  form: PlanForm = {
    name: '', slug: '', description: '', price_label: '', duration_days: 365, requires_approval: false,
    self_signup_enabled: true, max_members: null, benefits: '', is_active: true, sort_order: 0,
  };
  slugTouched = false;
  loading = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);
  errors = signal<Record<string, string>>({});

  ngOnInit() {
    if (!this.planId) return;
    this.loading.set(true);
    this.slugTouched = true;
    this.api.get<MembershipPlan>(`/apps/${this.appSlug}/members/plans/${this.planId}`).subscribe({
      next: p => {
        this.form = {
          name: p.name, slug: p.slug, description: p.description ?? '', price_label: p.price_label ?? '',
          duration_days: p.duration_days, requires_approval: p.requires_approval,
          self_signup_enabled: p.self_signup_enabled, max_members: p.max_members, benefits: p.benefits.join('\n'),
          is_active: p.is_active, sort_order: p.sort_order,
        };
        this.loading.set(false);
      },
      error: () => { this.error.set('Plan not found.'); this.loading.set(false); },
    });
  }

  onNameChange() {
    if (!this.slugTouched) this.form.slug = slugify(this.form.name);
  }

  /** Client-side checks mirroring the API's 422 rules. */
  validate(): Record<string, string> {
    const errors: Record<string, string> = {};
    if (!this.form.name.trim()) errors['name'] = 'Name is required.';
    if (!/^[a-z0-9-]{1,100}$/.test(this.form.slug)) errors['slug'] = 'Use 1-100 lowercase letters, digits and hyphens.';
    const d = this.numberOrNull(this.form.duration_days);
    if (d !== null && (d < 1 || !Number.isInteger(d))) errors['duration_days'] = 'Use a whole number of days, or leave empty.';
    const m = this.numberOrNull(this.form.max_members);
    if (m !== null && (m < 1 || !Number.isInteger(m))) errors['max_members'] = 'Use at least 1, or leave empty for unlimited.';
    return errors;
  }

  buildRequest(): MembershipPlanRequest {
    return {
      name: this.form.name.trim(),
      slug: this.form.slug,
      description: this.form.description.trim() || null,
      price_label: this.form.price_label.trim() || null,
      duration_days: this.numberOrNull(this.form.duration_days),
      requires_approval: this.form.requires_approval,
      self_signup_enabled: this.form.self_signup_enabled,
      max_members: this.numberOrNull(this.form.max_members),
      benefits: this.form.benefits.split('\n').map(b => b.trim()).filter(b => b.length > 0),
      is_active: this.form.is_active,
      sort_order: Number(this.form.sort_order) || 0,
    };
  }

  private numberOrNull(v: number | string | null): number | null {
    if (v === null || v === '' || v === undefined) return null;
    const n = Number(v);
    return Number.isNaN(n) ? null : n;
  }

  save() {
    const errors = this.validate();
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) return;
    this.saving.set(true);
    this.error.set(null);
    const base = `/apps/${this.appSlug}/members/plans`;
    const request$ = this.planId
      ? this.api.put<MembershipPlan>(`${base}/${this.planId}`, this.buildRequest())
      : this.api.post<MembershipPlan>(base, this.buildRequest());
    request$.subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'members', 'plans']),
      error: (e: { status?: number; error?: { detail?: string; errors?: { pointer: string; message: string }[] } }) => {
        this.saving.set(false);
        const fieldErrors: Record<string, string> = {};
        for (const fe of e?.error?.errors ?? []) fieldErrors[fe.pointer] = fe.message;
        this.errors.set(fieldErrors);
        this.error.set(e?.error?.detail ?? 'Could not save the plan.');
      },
    });
  }

  cancel() {
    this.router.navigate(['/apps', this.appSlug, 'members', 'plans']);
  }
}
