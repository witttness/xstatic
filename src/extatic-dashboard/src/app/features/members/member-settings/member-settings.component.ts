import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ButtonComponent } from '../../../shared/button/button.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { MemberSettings } from '../members.model';

/** Parses the schema textarea: empty → null; otherwise must be a JSON object. */
export function parseSchema(text: string): { value: Record<string, unknown> | null; error: string | null } {
  if (!text.trim()) return { value: null, error: null };
  try {
    const parsed: unknown = JSON.parse(text);
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed))
      return { value: null, error: 'The schema must be a JSON object.' };
    return { value: parsed as Record<string, unknown>, error: null };
  } catch (e) {
    return { value: null, error: 'Invalid JSON: ' + (e instanceof Error ? e.message : String(e)) };
  }
}

function splitList(text: string): string[] {
  return text.split(/[\n,]/).map(s => s.trim()).filter(s => s.length > 0);
}

@Component({
  selector: 'app-member-settings',
  standalone: true,
  imports: [FormsModule, RouterLink, PageHeaderComponent, ButtonComponent, LoadingSpinnerComponent],
  template: `
    <a [routerLink]="['/apps', appSlug, 'members']" class="text-xs text-text-muted hover:text-text-primary">&larr; Members</a>
    <app-page-header title="Member settings" />

    @if (loading()) {
      <app-loading-spinner />
    } @else {
      <form (ngSubmit)="save()" class="max-w-2xl space-y-6">
        <div>
          <label for="profile-schema" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Profile schema (JSON Schema)</label>
          <textarea id="profile-schema" name="schema" rows="12" [(ngModel)]="schemaText" (ngModelChange)="schemaError.set(parseSchema($event).error)"
            placeholder='{ "type": "object", "properties": { "city": { "type": "string" } } }'
            class="w-full bg-background border border-border px-3 py-2 text-xs text-text-primary font-mono focus:border-accent focus:outline-none"></textarea>
          @if (schemaError()) { <p class="text-xs text-error mt-1">{{ schemaError() }}</p> }
          <p class="text-xs text-text-muted mt-1">Validates the profile members submit when joining or editing. Leave empty for no validation.</p>
        </div>

        <fieldset class="space-y-3">
          <legend class="text-xs font-medium text-text-muted uppercase tracking-wider mb-2">Directory</legend>
          <label class="flex items-center gap-2 text-sm text-text-primary">
            <input type="checkbox" name="directory_enabled" [(ngModel)]="directoryEnabled" /> Enable the member directory
          </label>
          <div>
            <label for="directory-fields" class="block text-xs text-text-muted mb-1">Profile fields shown in the directory (comma or newline separated)</label>
            <input id="directory-fields" name="directory_fields" [(ngModel)]="directoryFields"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
            <p class="text-xs text-text-muted mt-1">Only opted-in active members appear, and only these fields plus their display name.</p>
          </div>
        </fieldset>

        <div class="grid grid-cols-2 gap-4">
          <div>
            <label for="prefix" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Member number prefix</label>
            <input id="prefix" name="prefix" [(ngModel)]="prefix"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
            <p class="text-xs text-text-muted mt-1">Next number: {{ prefix }}{{ nextNumber() }}</p>
          </div>
          <div>
            <label for="reminders" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Renewal reminders (days before expiry)</label>
            <input id="reminders" name="reminders" [(ngModel)]="reminderDays" placeholder="30, 7"
              class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary font-mono focus:border-accent focus:outline-none" />
          </div>
        </div>

        <div>
          <label for="notify" class="block text-xs font-medium text-text-muted uppercase tracking-wider mb-1">Notify on new applications</label>
          <textarea id="notify" name="notify" rows="2" [(ngModel)]="notifyEmails" placeholder="board@example.com"
            class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none"></textarea>
        </div>

        @if (error()) { <p class="text-xs text-error">{{ error() }}</p> }
        @if (saved()) { <p class="text-xs text-text-muted">Settings saved.</p> }
        <app-button type="submit" [loading]="saving()" [disabled]="!!schemaError()">Save settings</app-button>
      </form>
    }
  `,
})
export class MemberSettingsComponent implements OnInit {
  @Input() appSlug!: string;

  private api = inject(ApiService);

  readonly parseSchema = parseSchema;
  loading = signal(true);
  saving = signal(false);
  saved = signal(false);
  error = signal<string | null>(null);
  schemaError = signal<string | null>(null);
  nextNumber = signal('00001');

  schemaText = '';
  directoryEnabled = false;
  directoryFields = '';
  prefix = 'M-';
  reminderDays = '30, 7';
  notifyEmails = '';

  ngOnInit() {
    this.api.get<MemberSettings>(`/apps/${this.appSlug}/members/settings`).subscribe({
      next: s => { this.apply(s); this.loading.set(false); },
      error: () => { this.error.set('Failed to load settings.'); this.loading.set(false); },
    });
  }

  private apply(s: MemberSettings) {
    this.schemaText = s.profile_schema ? JSON.stringify(s.profile_schema, null, 2) : '';
    this.directoryEnabled = s.directory_enabled;
    this.directoryFields = s.directory_fields.join(', ');
    this.prefix = s.member_number_prefix;
    this.reminderDays = s.renewal_reminder_days.join(', ');
    this.notifyEmails = s.notify_emails.join('\n');
    this.nextNumber.set(String(s.next_member_number).padStart(5, '0'));
  }

  save() {
    const schema = parseSchema(this.schemaText);
    this.schemaError.set(schema.error);
    if (schema.error) return;
    const days = splitList(this.reminderDays).map(Number);
    if (days.some(d => !Number.isInteger(d) || d < 1)) {
      this.error.set('Reminder days must be whole numbers of at least 1.');
      return;
    }
    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);
    this.api.put<MemberSettings>(`/apps/${this.appSlug}/members/settings`, {
      profile_schema: schema.value,
      directory_enabled: this.directoryEnabled,
      directory_fields: splitList(this.directoryFields),
      member_number_prefix: this.prefix,
      notify_emails: splitList(this.notifyEmails),
      renewal_reminder_days: days,
    }).subscribe({
      next: s => { this.apply(s); this.saving.set(false); this.saved.set(true); },
      error: (e: { error?: { detail?: string; errors?: { pointer: string; message: string }[] } }) => {
        this.saving.set(false);
        const details = (e?.error?.errors ?? []).map(x => `${x.pointer}: ${x.message}`).join('; ');
        this.error.set(details || e?.error?.detail || 'Could not save settings.');
      },
    });
  }
}
