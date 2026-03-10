import { Component, inject } from '@angular/core';
import { Router, NavigationEnd, ActivatedRoute } from '@angular/router';
import { filter, map } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-topbar',
  standalone: true,
  template: `
    <header class="h-14 border-b border-border flex items-center px-6 shrink-0">
      <h1 class="text-sm font-medium text-text-primary">{{ pageTitle() }}</h1>
    </header>
  `,
})
export class TopbarComponent {
  private router = inject(Router);
  private activatedRoute = inject(ActivatedRoute);

  pageTitle = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      map(() => this.getRouteTitle())
    ),
    { initialValue: this.getRouteTitle() }
  );

  private getRouteTitle(): string {
    let route = this.activatedRoute;
    while (route.firstChild) route = route.firstChild;
    return route.snapshot?.data?.['title'] ?? '';
  }
}
