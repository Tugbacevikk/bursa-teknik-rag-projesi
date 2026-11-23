import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Session } from '../auth/session';

const roleLabels: Record<string, string> = {
  Admin: 'Yönetici',
  Editor: 'Editör',
};

// The frame around every staff screen: the top bar with the signed-in person and the
// navigation. Screens are shown in the outlet.
@Component({
  imports: [MatButtonModule, MatToolbarModule, RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-shell',
  styleUrl: './shell.scss',
  templateUrl: './shell.html',
})
export class Shell {
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly session = inject(Session);

  protected readonly signingOut = signal(false);

  protected roleLabel(role: string | undefined): string {
    return role ? (roleLabels[role] ?? role) : '';
  }

  protected async signOut(): Promise<void> {
    this.signingOut.set(true);
    try {
      await this.session.logout();
      await this.router.navigate(['/giris']);
    } catch {
      // The cookie is still there; staying signed in is the honest state.
      this.snackBar.open('Çıkış yapılamadı. Bağlantınızı kontrol edip yeniden deneyin.', 'Kapat', {
        duration: 6000,
      });
    } finally {
      this.signingOut.set(false);
    }
  }
}
