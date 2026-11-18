import { createContext, useContext, useEffect, useState } from 'react';
import api, { clearTokens, storeTokens } from '../api/client';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [shop, setShop] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    const token = localStorage.getItem('washtag_token');
    if (!token) {
      setLoading(false);
      return () => { active = false; };
    }

    api.get('/auth/me')
      .then(({ data }) => {
        if (!active) return;
        setUser(data.user);
        setShop(data.shop);
      })
      .catch(() => clearTokens())
      .finally(() => { if (active) setLoading(false); });

    return () => { active = false; };
  }, []);

  const storeSession = (data) => {
    storeTokens(data);
    setUser(data.user);
    setShop(data.shop);
  };

  const login = async (email, password) => {
    const { data } = await api.post('/auth/login', { email, password });
    storeSession(data);
    return data;
  };

  const register = async (payload) => {
    const { data } = await api.post('/auth/register', payload);
    storeSession(data);
    return data;
  };

  const logout = () => {
    clearTokens();
    setUser(null);
    setShop(null);
  };

  return (
    <AuthContext.Provider
      value={{ user, shop, loading, login, register, logout, setShop, isAuthenticated: !!user }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
}
