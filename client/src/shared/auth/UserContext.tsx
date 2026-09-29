import { useMeQuery, User } from '@/queries/user.ts';
import { HttpError } from '@/lib/api.ts';
import { createContext, useContext, useEffect } from 'react';

/**
 * React context for managing authenticated user state throughout the application.
 * We'll use it in (authenticated) routes where components can call useUser() to get the current user.
 */
const UserContext = createContext<User | undefined>(undefined);

/**
 * Provider component that wraps the application and provides user context to all child components.
 * Shows loading and error states while fetching user data, but we should never see these states since our auth route
 * ensures user data is loaded before rendering any authenticated routes.
 */
export const UserProvider = ({ children }: { children: React.ReactNode }) => {
  const { data, error, isLoading } = useMeQuery();

  useEffect(() => {
    if (error instanceof HttpError && error.status === 401) {
      const returnUrl = window.location.pathname + window.location.search;
      window.location.href = `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
    }
  }, [error]);

  if (error instanceof HttpError && error.status === 401) {
    return <p className="content-container py-12" role="status">Redirecting to sign in…</p>;
  }

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="loading loading-spinner loading-lg"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="alert alert-error max-w-md">
          <svg
            className="h-6 w-6 shrink-0 stroke-current"
            fill="none"
            viewBox="0 0 24 24"
            xmlns="http://www.w3.org/2000/svg"
          >
            <path
              d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z"
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth="2"
            />
          </svg>
          <span>Failed to load user data. Please try again later.</span>
        </div>
      </div>
    );
  }

  return <UserContext.Provider value={data}>{children}</UserContext.Provider>;
};

/**
 * Custom hook to access the current authenticated user's information.
 */
export const useUser = () => {
  const context = useContext(UserContext);
  if (!context) {
    throw new Error('useUser must be used within a UserProvider');
  }
  return context;
};
