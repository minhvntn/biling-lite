export async function fetchWithAuth(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const headers = new Headers(init?.headers);
  headers.set('X-API-KEY', 'SmbSecureLANKey2026');
  
  return fetch(input, {
    ...init,
    headers,
  });
}
