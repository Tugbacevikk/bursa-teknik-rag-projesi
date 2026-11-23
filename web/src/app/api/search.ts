import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// The API's POST /api/search (backend: SearchEndpoints.cs). Staff only.

export type SearchMethod = 'Hybrid' | 'Keyword' | 'Vector';

export interface SearchRequest {
  question: string;
  method?: SearchMethod;
  useReranker?: boolean;
  resultCount?: number;
}

export interface SearchHit {
  chunkId: string;
  documentId: string;
  documentTitle: string;
  unit: string;
  sourceUrl: string | null;
  validityPeriod: string | null;
  sectionPath: string | null;
  pageNumber: number | null;
  // What the document says, without the context header.
  text: string;
  score: number;
  // 1-based position in each search; null when that search did not return the chunk.
  keywordRank: number | null;
  vectorRank: number | null;
}

export interface SearchResponse {
  durationMs: number;
  vectorSearchFailed: boolean;
  reranked: boolean;
  hits: SearchHit[];
}

// The same limit as the server (SearchOptions.MaxQuestionLength).
export const maxQuestionLength = 2000;

export const searchMethodLabels: Record<SearchMethod, string> = {
  Hybrid: 'Hibrit (öğrencilerin kullandığı)',
  Keyword: 'Yalnızca anahtar kelime',
  Vector: 'Yalnızca anlam (vektör)',
};

@Injectable({ providedIn: 'root' })
export class SearchApi {
  private readonly http = inject(HttpClient);

  search(request: SearchRequest): Observable<SearchResponse> {
    return this.http.post<SearchResponse>('/api/search', request);
  }
}
