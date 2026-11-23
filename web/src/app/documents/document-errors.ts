import { HttpErrorResponse } from '@angular/common/http';
import { DuplicateDocument, documentLimits } from '../api/documents';
import { problemOf } from '../api/problem';

// What went wrong with an upload or a new version, split the way the form shows it: a
// message per field (the API's own Turkish wording) and one for the form as a whole.
export interface UploadFailure {
  fields: Record<string, string>;
  message: string | null;
}

export function uploadFailureOf(error: unknown): UploadFailure {
  if (!(error instanceof HttpErrorResponse)) {
    return { fields: {}, message: genericMessage };
  }

  switch (error.status) {
    case 400: {
      const fields: Record<string, string> = {};
      for (const [name, messages] of Object.entries(problemOf(error)?.errors ?? {})) {
        fields[name] = messages[0];
      }
      return { fields, message: null };
    }
    case 409: {
      const duplicate = error.error as DuplicateDocument | null;
      return {
        fields: {},
        message: duplicate?.existingTitle
          ? `Bu dosya daha önce "${duplicate.existingTitle}" adıyla yüklenmiş.`
          : 'Bu dosya daha önce yüklenmiş.',
      };
    }
    // Refused by the server before the endpoint saw it (request size limit).
    case 413:
      return { fields: { file: tooLargeMessage }, message: null };
    case 0:
      return { fields: {}, message: unreachableMessage };
  }

  return { fields: {}, message: genericMessage };
}

// For actions without a form (deleting); the 401 case never gets here, the session
// interceptor has already sent the user to the login screen.
export function actionFailureMessage(error: unknown, action: string): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 404) {
      return 'Belge bulunamadı; başka biri silmiş olabilir.';
    }
    if (error.status === 0) {
      return unreachableMessage;
    }
  }

  return `${action} başarısız oldu. Biraz sonra yeniden deneyin.`;
}

export const tooLargeMessage = `Dosya ${documentLimits.maxSizeBytes / (1024 * 1024)} MB'tan büyük.`;

const unreachableMessage = 'Sunucuya ulaşılamıyor. Bağlantınızı kontrol edip yeniden deneyin.';
const genericMessage = 'Belge yüklenemedi. Biraz sonra yeniden deneyin.';
