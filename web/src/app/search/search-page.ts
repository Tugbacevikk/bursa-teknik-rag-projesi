import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { problemOf } from '../api/problem';
import {
  SearchApi,
  SearchHit,
  SearchMethod,
  SearchResponse,
  maxQuestionLength,
  searchMethodLabels,
} from '../api/search';

// Lets staff try a question and see which parts of which documents it finds, before
// students ask it (PLANNING S7). Shows the same search the chat uses.
@Component({
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    ReactiveFormsModule,
  ],
  selector: 'app-search-page',
  styleUrl: './search-page.scss',
  templateUrl: './search-page.html',
})
export class SearchPage {
  private readonly api = inject(SearchApi);

  protected readonly maxLength = maxQuestionLength;
  protected readonly methods = Object.entries(searchMethodLabels) as [SearchMethod, string][];

  protected readonly form = inject(NonNullableFormBuilder).group({
    question: ['', [Validators.required, Validators.maxLength(maxQuestionLength)]],
    method: ['Hybrid' as SearchMethod],
  });

  protected readonly searching = signal(false);
  protected readonly result = signal<SearchResponse | null>(null);

  // The method the shown result came from; the form's value may have changed since.
  protected readonly searchedMethod = signal<SearchMethod>('Hybrid');
  protected readonly failure = signal<string | null>(null);

  // Hits whose full text is shown; the rest show the first lines.
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected async search(): Promise<void> {
    if (this.searching()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.searching.set(true);
    this.failure.set(null);

    try {
      const { question, method } = this.form.getRawValue();
      this.result.set(await firstValueFrom(this.api.search({ question: question.trim(), method })));
      this.searchedMethod.set(method);
      this.expanded.set(new Set());
    } catch (error) {
      this.result.set(null);
      this.failure.set(messageFor(error));
    } finally {
      this.searching.set(false);
    }
  }

  // Enter searches; Shift+Enter starts a new line.
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.search();
    }
  }

  protected toggle(hit: SearchHit): void {
    const next = new Set(this.expanded());
    if (!next.delete(hit.chunkId)) {
      next.add(hit.chunkId);
    }
    this.expanded.set(next);
  }

  protected isExpanded(hit: SearchHit): boolean {
    return this.expanded().has(hit.chunkId);
  }

  // A long text is cut to its start until expanded; short ones are shown whole.
  protected isLong(hit: SearchHit): boolean {
    return hit.text.length > 400;
  }

  protected location(hit: SearchHit): string {
    const parts = [hit.sectionPath, hit.pageNumber ? `s. ${hit.pageNumber}` : null];
    return parts.filter(Boolean).join(' · ');
  }
}

function messageFor(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 400) {
      const first = Object.values(problemOf(error)?.errors ?? {})[0]?.[0];
      return first ?? 'Soru geçersiz.';
    }
    if (error.status === 0) {
      return 'Sunucuya ulaşılamıyor. Bağlantınızı kontrol edip yeniden deneyin.';
    }
  }

  return 'Arama yapılamadı. Biraz sonra yeniden deneyin.';
}
