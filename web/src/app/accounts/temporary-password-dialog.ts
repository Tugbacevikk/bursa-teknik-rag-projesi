import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

export interface TemporaryPasswordDialogData {
  email: string;
  temporaryPassword: string;
  // "opened" for a new account, "reset" for a reset password.
  reason: 'opened' | 'reset';
}

// Shows a temporary password the one time the API sends it (PLANNING D6). Closing only
// with the button, so it is not lost to a stray click outside.
@Component({
  imports: [MatButtonModule, MatDialogModule],
  selector: 'app-temporary-password-dialog',
  template: `
    <h2 mat-dialog-title>{{ data.reason === 'opened' ? 'Hesap açıldı' : 'Parola sıfırlandı' }}</h2>
    <mat-dialog-content>
      <p><strong>{{ data.email }}</strong> için geçici parola:</p>
      <p class="password">
        <code>{{ data.temporaryPassword }}</code>
        <button matButton="outlined" type="button" (click)="copy()">{{ copied() ? 'Kopyalandı' : 'Kopyala' }}</button>
      </p>
      <p class="warning">
        Bu parola bir daha gösterilmez. Kişiye güvenli bir yoldan iletin (yüz yüze ya da telefonla; e-postayla
        göndermeyin). İlk girişte kendi parolasını belirlemeden başka bir şey yapamaz.
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton="filled" type="button" mat-dialog-close>Not aldım, kapat</button>
    </mat-dialog-actions>
  `,
  styles: `
    .password {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }

    code {
      padding: 0.5rem 0.75rem;
      border-radius: 0.5rem;
      background: #f0f3f7;
      font-size: 1.25rem;
      letter-spacing: 0.08em;
      user-select: all;
    }

    .warning {
      padding: 0.75rem 1rem;
      border-radius: 0.5rem;
      background: #fff4e0;
      color: #6b3a00;
    }
  `,
})
export class TemporaryPasswordDialog {
  protected readonly data = inject<TemporaryPasswordDialogData>(MAT_DIALOG_DATA);
  protected readonly copied = signal(false);

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.data.temporaryPassword);
      this.copied.set(true);
    } catch {
      // No clipboard access (e.g. not https): the password can still be selected by hand.
    }
  }
}
