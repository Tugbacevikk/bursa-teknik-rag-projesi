import { problemOf } from '../api/problem';

// The API's own words for a refused account change (400: e.g. the Admin's own account;
// 409: the email is taken, or the last active Admin), else the fallback. A 400's title is
// the framework's English sentence, so its field messages are used instead.
export function accountErrorMessage(error: unknown, fallback: string): string {
  const problem = problemOf(error);
  const fieldMessage = Object.values(problem?.errors ?? {})[0]?.[0];
  return fieldMessage ?? (problem?.status === 409 ? problem.title : undefined) ?? fallback;
}
