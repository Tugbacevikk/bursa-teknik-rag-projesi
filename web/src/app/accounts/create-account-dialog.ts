import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { StaffRole, identityLimits } from '../api/identity';
import { AccountCreated, UsersApi, roleLabels } from '../api/users';
import { accountErrorMessage } from './account-errors';

// Opens a staff account. Closes with what the API answered (the account and its
// temporary password), or with nothing when cancelled.
@Component({
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    ReactiveFormsModule,
  ],
  selector: 'app-create-account-dialog',
  template: `
    <h2 mat-dialog-title>Hesap aç</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content class="fields">
        @if (failure(); as message) {
          <p class="failure" role="alert">{{ message }}</p>
        }
        <mat-form-field appearance="outline" hideRequiredMarker>
          <mat-label>E-posta</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="off" inputmode="email" cdkFocusInitial />
          <mat-error>Geçerli bir e-posta adresi girin.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" hideRequiredMarker>
          <mat-label>Rol</mat-label>
          <mat-select formControlName="role">
            @for (role of roles; track role) {
              <mat-option [value]="role">{{ labels[role] }}</mat-option>
            }
          </mat-select>
          <mat-hint>Editör belgeleri yönetir ve analitiği görür; Yönetici ayrıca hesapları yönetir.</mat-hint>
        </mat-form-field>
        <p class="note">Sunucu bir geçici parola üretir; kişi ilk girişte kendi parolasını belirler.</p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Vazgeç</button>
        <button matButton="filled" type="submit" [disabled]="submitting()">
          @if (submitting()) {
            <mat-spinner diameter="20" aria-label="Hesap açılıyor" />
          } @else {
            Hesap aç
          }
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields {
      display: grid;
      gap: 0.5rem;
      min-width: min(24rem, 70vw);
    }

    .failure {
      margin: 0;
      padding: 0.75rem 1rem;
      border-radius: 0.5rem;
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }

    .note {
      margin: 0.75rem 0 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class CreateAccountDialog {
  private readonly api = inject(UsersApi);
  private readonly dialogRef = inject<MatDialogRef<CreateAccountDialog, AccountCreated>>(MatDialogRef);

  protected readonly roles: StaffRole[] = ['Editor', 'Admin'];
  protected readonly labels = roleLabels;
  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(identityLimits.emailMaxLength)]],
    role: ['Editor' as StaffRole, Validators.required],
  });

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.failure.set(null);
    try {
      const { email, role } = this.form.getRawValue();
      this.dialogRef.close(await firstValueFrom(this.api.create(email.trim(), role)));
    } catch (error) {
      this.failure.set(accountErrorMessage(error, 'Hesap açılamadı. Bağlantınızı kontrol edip yeniden deneyin.'));
    } finally {
      this.submitting.set(false);
    }
  }
}
