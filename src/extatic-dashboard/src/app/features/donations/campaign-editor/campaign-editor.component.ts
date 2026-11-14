import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ConfirmModalComponent } from '../../../shared/confirm-modal/confirm-modal.component';
import { Campaign, CampaignRequest } from '../donations.models';
import { SUPPORTED_CURRENCIES, currencyDecimals, fromMinor, toMinor } from '../money';

const INPUT = 'w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none';
const LABEL = 'block text-xs font-medium text-text-muted uppercase tracking-wider mb-1';

/** Amounts are typed in MAJOR units (e.g. 25.00 USD, 500 JPY) and converted to minor units on save. */
@Component({
  selector: 'app-donations-campaign-editor',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent, ConfirmModalComponent],
  template: `
    <app-page-header [title]="id ? 'Edit campaign' : 'New campaign'">
      <a [routerLink]="['/apps', appSlug, 'donations', 'campaigns']"><app-button variant="secondary">Back</app-button></a>
    </app-page-header>

    @if (loading()) {
      <app-loading-spinner />
    } @else {
      <form [formGroup]="form" (ngSubmit)="save()" class="max-w-2xl space-y-5">
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label class="${LABEL}" for="name">Name</label>
            <input id="name" formControlName="name" class="${INPUT}" (input)="autoSlug()" />
          </div>
          <div>
            <label class="${LABEL}" for="slug">Slug</label>
            <input id="slug" formControlName="slug" class="${INPUT} font-mono" placeholder="general-fund" />
          </div>
        </div>
        <div>
          <label class="${LABEL}" for="description">Description</label>
          <textarea id="description" formControlName="description" rows="3" class="${INPUT}"></textarea>
        </div>
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div>
            <label class="${LABEL}" for="currency">Currency</label>
            <select id="currency" formControlName="currency" class="${INPUT}">
              @for (c of currencies; track c) { <option [value]="c">{{ c.toUpperCase() }}</option> }
            </select>
            <p class="text-xs text-text-muted mt-1">
              {{ decimals() === 0 ? 'Whole amounts only (no minor unit).' : 'Up to 2 decimals.' }}
            </p>
          </div>
          <div>
            <label class="${LABEL}" for="goal">Goal (optional)</label>
            <input id="goal" formControlName="goal" inputmode="decimal" class="${INPUT}" />
          </div>
          <div>
            <label class="${LABEL}" for="suggested">Suggested amounts</label>
            <input id="suggested" formControlName="suggested" class="${INPUT}" placeholder="5, 10, 25" />
          </div>
          <div>
            <label class="${LABEL}" for="min">Minimum</label>
            <input id="min" formControlName="min" inputmode="decimal" class="${INPUT}" />
          </div>
          <div>
            <label class="${LABEL}" for="max">Maximum</label>
            <input id="max" formControlName="max" inputmode="decimal" class="${INPUT}" />
          </div>
        </div>
        <div class="flex flex-wrap gap-6 text-sm text-text-primary">
          <label class="flex items-center gap-2"><input type="checkbox" formControlName="allow_custom_amount" class="accent-accent" /> Allow custom amounts</label>
          <label class="flex items-center gap-2"><input type="checkbox" formControlName="allow_recurring" class="accent-accent" /> Allow monthly / yearly</label>
          <label class="flex items-center gap-2"><input type="checkbox" formControlName="show_donor_list" class="accent-accent" /> Show recent donors publicly</label>
          <label class="flex items-center gap-2"><input type="checkbox" formControlName="is_active" class="accent-accent" /> Active</label>
        </div>
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label class="${LABEL}" for="success_url">Success URL</label>
            <input id="success_url" formControlName="success_url" class="${INPUT} font-mono" placeholder="https://example.org/thanks" />
            <p class="text-xs text-text-muted mt-1"><span class="font-mono">donation_id</span> is appended so the page can poll the status.</p>
          </div>
          <div>
            <label class="${LABEL}" for="cancel_url">Cancel URL</label>
            <input id="cancel_url" formControlName="cancel_url" class="${INPUT} font-mono" placeholder="https://example.org/donate" />
          </div>
          <div>
            <label class="${LABEL}" for="starts_at">Starts (optional)</label>
            <input id="starts_at" type="datetime-local" formControlName="starts_at" class="${INPUT}" />
          </div>
          <div>
            <label class="${LABEL}" for="ends_at">Ends (optional)</label>
            <input id="ends_at" type="datetime-local" formControlName="ends_at" class="${INPUT}" />
          </div>
        </div>
        <div>
          <label class="${LABEL}" for="notify_emails">Notify on each donation (comma-separated emails)</label>
          <input id="notify_emails" formControlName="notify_emails" class="${INPUT}" />
        </div>

        @if (errors().length) {
          <ul class="text-xs text-error space-y-1" data-testid="errors">
            @for (e of errors(); track e) { <li>{{ e }}</li> }
          </ul>
        }
        <div class="flex gap-3">
          <app-button type="submit" [loading]="saving()">{{ id ? 'Save' : 'Create campaign' }}</app-button>
          @if (id) { <app-button variant="danger" (click)="confirmDelete.set(true)">Delete</app-button> }
        </div>
      </form>
    }

    @if (confirmDelete()) {
      <app-confirm-modal title="Delete campaign" confirmLabel="Delete"
        message="The campaign is removed; its donations are kept without a campaign." (confirmed)="remove()" (cancelled)="confirmDelete.set(false)" />
    }
  `,
})
export class CampaignEditorComponent implements OnInit {
  @Input() appSlug!: string;
  /** Route param; undefined on the "new" route. */
  @Input() id?: string;

  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  readonly currencies = SUPPORTED_CURRENCIES;
  loading = signal(false);
  saving = signal(false);
  errors = signal<string[]>([]);
  confirmDelete = signal(false);
  private slugTouched = false;

