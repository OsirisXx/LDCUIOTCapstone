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
const API_BASE_URL = 'http://172.72.100.126:5000';
axios.defaults.baseURL = API_BASE_URL;

const SUPABASE_REDIRECT_URL =
  process.env.REACT_APP_SUPABASE_REDIRECT_URL || window.location.origin;

export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const lastSupabaseTokenRef = useRef(null);
  const isInitializingRef = useRef(false);

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

      if (lastSupabaseTokenRef.current === accessToken) {
        console.log('🔄 [exchangeSupabaseSession] Same token, skipping exchange');
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
    [clearSession]
  );

  useEffect(() => {
    let isMounted = true;

    const initialiseSession = async () => {
      // Prevent multiple simultaneous initializations
      if (isInitializingRef.current) {
        console.log('🔄 [AuthContext] Already initializing, skipping...');
        return;
      }
      
      isInitializingRef.current = true;
      console.log('🔄 [AuthContext] Initializing session check...');
      setLoading(true);
      try {
        // First, check if there's an existing token and verify it's valid
        const existingToken = localStorage.getItem('token');
        console.log('🔄 [AuthContext] Existing token found:', existingToken ? 'Yes' : 'No');
        
        if (existingToken) {
          try {
            console.log('🔄 [AuthContext] Verifying existing token...');
            const verifyResponse = await axios.get('/api/auth/verify');
            const verifiedUser = verifyResponse.data?.user;
            console.log('🔄 [AuthContext] Token verified, user:', verifiedUser);
            
            // If token exists but user is not admin/superadmin, clear it
            if (verifiedUser && verifiedUser.role !== 'admin' && verifiedUser.role !== 'superadmin') {
              console.warn('❌ [AuthContext] Existing token belongs to non-admin user, clearing session');
              clearSession();
              await supabase.auth.signOut();
              if (isMounted) {
                setLoading(false);
              }
              return;
            }
            
            // Token is valid and user is admin/superadmin, set the user
            if (verifiedUser) {
              console.log('✅ [AuthContext] Valid admin token found, setting user');
              setUser(verifiedUser);
              if (isMounted) {
                setLoading(false);
              }
              return;
            }
          } catch (verifyError) {
            // Token is invalid or expired, clear it
            console.warn('❌ [AuthContext] Existing token is invalid, clearing session:', verifyError.message);
            clearSession();
          }
        }
        
        console.log('🔄 [AuthContext] Checking Supabase session...');
        const {
          data: { session }
        } = await supabase.auth.getSession();

        if (!isMounted) {
          console.log('🔄 [AuthContext] Component unmounted, aborting');
          return;
        }

        if (session) {
          console.log('🔄 [AuthContext] Supabase session found, exchanging...');
          await exchangeSupabaseSession(session);
        } else {
          console.log('🔄 [AuthContext] No Supabase session, clearing any stale local token');
          // No Supabase session, ensure we clear any stale local token
          // BUT only if we don't have a valid password-based session
          const tokenStillExists = localStorage.getItem('token');
          if (!tokenStillExists) {
            clearSession();
          }
        }
      } catch (error) {
        console.error('❌ [AuthContext] Initial auth check failed:', error);
        toast.error('Unable to verify session. Please sign in again.');
        clearSession();
      } finally {
        if (isMounted) {
          console.log('🔄 [AuthContext] Session initialization complete, loading=false');
          setLoading(false);
        }
        isInitializingRef.current = false;
      }
    };

    initialiseSession();

    const {
      data: { subscription }
    } = supabase.auth.onAuthStateChange(async (event, session) => {
      console.log('🔔 [AuthContext] Supabase auth state changed:', event, session ? 'Session exists' : 'No session');
      
      if (!isMounted) {
        console.log('🔔 [AuthContext] Component unmounted, ignoring auth state change');
        return;
      }

      if (event === 'SIGNED_OUT' || !session) {
        console.log('🔔 [AuthContext] Supabase SIGNED_OUT or no session');
        
        // Don't clear session if we have a valid password-based token
        const existingToken = localStorage.getItem('token');
        if (existingToken) {
          console.log('🔔 [AuthContext] Password-based token exists, preserving session');
          setLoading(false);
          return;
        }
        
        console.log('🔔 [AuthContext] No token found, clearing session');
        clearSession();
        setLoading(false);
        return;
      }

      console.log('🔔 [AuthContext] Supabase session active, exchanging...');
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

  const loginWithPassword = useCallback(async (email, password) => {
    console.log('🔐 [LoginWithPassword] Starting password login for:', email);
    setLoading(true);
    try {
      console.log('🔐 [LoginWithPassword] Sending POST request to /api/auth/login');
      const response = await axios.post('/api/auth/login', {
        email,
        password
      });

      console.log('🔐 [LoginWithPassword] Response received:', response.data);
      const { token, user: userData } = response.data;
      
      console.log('🔐 [LoginWithPassword] User data:', userData);
      console.log('🔐 [LoginWithPassword] Token received:', token ? 'Yes' : 'No');
      
      // Double-check role before setting user (security layer)
      if (userData.role !== 'admin' && userData.role !== 'superadmin') {
        console.warn('❌ [LoginWithPassword] User does not have admin privileges, rejecting session');
        clearSession();
        toast.error('This account does not have administrator privileges.');
        setLoading(false);
        return { success: false };
      }
      
      console.log('✅ [LoginWithPassword] Role check passed:', userData.role);
      
      localStorage.setItem('token', token);
      console.log('✅ [LoginWithPassword] Token stored in localStorage');
      
      axios.defaults.headers.common['Authorization'] = `Bearer ${token}`;
      console.log('✅ [LoginWithPassword] Authorization header set');
      
      setUser(userData);
      console.log('✅ [LoginWithPassword] User state set:', userData);
      
      toast.success('Logged in successfully');
      console.log('✅ [LoginWithPassword] Login successful, returning success=true');
      return { success: true };
    } catch (error) {
      console.error('❌ [LoginWithPassword] Password login failed:', error);
      console.error('❌ [LoginWithPassword] Error response:', error.response?.data);
      const message = error.response?.data?.message || error.message || 'Login failed. Please check your credentials.';
      toast.error(message);
      return { success: false, message };
    } finally {
      console.log('🔐 [LoginWithPassword] Setting loading to false');
      setLoading(false);
    }
  }, [clearSession]);

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

  const setPassword = useCallback(
    async (newPassword, confirmPassword) => {
      try {
        await axios.post('/api/auth/set-password', {
          newPassword,
          confirmPassword
        });
        toast.success('Password set successfully');
        
        // Update user state to reflect password is now set
        setUser(prev => prev ? { ...prev, has_password: true } : prev);
        
        return { success: true };
      } catch (error) {
        const message = error.response?.data?.message || 
                       error.response?.data?.errors?.[0]?.msg || 
                       'Failed to set password';
        toast.error(message);
        return { success: false, message };
      }
    },
    []
  );

  const changePassword = useCallback(
    async (currentPassword, newPassword, confirmPassword) => {
      try {
        await axios.post('/api/auth/change-password', {
          currentPassword,
          newPassword,
          confirmPassword
        });
        toast.success('Password changed successfully');
        return { success: true };
      } catch (error) {
        const message = error.response?.data?.message || 
                       error.response?.data?.errors?.[0]?.msg || 
                       'Password change failed';
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
    loginWithPassword,
    logout,
    setPassword,
    changePassword
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};