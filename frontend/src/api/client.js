import axios from 'axios';

const api = axios.create({
  baseURL: '/api',
  timeout: 20000,
  headers: { 'Content-Type': 'application/json' }
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('washtag_token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    const message =
      error.response?.data?.message ||
      (error.response?.status === 404 ? 'Not found.' : null) ||
      error.message ||
      'Something went wrong. Please try again.';

    if (error.response?.status === 401 && !window.location.pathname.startsWith('/t/')) {
      localStorage.removeItem('washtag_token');
      if (window.location.pathname.startsWith('/app')) window.location.replace('/login');
    }

    return Promise.reject(new Error(message));
  }
);

export default api;
