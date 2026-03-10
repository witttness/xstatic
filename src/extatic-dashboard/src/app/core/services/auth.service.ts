import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, of } from 'rxjs';
import { User } from '../models/user.model';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);

  currentUser = signal<User | null>(null);
  loading = signal(true);

  isAuthenticated = computed(() => this.currentUser() !== null);

  loadCurrentUser(): Promise<void> {
    return new Promise(resolve => {
      this.http.get<User>('/auth/me', { withCredentials: true }).pipe(
        catchError(() => of(null))
      ).subscribe(user => {
        this.currentUser.set(user);
        this.loading.set(false);
        resolve();
      });
    });
  }
}
