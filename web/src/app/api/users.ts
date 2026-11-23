import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { StaffRole } from './identity';

// The API's /api/users endpoints, Admin only (backend: UserEndpoints.cs, PLANNING D6).

export interface StaffAccount {
  id: string;
  email: string;
  role: StaffRole;
  isActive: boolean;
  // Has not yet replaced the temporary password.
  mustChangePassword: boolean;
  createdAt: string;
}

// The temporary password is only ever in these answers; nothing keeps it afterwards.
export interface AccountCreated {
  user: StaffAccount;
  temporaryPassword: string;
}

export interface PasswordReset {
  temporaryPassword: string;
}

// Fields left out stay as they are.
export interface AccountChange {
  role?: StaffRole;
  isActive?: boolean;
}

export const roleLabels: Record<StaffRole, string> = {
  Admin: 'Yönetici',
  Editor: 'Editör',
};

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list(): Observable<StaffAccount[]> {
    return this.http.get<StaffAccount[]>('/api/users');
  }

  // 409 when the email already has an account.
  create(email: string, role: StaffRole): Observable<AccountCreated> {
    return this.http.post<AccountCreated>('/api/users', { email, role });
  }

  // 400 for the Admin's own account, 409 for the last active Admin.
  update(id: string, change: AccountChange): Observable<StaffAccount> {
    return this.http.patch<StaffAccount>(`/api/users/${id}`, change);
  }

  resetPassword(id: string): Observable<PasswordReset> {
    return this.http.post<PasswordReset>(`/api/users/${id}/reset-password`, null);
  }
}
