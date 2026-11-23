import { HttpErrorResponse } from '@angular/common/http';
import { accountErrorMessage } from './account-errors';

const problem = (status: number, error: object | null) => new HttpErrorResponse({ status, error });

describe('accountErrorMessage', () => {
  // A 400's title is the framework's English sentence; the field message is the Turkish one.
  it('uses the field message of a 400', () => {
    const error = problem(400, {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { email: ['Geçerli bir e-posta adresi girin.'] },
    });

    expect(accountErrorMessage(error, 'olmadı')).toBe('Geçerli bir e-posta adresi girin.');
  });

  it("uses the title of a 409 (email taken, last Admin)", () => {
    expect(accountErrorMessage(problem(409, { title: 'En az bir aktif Admin hesabı kalmalıdır.', status: 409 }), 'olmadı')).toBe(
      'En az bir aktif Admin hesabı kalmalıdır.',
    );
  });

  it('falls back when there is nothing to show', () => {
    expect(accountErrorMessage(problem(500, null), 'Değişiklik kaydedilemedi.')).toBe('Değişiklik kaydedilemedi.');
    expect(accountErrorMessage(new Error('network'), 'Değişiklik kaydedilemedi.')).toBe('Değişiklik kaydedilemedi.');
  });
});