  form = this.fb.nonNullable.group({
    name: '',
    slug: '',
    description: '',
    currency: 'usd',
    goal: '',
    suggested: '5, 10, 25',
    min: '1',
    max: '10000',
    allow_custom_amount: true,
    allow_recurring: false,
    show_donor_list: false,
    is_active: true,
    success_url: '',
    cancel_url: '',
    starts_at: '',
    ends_at: '',
    notify_emails: '',
  });

  decimals() { return currencyDecimals(this.form.controls.currency.value); }

  private get base() { return `/apps/${this.appSlug}/donations/campaigns`; }

  ngOnInit() {
    if (!this.id) return;
    this.slugTouched = true;
    this.loading.set(true);
    this.api.get<Campaign>(`${this.base}/${this.id}`).subscribe({
      next: c => { this.load(c); this.loading.set(false); },
      error: () => { this.errors.set(['Failed to load the campaign.']); this.loading.set(false); },
    });
  }

  /** Fills the form from an API campaign (minor → major units). */
  load(c: Campaign) {
    const major = (m: number | null) => fromMinor(m, c.currency);
    this.form.setValue({
      name: c.name,
      slug: c.slug,
      description: c.description ?? '',
      currency: c.currency,
      goal: major(c.goal_amount_minor),
      suggested: c.suggested_amounts_minor.map(major).join(', '),
      min: major(c.min_amount_minor),
      max: major(c.max_amount_minor),
      allow_custom_amount: c.allow_custom_amount,
      allow_recurring: c.allow_recurring,
      show_donor_list: c.show_donor_list,
      is_active: c.is_active,
      success_url: c.success_url,
      cancel_url: c.cancel_url,
      starts_at: toLocalInput(c.starts_at),
      ends_at: toLocalInput(c.ends_at),
      notify_emails: c.notify_emails.join(', '),
    });
  }

  autoSlug() {
    if (this.slugTouched || this.form.controls.slug.dirty) return;
    const slug = this.form.controls.name.value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 100);
    this.form.controls.slug.setValue(slug);
  }

  /** Converts the form to an API request (major → minor units). Returns null and sets errors when invalid. */
  buildRequest(): CampaignRequest | null {
    const v = this.form.getRawValue();
    const currency = v.currency;
    const errors: string[] = [];
    const unit = currencyDecimals(currency) === 0 ? 'a whole number' : 'an amount with at most 2 decimals';
    const amount = (label: string, text: string, required: boolean): number | null => {
      if (!text.trim()) { if (required) errors.push(`${label} is required.`); return null; }
      const minor = toMinor(text, currency);
      if (minor === null) errors.push(`${label} must be ${unit} in ${currency.toUpperCase()}.`);
      return minor;
    };
    const goal = amount('Goal', v.goal, false);
    const min = amount('Minimum', v.min, true);
    const max = amount('Maximum', v.max, true);
    const suggested = v.suggested.split(',').map(s => s.trim()).filter(Boolean)
      .map(s => amount(`Suggested amount "${s}"`, s, true)).filter((n): n is number => n !== null);
    if (!v.name.trim()) errors.push('Name is required.');
    if (errors.length) { this.errors.set(errors); return null; }
    this.errors.set([]);
    return {
      name: v.name.trim(),
      slug: v.slug.trim(),
      description: v.description.trim() || null,
      currency,
      goal_amount_minor: goal,
      suggested_amounts_minor: suggested,
      min_amount_minor: min!,
      max_amount_minor: max!,
      allow_custom_amount: v.allow_custom_amount,
      allow_recurring: v.allow_recurring,
      show_donor_list: v.show_donor_list,
      is_active: v.is_active,
      success_url: v.success_url.trim(),
      cancel_url: v.cancel_url.trim(),
      starts_at: fromLocalInput(v.starts_at),
      ends_at: fromLocalInput(v.ends_at),
      notify_emails: v.notify_emails.split(/[,\s]+/).map(s => s.trim()).filter(Boolean),
    };
  }

  save() {
    const body = this.buildRequest();
    if (!body) return;
    this.saving.set(true);
    const call = this.id ? this.api.put<Campaign>(`${this.base}/${this.id}`, body) : this.api.post<Campaign>(this.base, body);
    call.subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'donations', 'campaigns']),
      error: (e: HttpErrorResponse) => {
        this.saving.set(false);
        const list = e.error?.errors as { pointer: string; message: string }[] | undefined;
        this.errors.set(list?.length ? list.map(x => `${x.pointer}: ${x.message}`)
          : [e.status === 409 ? 'A campaign with this slug already exists.' : 'Failed to save the campaign.']);
      },
    });
  }

  remove() {
    this.confirmDelete.set(false);
    this.api.delete<void>(`${this.base}/${this.id}`).subscribe({
      next: () => this.router.navigate(['/apps', this.appSlug, 'donations', 'campaigns']),
      error: () => this.errors.set(['Failed to delete the campaign.']),
    });
  }
}

function toLocalInput(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function fromLocalInput(value: string): string | null {
  return value ? new Date(value).toISOString() : null;
}
