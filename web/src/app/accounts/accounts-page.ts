import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { firstValueFrom } from 'rxjs';
import { StaffRole } from '../api/identity';
import { AccountChange, AccountCreated, StaffAccount, UsersApi, roleLabels } from '../api/users';
import { Session } from '../auth/session';
import { ConfirmDialog, ConfirmDialogData } from '../documents/confirm-dialog/confirm-dialog';
import { accountErrorMessage } from './account-errors';
import { CreateAccountDialog } from './create-account-dialog';
import { TemporaryPasswordDialog, TemporaryPasswordDialogData } from './temporary-password-dialog';

const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium' });

// Staff accounts, for Admins (PLANNING D6). Accounts are closed, never deleted, so the
// audit log keeps who did what.
@Component({
  imports: [MatButtonModule, MatMenuModule, MatProgressSpinnerModule, MatTableModule],
  selector: 'app-accounts-page',
  styleUrl: './accounts-page.scss',
  templateUrl: './accounts-page.html',
})
export class AccountsPage {
  private readonly api = inject(UsersApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly session = inject(Session);

  protected readonly columns = ['email', 'role', 'status', 'created', 'actions'];

  protected readonly accounts = signal<StaffAccount[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadFailed = signal(false);
  // The account a change is being sent for; its menu waits meanwhile.
  protected readonly busyId = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.accounts.set(await firstValueFrom(this.api.list()));
      this.loadFailed.set(false);
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loaded.set(true);
    }
  }

  protected roleLabel(account: StaffAccount): string {
    return roleLabels[account.role];
  }

  protected isSelf(account: StaffAccount): boolean {
    return account.id === this.session.user()?.id;
  }

  protected formatDate(value: string): string {
    return dateFormat.format(new Date(value));
  }

  protected async openCreate(): Promise<void> {
    const created = await firstValueFrom(
      this.dialog.open<CreateAccountDialog, void, AccountCreated>(CreateAccountDialog, { autoFocus: 'first-tabbable' }).afterClosed(),
    );
    if (!created) {
      return;
    }

    this.accounts.update((list) => [...list, created.user].sort((a, b) => a.email.localeCompare(b.email, 'tr')));
    this.showTemporaryPassword({ email: created.user.email, temporaryPassword: created.temporaryPassword, reason: 'opened' });
  }

  protected setRole(account: StaffAccount, role: StaffRole): Promise<void> {
    return this.change(account, { role }, `${account.email} artık ${roleLabels[role]}.`);
  }

  protected async setActive(account: StaffAccount, isActive: boolean): Promise<void> {
    if (
      !isActive &&
      !(await this.confirm({
        title: 'Hesabı kapat',
        message: `${account.email} artık giriş yapamaz; açık oturumu da hemen kapanır. Hesap daha sonra yeniden açılabilir.`,
        confirmLabel: 'Kapat',
      }))
    ) {
      return;
    }

    await this.change(account, { isActive }, `${account.email} ${isActive ? 'yeniden açıldı' : 'kapatıldı'}.`);
  }

  protected async resetPassword(account: StaffAccount): Promise<void> {
    if (
      !(await this.confirm({
        title: 'Parolayı sıfırla',
        message: `${account.email} için yeni bir geçici parola üretilecek; eski parolası çalışmaz ve kişi ilk girişte yeni parola belirler.`,
        confirmLabel: 'Sıfırla',
      }))
    ) {
      return;
    }

    this.busyId.set(account.id);
    try {
      const reset = await firstValueFrom(this.api.resetPassword(account.id));
      this.replace({ ...account, mustChangePassword: true });
      this.showTemporaryPassword({ email: account.email, temporaryPassword: reset.temporaryPassword, reason: 'reset' });
    } catch (error) {
      this.notify(accountErrorMessage(error, 'Parola sıfırlanamadı.'));
    } finally {
      this.busyId.set(null);
    }
  }

  private async change(account: StaffAccount, change: AccountChange, done: string): Promise<void> {
    this.busyId.set(account.id);
    try {
      this.replace(await firstValueFrom(this.api.update(account.id, change)));
      this.notify(done);
    } catch (error) {
      this.notify(accountErrorMessage(error, 'Değişiklik kaydedilemedi.'));
    } finally {
      this.busyId.set(null);
    }
  }

  private replace(account: StaffAccount): void {
    this.accounts.update((list) => list.map((a) => (a.id === account.id ? account : a)));
  }

  private showTemporaryPassword(data: TemporaryPasswordDialogData): void {
    this.dialog.open(TemporaryPasswordDialog, { data, disableClose: true, maxWidth: '32rem' });
  }

  private async confirm(data: ConfirmDialogData): Promise<boolean> {
    return (await firstValueFrom(this.dialog.open(ConfirmDialog, { data }).afterClosed())) === true;
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'Kapat', { duration: 6000 });
  }
}
