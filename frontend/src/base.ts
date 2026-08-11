// Captured at bundle load: where did this script come from?
const src = (document.currentScript as HTMLScriptElement | null)?.src;
export const scriptOrigin =
  src && new URL(src).origin !== location.origin ? new URL(src).origin : '';

let apiBase = scriptOrigin;
export function setApiBase(base: string | null) {
  apiBase = (base ?? scriptOrigin).replace(/\/$/, '');
}
/** Prefix a server path (API call, thumbnail URL, /harmoniq) with the API base. */
export function apiUrl(path: string): string {
  return apiBase + path;
}
