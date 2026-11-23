import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Rating } from './chat';

// The API's /api/analytics endpoints (backend: AnalyticsEndpoints.cs, PLANNING D5).

// Dates as yyyy-MM-dd, both included, in the school's time zone.
export interface AnalyticsPeriod {
  from: string;
  to: string;
}

export interface AnalyticsDay {
  date: string;
  questions: number;
  answered: number;
  notFound: number;
  up: number;
  down: number;
}

export interface AnalyticsSummary {
  period: AnalyticsPeriod;
  questions: number;
  answered: number;
  notFound: number;
  up: number;
  down: number;
  days: AnalyticsDay[];
}

export interface AnalyticsList<T> {
  period: AnalyticsPeriod;
  total: number;
  items: T[];
}

export interface AnsweredQuestion {
  conversationId: string;
  messageId: string;
  question: string;
  answer: string;
  outcome: 'Answered' | 'NotFound';
  feedback: Rating | null;
  feedbackComment: string | null;
  answeredAt: string;
}

export interface TopDocument {
  documentId: string;
  title: string;
  unit: string;
  deleted: boolean;
  answers: number;
}

export interface TopQuestion {
  question: string;
  count: number;
  lastAskedAt: string;
}

export interface StaffSource {
  number: number;
  documentId: string;
  title: string;
  unit: string;
  sectionPath: string | null;
  pageNumber: number | null;
}

export interface StaffMessage {
  id: string;
  role: 'User' | 'Assistant';
  content: string;
  outcome: 'Answered' | 'NotFound' | null;
  sources: StaffSource[];
  model: string | null;
  durationMs: number | null;
  feedback: Rating | null;
  feedbackComment: string | null;
  createdAt: string;
}

export interface StaffConversation {
  conversationId: string;
  createdAt: string;
  messages: StaffMessage[];
}

@Injectable({ providedIn: 'root' })
export class AnalyticsApi {
  private readonly http = inject(HttpClient);

  summary(period: AnalyticsPeriod): Observable<AnalyticsSummary> {
    return this.http.get<AnalyticsSummary>('/api/analytics/summary', { params: periodParams(period) });
  }

  unanswered(period: AnalyticsPeriod, limit: number): Observable<AnalyticsList<AnsweredQuestion>> {
    return this.list('unanswered', period, limit);
  }

  negativeFeedback(period: AnalyticsPeriod, limit: number): Observable<AnalyticsList<AnsweredQuestion>> {
    return this.list('negative-feedback', period, limit);
  }

  topDocuments(period: AnalyticsPeriod, limit: number): Observable<AnalyticsList<TopDocument>> {
    return this.list('top-documents', period, limit);
  }

  topQuestions(period: AnalyticsPeriod, limit: number): Observable<AnalyticsList<TopQuestion>> {
    return this.list('top-questions', period, limit);
  }

  conversation(conversationId: string): Observable<StaffConversation> {
    return this.http.get<StaffConversation>(`/api/analytics/conversations/${encodeURIComponent(conversationId)}`);
  }

  private list<T>(name: string, period: AnalyticsPeriod, limit: number): Observable<AnalyticsList<T>> {
    return this.http.get<AnalyticsList<T>>(`/api/analytics/${name}`, {
      params: periodParams(period).set('limit', limit),
    });
  }
}

function periodParams(period: AnalyticsPeriod): HttpParams {
  return new HttpParams().set('from', period.from).set('to', period.to);
}
