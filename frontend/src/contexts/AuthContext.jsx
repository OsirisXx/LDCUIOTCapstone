import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef
} from 'react';
import axios from 'axios';
import toast from 'react-hot-toast';
import supabase from '../lib/supabaseClient';

const AuthContext = createContext();

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};

// Configure axios defaults
const API_BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5000';
axios.defaults.baseURL = API_BASE_URL;

const SUPABASE_REDIRECT_URL =
  process.env.REACT_APP_SUPABASE_REDIRECT_URL || window.location.origin;

export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const lastSupabaseTokenRef = useRef(null);

  const clearSession = useCallback(() => {
    localStorage.removeItem('token');
    delete axios.defaults.headers.common['Authorization'];
    setUser(null);
  }, []);

  useEffect(() => {
    const token = localStorage.getItem('token');
    if (token) {
      axios.defaults.headers.common['Authorization'] = `Bearer ${token}`;
    }
  }, []);

  const exchangeSupabaseSession = useCallback(
    async (session) => {
      const accessToken = session?.access_token;
      if (!accessToken) {
        clearSession();
        return;
      }

      if (lastSupabaseTokenRef.current === accessToken && user) {
        return;
      }

      try {
        const response = await axios.post('/api/auth/supabase', {
          accessToken,
          refreshToken: session.refresh_token
        });

        const { token, user: userData } = response.data;
        
        // Double-check role before setting user (security layer)
        if (userData.role !== 'admin' && userData.role !== 'superadmin') {
          console.warn('User does not have admin privileges, rejecting session');
          clearSession();
          await supabase.auth.signOut();
          toast.error('This account does not have administrator privileges.');
          setLoading(false);
          window.location.replace('/login');
          return;
        }
        
        localStorage.setItem('token', token);
        axios.defaults.headers.common['Authorization'] = `Bearer ${token}`;
        lastSupabaseTokenRef.current = accessToken;
        setUser(userData);
      } catch (error) {
        console.error('Supabase session exchange failed:', error);
        console.error('Error response:', error.response?.data);
        console.error('Error status:', error.response?.status);
        
        // For 403 (Forbidden) errors, handle specially
        if (error.response?.status === 403) {
          const message = error.response?.data?.message || 
            'This account does not have administrator privileges.';
          toast.error(message);
          clearSession();
          await supabase.auth.signOut();
          // Ensure user state is cleared
          setUser(null);
          setLoading(false);
          window.location.replace('/login');
          return;
        }
        
        // For other errors
        const message =
          error.response?.data?.message || 
          error.response?.data?.error ||
          error.message || 
          'Authentication failed. Please try again.';
        toast.error(message);
        clearSession();
        await supabase.auth.signOut();
        setUser(null);
        setLoading(false);
        window.location.replace('/login');
      }
    },
    [clearSession, user]
  );

  useEffect(() => {
    let isMounted = true;

    const initialiseSession = async () => {
      setLoading(true);
      try {
        // First, check if there's an existing token and verify it's valid
        const existingToken = localStorage.getItem('token');
        if (existingToken) {
          try {
            const verifyResponse = await axios.get('/api/auth/verify');
            const verifiedUser = verifyResponse.data?.user;
            
            // If token exists but user is not admin/superadmin, clear it
            if (verifiedUser && verifiedUser.role !== 'admin' && verifiedUser.role !== 'superadmin') {
              console.warn('Existing token belongs to non-admin user, clearing session');
              clearSession();
              await supabase.auth.signOut();
              if (isMounted) {
                setLoading(false);
              }
              return;
            }
          } catch (verifyError) {
            // Token is invalid or expired, clear it
            console.warn('Existing token is invalid, clearing session');
            clearSession();
          }
        }
        
        const {
          data: { session }
        } = await supabase.auth.getSession();

        if (!isMounted) {
          return;
        }

        if (session) {
          await exchangeSupabaseSession(session);
        } else {
          // No Supabase session, ensure we clear any stale local token
          clearSession();
        }
      } catch (error) {
        console.error('Initial auth check failed:', error);
        toast.error('Unable to verify session. Please sign in again.');
        clearSession();
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    initialiseSession();

    const {
      data: { subscription }
    } = supabase.auth.onAuthStateChange(async (event, session) => {
      if (!isMounted) {
        return;
      }

      if (event === 'SIGNED_OUT' || !session) {
        clearSession();
        setLoading(false);
        return;
      }

      setLoading(true);
      await exchangeSupabaseSession(session);
      setLoading(false);
    });

    return () => {
      isMounted = false;
      subscription.unsubscribe();
    };
  }, [clearSession, exchangeSupabaseSession]);

  const login = useCallback(async () => {
    setLoading(true);
    try {
      const { error } = await supabase.auth.signInWithOAuth({
        provider: 'google',
        options: {
          redirectTo: SUPABASE_REDIRECT_URL,
          queryParams: {
            prompt: 'select_account'
          }
        }
      });

      if (error) {
        throw error;
      }
    } catch (error) {
      console.error('Google sign-in failed:', error);
      const message = error.message || 'Unable to sign in with Google.';
      toast.error(message);
      setLoading(false);
    }
  }, []);

  const logout = useCallback(async () => {
    try {
      await axios.post('/api/auth/logout');
    } catch (error) {
      console.error('Logout error:', error);
    } finally {
      await supabase.auth.signOut();
      clearSession();
      toast.success('Logged out successfully');
    }
  }, [clearSession]);

  const changePassword = useCallback(
    async (currentPassword, newPassword) => {
      try {
        await axios.post('/api/auth/change-password', {
          currentPassword,
          newPassword
        });
        toast.success('Password changed successfully');
        return { success: true };
      } catch (error) {
        const message = error.response?.data?.message || 'Password change failed';
        toast.error(message);
        return { success: false, message };
      }
    },
    []
  );

  const value = {
    user,
    loading,
    login,
    logout,
    changePassword
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};