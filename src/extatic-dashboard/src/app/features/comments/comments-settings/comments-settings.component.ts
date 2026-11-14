import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { CommentsNavComponent } from '../comments-nav.component';
import { CommentSettings } from '../comments.model';

interface ApiValidationError { pointer: string; message: string }

const splitLines = (value: string) =>
  value.split(/[\n,]/).map(v => v.trim()).filter(v => v.length > 0);

@Component({
  selector: 'app-comments-settings',
  standalone: true,
  imports: [FormsModule, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent, CommentsNavComponent],
  template: `
    <app-page-header title="Comments" subtitle="Comment settings for this app." />
    <app-comments-nav [appSlug]="appSlug" />

    @if (loading()) {
      <app-loading-spinner />
    } @else if (loadError()) {
      <p class="text-error text-sm">{{ loadError() }}</p>
    } @else if (model) {
      <form (ngSubmit)="save()" class="max-w-xl space-y-5">
        <label class="flex items-center gap-2 text-sm text-text-primary">
          <input type="checkbox" name="enabled" [(ngModel)]="model.enabled" /> Enable comments
        </label>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="mode">Moderation</label>
          <select id="mode" name="moderation_mode" [(ngModel)]="model.moderation_mode" class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary">
            <option value="all">Hold every comment for review</option>
            <option value="anonymous_only">Hold anonymous comments only</option>
            <option value="none">Publish immediately</option>
          </select>
        </div>

        <label class="flex items-center gap-2 text-sm text-text-primary">
          <input type="checkbox" name="allow_anonymous" [(ngModel)]="model.allow_anonymous" /> Allow anonymous comments
        </label>
        <label class="flex items-center gap-2 text-sm text-text-primary">
          <input type="checkbox" name="require_email" [(ngModel)]="model.require_email_for_anonymous" /> Require email from anonymous commenters
        </label>
        <label class="flex items-center gap-2 text-sm text-text-primary">
          <input type="checkbox" name="expose_avatar_hash" [(ngModel)]="model.expose_avatar_hash" /> Expose avatar hashes (Gravatar-style)
        </label>

        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="max_length">Max length</label>
            <input id="max_length" type="number" name="max_length" [(ngModel)]="model.max_length" min="1" max="20000" class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="max_depth">Max reply depth</label>
            <input id="max_depth" type="number" name="max_depth" [(ngModel)]="model.max_depth" min="1" max="10" class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="max_links">Max links before review</label>
            <input id="max_links" type="number" name="max_links" [(ngModel)]="model.max_links" min="0" max="100" class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary" />
          </div>
          <div>
            <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="auto_close">Auto-close after days</label>
            <input id="auto_close" type="number" name="auto_close_after_days" [(ngModel)]="model.auto_close_after_days" min="1" placeholder="Never" class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary" />
          </div>
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="blocked_terms">Blocked terms (one per line)</label>
          <textarea id="blocked_terms" name="blocked_terms" rows="4" [(ngModel)]="blockedTermsText" class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono"></textarea>
          <select name="blocked_term_action" [(ngModel)]="model.blocked_term_action" class="mt-2 bg-background border border-border px-3 py-1.5 text-sm text-text-primary">
            <option value="pending">Hold for review</option>
            <option value="spam">Mark as spam</option>
          </select>
        </div>

        <div>
          <label class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1" for="notify_emails">Notify emails (one per line)</label>
          <textarea id="notify_emails" name="notify_emails" rows="3" [(ngModel)]="notifyEmailsText" class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono"></textarea>
        </div>

        @for (e of errors(); track e.pointer + e.message) {
          <p class="text-xs text-error">{{ e.pointer }}: {{ e.message }}</p>
        }
        @if (saveError()) { <p class="text-xs text-error">{{ saveError() }}</p> }
        @if (saved()) { <p class="text-xs text-success">Settings saved.</p> }

        <app-button type="submit" [loading]="saving()">Save settings</app-button>
      </form>
    }
  `,
})
export class CommentsSettingsComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  model: CommentSettings | null = null;
  blockedTermsText = '';
  notifyEmailsText = '';
  loading = signal(true);
  loadError = signal<string | null>(null);
  saving = signal(false);
  saved = signal(false);
  saveError = signal<string | null>(null);
  errors = signal<ApiValidationError[]>([]);

  private get url() { return `/apps/${this.appSlug}/comments/settings`; }

  ngOnInit() {
    this.api.get<CommentSettings>(this.url).subscribe({
      next: s => { this.apply(s); this.loading.set(false); },
      error: err => {
        this.loadError.set(err?.status === 403 ? 'Only owners and admins can change comment settings.' : 'Failed to load settings.');
        this.loading.set(false);
      },
    });
  }

  save() {
    if (!this.model) return;
    const body: CommentSettings = {
      ...this.model,
      auto_close_after_days: this.model.auto_close_after_days ? Number(this.model.auto_close_after_days) : null,
      blocked_terms: splitLines(this.blockedTermsText),
      notify_emails: splitLines(this.notifyEmailsText),
    };
    delete body.updated_at;
    this.saving.set(true);
    this.saved.set(false);
    this.saveError.set(null);
    this.errors.set([]);
    this.api.put<CommentSettings>(this.url, body).subscribe({
      next: s => { this.apply(s); this.saving.set(false); this.saved.set(true); },
      error: err => {
        this.saving.set(false);
        // ProblemDetails from the API nests the list: { errors: { errors: [...] } }.
        const list = err?.error?.errors?.errors as ApiValidationError[] | undefined;
        if (err?.status === 422 && Array.isArray(list)) this.errors.set(list);
        else this.saveError.set('Failed to save settings.');
      },
    });
  }

  private apply(s: CommentSettings) {
    this.model = { ...s };
    this.blockedTermsText = s.blocked_terms.join('\n');
    this.notifyEmailsText = s.notify_emails.join('\n');
  }
}
