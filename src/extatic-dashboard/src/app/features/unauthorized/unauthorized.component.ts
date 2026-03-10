import { Component } from '@angular/core';

@Component({
  selector: 'app-unauthorized',
  standalone: true,
  imports: [],
  template: `
    <div class="min-h-screen bg-background flex items-center justify-center">
      <div class="text-center">
        <h1 class="text-2xl font-semibold text-text-primary mb-2">Unauthorized</h1>
        <p class="text-text-muted text-sm mb-6">You must be signed in to access this page.</p>
        <a href="/oauth2/sign_in?rd=/" class="text-accent text-sm hover:underline">Sign in</a>
      </div>
    </div>
  `,
})
export class UnauthorizedComponent {}
