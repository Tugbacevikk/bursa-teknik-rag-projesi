import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CurrentUser, IdentityApi, LoginRequest } from '../api/identity';

// 'unknown' lasts only until the first answer from the API at startup.
export type SessionStatus = 'unknown' | 'signedIn' | 'signedOut';

// Adapted from DocAssistant. Who is signed in, for the whole app.
//
// The session itself lives in an HttpOnly cookie the app cannot read, so the app cannot
// look it up: it asks the API (GET /api/auth/me) and remembers the answer here. Screens
// read the signals below; signing in and out goes through this service so that the
// remembered answer and the cookie never drift apart.
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly api = inject(IdentityApi);

  private readonly currentUser = signal<CurrentUser | null>(null);
  private readonly currentStatus = signal<SessionStatus>('unknown');

  readonly user = this.currentUser.asReadonly();
  readonly status = this.currentStatus.asReadonly();
  readonly isSignedIn = computed(() => this.currentStatus() === 'signedIn');

  // Admins also manage staff accounts; both roles manage documents.
  readonly isAdmin = computed(() => this.currentUser()?.role === 'Admin');

  // Signed in with a temporary password: the panel shows only the password screen.
  readonly mustChangePassword = computed(() => this.currentUser()?.mustChangePassword === true);

  // Asks the API who is signed in. Called once when the app starts (app.config.ts) and
  // after signing in. Never fails: without an answer the user counts as signed out, and
  // the login screen reports what goes wrong when they try.
  async load(): Promise<void> {
    try {
      const user = await firstValueFrom(this.api.currentUser());

      this.currentUser.set(user);
      this.currentStatus.set('signedIn');
    } catch {
      this.clear();
    }
  }

  // Fails with the API's error (HttpErrorResponse) when the request is rejected; the
  // login screen shows it.
  async login(request: LoginRequest): Promise<void> {
    await firstValueFrom(this.api.login(request));
    await this.load();
  }

  // Fails with the API's error (400: wrong current password, unsuitable new one).
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await firstValueFrom(this.api.changePassword({ currentPassword, newPassword }));
    this.currentUser.update((user) => (user ? { ...user, mustChangePassword: false } : user));
  }

  // Only the API can remove the cookie. If that request fails the cookie is still there,
  // so the session is not forgotten here either.
  async logout(): Promise<void> {
    await firstValueFrom(this.api.logout());
    this.clear();
  }

  // The API answered 401 to a request: the session has expired or the account was closed.
  expire(): void {
    this.clear();
  }

  private clear(): void {
    this.currentUser.set(null);
    this.currentStatus.set('signedOut');
  }
}
