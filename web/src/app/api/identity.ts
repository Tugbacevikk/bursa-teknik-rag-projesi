import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// The API's /api/auth endpoints (backend: AuthEndpoints.cs).

export type StaffRole = 'Admin' | 'Editor';

export interface CurrentUser {
  id: string;
  email: string;
  role: StaffRole;
  // Signed in with a temporary password: only changing it works until then (PLANNING D6).
  mustChangePassword: boolean;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

// The same limits as the server (UserConfiguration, PasswordRules).
export const identityLimits = {
  emailMaxLength: 254,
  passwordMinLength: 12,
  passwordMaxLength: 128,
} as const;

@Injectable({ providedIn: 'root' })
export class IdentityApi {
  private readonly http = inject(HttpClient);

  // The answer also carries the token, for clients outside the browser; the app ignores
  // it: the browser keeps the session in an HttpOnly cookie the app cannot read.
  login(request: LoginRequest): Observable<unknown> {
    return this.http.post('/api/auth/login', request);
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null);
  }

  currentUser(): Observable<CurrentUser> {
    return this.http.get<CurrentUser>('/api/auth/me');
  }

  // 400 with errors.currentPassword or errors.newPassword when refused.
  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>('/api/auth/password', request);
  }
}
