import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// The API's /api/documents endpoints (backend: DocumentEndpoints.cs).

export type DocumentKind =
  'Regulation' | 'Faq' | 'Form' | 'Announcement' | 'AcademicCalendar' | 'Other';

export type DocumentStatus = 'Pending' | 'Processing' | 'Done' | 'Failed';

export interface DocumentSummary {
  id: string;
  title: string;
  unit: string;
  kind: DocumentKind;
  status: DocumentStatus;
  version: number;
  fileName: string;
  sizeBytes: number;
  chunkCount: number;
  // Failed: why. Pending: why it was postponed (e.g. the AI service was down). Else null.
  failureReason: string | null;
  updatedAt: string;
}

export interface UploadDocumentRequest {
  file: File;
  title: string;
  unit: string;
  kind: DocumentKind;
  sourceUrl: string;
  validityPeriod: string;
}

export interface DocumentCreated {
  id: string;
  status: DocumentStatus;
  version: number;
}

// 409 body: the same file is already there.
export interface DuplicateDocument {
  error: string;
  existingDocumentId: string;
  existingTitle: string;
}

// The same limits as the server (DocumentConfiguration, DocumentFileInspector).
export const documentLimits = {
  maxSizeBytes: 20 * 1024 * 1024,
  titleMaxLength: 300,
  unitMaxLength: 100,
  sourceUrlMaxLength: 2048,
  validityPeriodMaxLength: 50,
  // For the file picker only; the server decides from the file's contents.
  accept: '.pdf,.docx,.md,.markdown,.txt',
} as const;

export const documentKindLabels: Record<DocumentKind, string> = {
  Regulation: 'Yönetmelik / Yönerge',
  Faq: 'Sıkça Sorulan Sorular',
  Form: 'Form / Dilekçe',
  Announcement: 'Duyuru',
  AcademicCalendar: 'Akademik Takvim',
  Other: 'Diğer',
};

export const documentStatusLabels: Record<DocumentStatus, string> = {
  Pending: 'Sırada',
  Processing: 'İşleniyor',
  Done: 'Hazır',
  Failed: 'Hata',
};

@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);

  list(): Observable<DocumentSummary[]> {
    return this.http.get<DocumentSummary[]>('/api/documents');
  }

  upload(request: UploadDocumentRequest): Observable<DocumentCreated> {
    const form = new FormData();
    form.append('file', request.file);
    form.append('title', request.title);
    form.append('unit', request.unit);
    form.append('kind', request.kind);
    form.append('sourceUrl', request.sourceUrl);
    form.append('validityPeriod', request.validityPeriod);

    return this.http.post<DocumentCreated>('/api/documents', form);
  }

  replaceFile(id: string, file: File): Observable<DocumentCreated> {
    const form = new FormData();
    form.append('file', file);

    return this.http.put<DocumentCreated>(`/api/documents/${id}/file`, form);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/documents/${id}`);
  }

  // A plain link: the browser downloads it with the session cookie, and the API sends it
  // as an attachment.
  fileUrl(id: string): string {
    return `/api/documents/${id}/file`;
  }
}
