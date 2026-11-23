import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { firstValueFrom } from 'rxjs';
import {
  DocumentCreated,
  DocumentSummary,
  DocumentsApi,
  documentKindLabels,
  documentLimits,
  documentStatusLabels,
} from '../api/documents';
import { ConfirmDialog, ConfirmDialogData } from './confirm-dialog/confirm-dialog';
import { actionFailureMessage, tooLargeMessage, uploadFailureOf } from './document-errors';
import { UploadDialog, UploadDialogData } from './upload-dialog/upload-dialog';

// While a document is waiting or being read, the list asks again this often, so staff
// see it become ready (or fail) without reloading the page.
const refreshWhileProcessingMs = 3000;

const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' });

@Component({
  imports: [MatButtonModule, MatMenuModule, MatProgressSpinnerModule, MatTableModule],
  selector: 'app-documents',
  styleUrl: './documents.scss',
  templateUrl: './documents.html',
})
export class Documents {
  private readonly api = inject(DocumentsApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly accept = documentLimits.accept;
  protected readonly columns = [
    'title',
    'unit',
    'kind',
    'status',
    'version',
    'chunks',
    'updated',
    'actions',
  ];

  protected readonly documents = signal<DocumentSummary[]>([]);
  protected readonly loaded = signal(false);
  protected readonly loadFailed = signal(false);

  protected readonly inProgress = computed(
    () =>
      this.documents().filter((d) => d.status === 'Pending' || d.status === 'Processing').length,
  );

  // The document a new file is being chosen for (the shared hidden file input).
  private replaceTarget: DocumentSummary | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
    void this.load();
  }

  protected async load(): Promise<void> {
    clearTimeout(this.refreshTimer);

    try {
      this.documents.set(await firstValueFrom(this.api.list()));
      this.loadFailed.set(false);
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loaded.set(true);
    }

    if (this.inProgress() > 0) {
      this.refreshTimer = setTimeout(() => void this.load(), refreshWhileProcessingMs);
    }
  }

  protected openUpload(): void {
    const knownUnits = [...new Set(this.documents().map((d) => d.unit))];

    this.dialog
      .open<UploadDialog, UploadDialogData, DocumentCreated>(UploadDialog, {
        data: { knownUnits },
        width: '36rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((created) => {
        if (created) {
          this.snackBar.open('Belge yüklendi; birazdan aranabilir olacak.', 'Tamam', {
            duration: 5000,
          });
          void this.load();
        }
      });
  }

  protected chooseNewVersion(document: DocumentSummary, input: HTMLInputElement): void {
    this.replaceTarget = document;
    input.value = '';
    input.click();
  }

  protected async uploadNewVersion(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    const target = this.replaceTarget;
    this.replaceTarget = null;

    if (!file || !target) {
      return;
    }

    if (file.size > documentLimits.maxSizeBytes) {
      this.snackBar.open(tooLargeMessage, 'Kapat', { duration: 6000 });
      return;
    }

    try {
      const result = await firstValueFrom(this.api.replaceFile(target.id, file));
      this.snackBar.open(`"${target.title}" için ${result.version}. sürüm yüklendi.`, 'Tamam', {
        duration: 5000,
      });
    } catch (error) {
      const failure = uploadFailureOf(error);
      this.snackBar.open(
        failure.fields['file'] ?? failure.message ?? 'Yeni sürüm yüklenemedi.',
        'Kapat',
        {
          duration: 8000,
        },
      );
    }

    void this.load();
  }

  protected confirmDelete(document: DocumentSummary): void {
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Belge silinsin mi?',
          message: `"${document.title}" ve aramadaki bütün parçaları kalıcı olarak silinecek. Bu işlem geri alınamaz.`,
          confirmLabel: 'Sil',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (confirmed) {
          void this.delete(document);
        }
      });
  }

  private async delete(document: DocumentSummary): Promise<void> {
    try {
      await firstValueFrom(this.api.delete(document.id));
      this.snackBar.open(`"${document.title}" silindi.`, 'Tamam', { duration: 5000 });
    } catch (error) {
      this.snackBar.open(actionFailureMessage(error, 'Silme'), 'Kapat', { duration: 8000 });
    }

    void this.load();
  }

  protected fileUrl(document: DocumentSummary): string {
    return this.api.fileUrl(document.id);
  }

  // Methods rather than indexing the label maps in the template, where table rows are untyped.
  protected kindLabel(document: DocumentSummary): string {
    return documentKindLabels[document.kind] ?? document.kind;
  }

  protected statusLabel(document: DocumentSummary): string {
    return documentStatusLabels[document.status] ?? document.status;
  }

  protected formatDate(iso: string): string {
    return dateFormat.format(new Date(iso));
  }

  protected formatSize(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${Math.max(1, Math.round(bytes / 1024))} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
