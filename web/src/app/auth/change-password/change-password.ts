import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { identityLimits } from '../../api/identity';
import { Session } from '../session';

// The signed-in person's own password (PLANNING D6). Someone signed in with a temporary
// password is sent here first and cannot leave until it is changed (passwordChangedFirst).
@Component({
  imports: [MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, ReactiveFormsModule],
  selector: 'app-change-password',
  template: `
    <section class="card" aria-labelledby="password-title">
      <h1 id="password-title" class="title">Parolayı değiştir</h1>
      @if (session.mustChangePassword()) {
        <p class="notice" role="status">
          Hesabınız geçici bir parolayla açıldı ya da parolanız sıfırlandı. Devam etmeden önce yalnızca
          sizin bildiğiniz yeni bir parola belirleyin.
        </p>
      }

      @if (failure(); as message) {
        <p class="failure" role="alert">{{ message }}</p>
      }

      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-form-field appearance="outline" hideRequiredMarker>
          <mat-label>{{ session.mustChangePassword() ? 'Geçici parola' : 'Mevcut parola' }}</mat-label>
          <input matInput type="password" formControlName="currentPassword" autocomplete="current-password" />
          @if (form.controls.currentPassword.hasError('server')) {
            <mat-error>{{ form.controls.currentPassword.getError('server') }}</mat-error>
          } @else {
            <mat-error>Mevcut parolanızı girin.</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" hideRequiredMarker>
          <mat-label>Yeni parola</mat-label>
          <input matInput type="password" formControlName="newPassword" autocomplete="new-password" />
          <mat-hint>En az {{ limits.passwordMinLength }} karakter.</mat-hint>
          @if (form.controls.newPassword.hasError('server')) {
            <mat-error>{{ form.controls.newPassword.getError('server') }}</mat-error>
          } @else {
            <mat-error>Yeni parola {{ limits.passwordMinLength }}–{{ limits.passwordMaxLength }} karakter olmalıdır.</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" hideRequiredMarker>
          <mat-label>Yeni parola (tekrar)</mat-label>
          <input matInput type="password" formControlName="repeat" autocomplete="new-password" />
          <mat-error>Parolalar aynı değil.</mat-error>
        </mat-form-field>

        <button matButton="filled" type="submit" class="submit" [disabled]="submitting()">
          @if (submitting()) {
            <mat-spinner diameter="20" aria-label="Parola değiştiriliyor" />
          } @else {
            Parolayı değiştir
          }
        </button>
      </form>
    </section>
  `,
  styles: `
    .card {
      max-width: 26rem;
      padding: 1.5rem;
      border-radius: 0.75rem;
      background: var(--mat-sys-surface);
      box-shadow: 0 1px 3px rgb(0 0 0 / 0.08);
    }

    .title {
      margin: 0 0 1rem;
      font: var(--mat-sys-headline-small);
    }

    .notice,
    .failure {
      margin: 0 0 1rem;
      padding: 0.75rem 1rem;
      border-radius: 0.5rem;
      font: var(--mat-sys-body-medium);
    }

    .notice {
      background: #fff4e0;
      color: #6b3a00;
    }

    .failure {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }

    .form {
      display: grid;
      gap: 0.5rem;
    }

    .submit {
      justify-self: start;
      min-width: 10rem;
    }
  `,
})
export class ChangePassword {
  protected readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly limits = identityLimits;
  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    currentPassword: ['', [Validators.required, Validators.maxLength(identityLimits.passwordMaxLength)]],
    newPassword: [
      '',
      [
        Validators.required,
        Validators.minLength(identityLimits.passwordMinLength),
        Validators.maxLength(identityLimits.passwordMaxLength),
      ],
    ],
    repeat: ['', [Validators.required, (control: AbstractControl) => this.matches(control)]],
  });

  constructor() {
    // The repeat is checked again when the new password changes after it.
    this.form.controls.newPassword.valueChanges.subscribe(() =>
      this.form.controls.repeat.updateValueAndValidity({ emitEvent: false }),
    );
  }

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
    const { currentPassword, newPassword } = this.form.getRawValue();

    try {
      await this.session.changePassword(currentPassword, newPassword);
      this.form.reset();
      this.snackBar.open('Parolanız değiştirildi.', 'Kapat', { duration: 4000 });
      await this.router.navigate(['/panel']);
    } catch (error) {
      this.showError(error);
    } finally {
      this.submitting.set(false);
    }
  }

  private matches(control: AbstractControl): ValidationErrors | null {
    return this.form && control.value !== this.form.controls.newPassword.value ? { mismatch: true } : null;
  }

  // The API's field errors go under their fields; anything else above the form.
  private showError(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 400 && error.error?.errors) {
      const errors = error.error.errors as Record<string, string[]>;
      for (const field of ['currentPassword', 'newPassword'] as const) {
        const message = errors[field]?.[0];
        if (message) {
          this.form.controls[field].setErrors({ server: message });
          this.form.controls[field].markAsTouched();
        }
      }
      return;
    }

    this.failure.set(
      error instanceof HttpErrorResponse && error.status === 429
        ? 'Çok fazla deneme yapıldı. Biraz bekleyip yeniden deneyin.'
        : 'Parola değiştirilemedi. Bağlantınızı kontrol edip yeniden deneyin.',
    );
  }
}
