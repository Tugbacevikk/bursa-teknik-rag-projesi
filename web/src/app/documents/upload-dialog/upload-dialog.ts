import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import {
  DocumentCreated,
  DocumentKind,
  DocumentsApi,
  documentKindLabels,
  documentLimits,
} from '../../api/documents';
import { tooLargeMessage, uploadFailureOf } from '../document-errors';

export interface UploadDialogData {
  // Units already used by other documents, offered as suggestions so the same office is
  // not spelled three ways.
  knownUnits: string[];
}

// Offered even before any document uses them.
const commonUnits = ['İMEP Koordinatörlüğü', 'Öğrenci İşleri Daire Başkanlığı'];

// Upload form in a dialog. Closes with the created document, or with nothing when cancelled.
@Component({
  imports: [
    MatAutocompleteModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    ReactiveFormsModule,
  ],
  selector: 'app-upload-dialog',
  styleUrl: './upload-dialog.scss',
  templateUrl: './upload-dialog.html',
})
export class UploadDialog {
  private readonly api = inject(DocumentsApi);
  private readonly dialogRef = inject<MatDialogRef<UploadDialog, DocumentCreated>>(MatDialogRef);
  private readonly data = inject<UploadDialogData>(MAT_DIALOG_DATA);

  protected readonly limits = documentLimits;
  protected readonly kinds = Object.entries(documentKindLabels) as [DocumentKind, string][];

  protected readonly form = inject(NonNullableFormBuilder).group({
    title: ['', [Validators.required, Validators.maxLength(documentLimits.titleMaxLength)]],
    unit: ['', [Validators.required, Validators.maxLength(documentLimits.unitMaxLength)]],
    kind: ['' as DocumentKind | '', Validators.required],
    sourceUrl: [
      '',
      [
        Validators.maxLength(documentLimits.sourceUrlMaxLength),
        Validators.pattern(/^https?:\/\/\S+$/),
      ],
    ],
    validityPeriod: ['', Validators.maxLength(documentLimits.validityPeriodMaxLength)],
  });

  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);

  private readonly unitText = signal('');
  protected readonly unitSuggestions = computed(() => {
    const typed = this.unitText().toLocaleLowerCase('tr');
    const all = [...new Set([...commonUnits, ...this.data.knownUnits])].sort((a, b) =>
      a.localeCompare(b, 'tr'),
    );
    return all.filter((unit) => unit.toLocaleLowerCase('tr').includes(typed));
  });

  constructor() {
    this.form.controls.unit.valueChanges.subscribe((value) => this.unitText.set(value));
  }

  protected chooseFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = input.files?.[0] ?? null;
    this.file.set(chosen);
    this.fileError.set(
      chosen && chosen.size > documentLimits.maxSizeBytes ? tooLargeMessage : null,
    );

    // The file name is a good first guess for the title; staff usually tidy it up.
    if (chosen && !this.form.controls.title.value) {
      this.form.controls.title.setValue(chosen.name.replace(/\.[^.]+$/, '').replace(/[_-]+/g, ' '));
    }
  }

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    const file = this.file();
    if (!file) {
      this.fileError.set('Bir dosya seçin.');
    }

    if (this.form.invalid || !file || this.fileError()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    try {
      const value = this.form.getRawValue();
      const created = await firstValueFrom(
        this.api.upload({
          file,
          title: value.title.trim(),
          unit: value.unit.trim(),
          kind: value.kind as DocumentKind,
          sourceUrl: value.sourceUrl.trim(),
          validityPeriod: value.validityPeriod.trim(),
        }),
      );
      this.dialogRef.close(created);
    } catch (error) {
      this.showFailure(uploadFailureOf(error));
    } finally {
      this.submitting.set(false);
    }
  }

  // The server's message goes under the field it is about; anything else on top.
  private showFailure(failure: ReturnType<typeof uploadFailureOf>): void {
    for (const [name, message] of Object.entries(failure.fields)) {
      if (name === 'file') {
        this.fileError.set(message);
        continue;
      }

      const control = this.form.get(name);
      if (control) {
        control.setErrors({ server: message });
        control.markAsTouched();
      } else {
        this.failure.set(message);
      }
    }

    if (failure.message) {
      this.failure.set(failure.message);
    }
  }
}
