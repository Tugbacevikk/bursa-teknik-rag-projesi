/** Shape of the API's /health response (see backend HealthResponseWriter). */
export interface HealthReport {
  status: HealthStatusValue;
  checks: Record<string, HealthCheckEntry>;
}

export interface HealthCheckEntry {
  status: HealthStatusValue;
  description: string | null;
  durationMs: number;
}

export type HealthStatusValue = 'Healthy' | 'Degraded' | 'Unhealthy';

export function isHealthReport(value: unknown): value is HealthReport {
  return typeof value === 'object' && value !== null && 'status' in value && 'checks' in value;
}
