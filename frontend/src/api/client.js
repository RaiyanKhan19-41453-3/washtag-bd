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

export function clearTokens() {
  localStorage.removeItem('washtag_token');
  localStorage.removeItem('washtag_refresh_token');
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
  const { data } = await authApi.post('/auth/refresh', { refreshToken });
  storeTokens(data);
  return data;
}

function redirectToLogin() {
  clearTokens();
  if (window.location.pathname.startsWith('/app')) window.location.replace('/login');
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const config = error.config || {};
    const url = config.url || '';
    const isAuthCall = url.includes('/auth/login') || url.includes('/auth/register') || url.includes('/auth/refresh');
    const status = error.response?.status;

    if (status !== 401 || isAuthCall || config._retry || window.location.pathname.startsWith('/t/') || window.location.pathname.startsWith('/track/')) {
      const data = error.response?.data;
      const validation = data?.errors
        ? Object.values(data.errors).flat().join(', ')
        : null;
      const message = data?.message || data?.title || validation || error.message || 'Something went wrong. Please try again.';
      return Promise.reject(new Error(message));
    }

    config._retry = true;
    try {
      if (!refreshInFlight) {
        refreshInFlight = refreshTokens().finally(() => { refreshInFlight = null; });
      }
      await refreshInFlight;
      config.headers.Authorization = `Bearer ${localStorage.getItem('washtag_token')}`;
      return api(config);
    } catch {
      redirectToLogin();
      return Promise.reject(new Error('Your session expired. Please log in again.'));
    }
  }
);

export default api;
