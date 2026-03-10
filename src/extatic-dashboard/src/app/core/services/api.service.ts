import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  get<T>(path: string): Observable<T> {
    return this.http.get<T>(path);
  }

  post<T>(path: string, body: unknown = {}): Observable<T> {
    return this.http.post<T>(path, body);
  }

  put<T>(path: string, body: unknown = {}): Observable<T> {
    return this.http.put<T>(path, body);
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(path);
  }
}
