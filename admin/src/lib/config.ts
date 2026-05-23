const getBaseUrl = () => {
  if (typeof window !== 'undefined') {
    const host = window.location.hostname;
    return `http://${host}:9000`;
  }
  return 'http://localhost:9000';
};

export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? `${getBaseUrl()}/api/v1`;

export const WS_BASE_URL =
  import.meta.env.VITE_WS_BASE_URL ?? getBaseUrl();
