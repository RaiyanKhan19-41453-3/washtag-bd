import axios from 'axios';

const api = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' }
});

const authApi = axios.create({
  baseURL: '/api',
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' }
});

export function storeTokens({ token, refreshToken }) {
  if (token) localStorage.setItem('washtag_token', token);
  if (refreshToken) localStorage.setItem('washtag_refresh_token', refreshToken);
}

export function sessionSecret() {
  let value = localStorage.getItem('washtag_session_secret');
  if (!value) {
    value = crypto.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    localStorage.setItem('washtag_session_secret', value);
  }
  return value;
}

export function clearTokens() {
  localStorage.removeItem('washtag_token');
  localStorage.removeItem('washtag_refresh_token');
  localStorage.removeItem('washtag_session_secret');
}

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('washtag_token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

let refreshInFlight = null;

async function refreshTokens() {
  const refreshToken = localStorage.getItem('washtag_refresh_token');
  if (!refreshToken) throw new Error('No refresh token');
  const { data } = await authApi.post('/auth/refresh', { refreshToken, sessionSecret: sessionSecret() });
  storeTokens(data);
  return data;
}

function redirectToLogin() {
  clearTokens();
  if (window.location.pathname.startsWith('/app')) {
    sessionStorage.setItem('washtag_from', window.location.pathname + window.location.search);
    window.location.replace('/login');
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const config = error.config || {};
    const url = config.url || '';
    const isAuthCall = url.includes('/auth/login') || url.includes('/auth/register') || url.includes('/auth/refresh');
    const status = error.response?.status;

    // The backend locks a shop by returning 403. Tell AuthContext to re-read the
    // profile so the renewal banner appears even if the lock happened mid-session.
    const responseMessage = error.response?.data?.message;
    if (status === 403 && typeof responseMessage === 'string' &&
        responseMessage.toLowerCase().includes('subscription is inactive')) {
      window.dispatchEvent(new CustomEvent('washtag:subscription-locked'));
    }

    if (status !== 401 || isAuthCall || config._retry || window.location.pathname.startsWith('/t/') || window.location.pathname.startsWith('/track/')) {
      const data = error.response?.data;
      const validation = data?.errors
        ? Object.values(data.errors).flat().join(', ')
        : null;
      const message = data?.message || validation || data?.title ||
        (status === 403 ? 'You do not have permission to do that.' : null) ||
        error.message || 'Something went wrong. Please try again.';
      const failure = new Error(message);
      failure.status = status;
      failure.response = error.response;
      return Promise.reject(failure);
    }

    // Never replay a non-idempotent write after a 401 — it may have committed.
    if (!['get', 'head', 'options'].includes((config.method || 'get').toLowerCase())) {
      redirectToLogin();
      return Promise.reject(new Error('Your session expired. Please log in again.'));
    }

    config._retry = true;
    try {
      if (!refreshInFlight) {
        refreshInFlight = refreshTokens().finally(() => { refreshInFlight = null; });
      }
      await refreshInFlight;
      config.headers.Authorization = `Bearer ${localStorage.getItem('washtag_token')}`;
      return api(config);
    } catch (refreshError) {
      const status = refreshError?.response?.status ?? refreshError?.status;
      if (status === 400 || status === 401) {
        // The token itself is rejected (e.g. revoked family) — clear it rather than
        // leaving the tab stuck retrying a dead token forever.
        redirectToLogin();
        return Promise.reject(new Error('Your session expired. Please log in again.'));
      }
      // The refresh can fail because the API/database is briefly unavailable. Keep
      // stored tokens so a transient outage does not log every user out.
      return Promise.reject(new Error('Could not refresh your session. Please retry in a moment.'));
    }
  }
);

export default api;
