import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';

type TimestampFormat = 'utc' | 'local';

interface UserPreferencesContextType {
  timestampFormat: TimestampFormat;
  setTimestampFormat: (format: TimestampFormat) => void;
  formatTimestamp: (date: Date | string | null | undefined, options?: { includeSeconds?: boolean }) => string;
  formatRelativeTime: (date: Date | string | null | undefined) => string;
}

const UserPreferencesContext = createContext<UserPreferencesContextType | undefined>(undefined);

export function UserPreferencesProvider({ children }: { children: ReactNode }) {
  const [timestampFormat, setTimestampFormatState] = useState<TimestampFormat>(() => {
    const saved = localStorage.getItem('timestampFormat');
    return (saved === 'utc' || saved === 'local') ? saved : 'local';
  });

  useEffect(() => {
    localStorage.setItem('timestampFormat', timestampFormat);
  }, [timestampFormat]);

  const setTimestampFormat = (format: TimestampFormat) => {
    setTimestampFormatState(format);
  };

  const formatTimestamp = (
    date: Date | string | null | undefined,
    options?: { includeSeconds?: boolean }
  ): string => {
    if (!date) return 'Never';

    const d = typeof date === 'string' ? new Date(date) : date;
    if (isNaN(d.getTime())) return 'Invalid date';

    const includeSeconds = options?.includeSeconds ?? false;

    if (timestampFormat === 'utc') {
      const datePart = d.toISOString().split('T')[0];
      const timePart = includeSeconds
        ? d.toISOString().split('T')[1].slice(0, 8)
        : d.toISOString().split('T')[1].slice(0, 5);
      return `${datePart} ${timePart} UTC`;
    } else {
      const dateOptions: Intl.DateTimeFormatOptions = {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        ...(includeSeconds && { second: '2-digit' }),
        hour12: false,
      };
      return d.toLocaleString(undefined, dateOptions);
    }
  };

  const formatRelativeTime = (date: Date | string | null | undefined): string => {
    if (!date) return 'Never';

    const d = typeof date === 'string' ? new Date(date) : date;
    if (isNaN(d.getTime())) return 'Invalid date';

    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    const diffSecs = Math.floor(diffMs / 1000);
    const diffMins = Math.floor(diffSecs / 60);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffSecs < 60) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;

    // For older dates, use the formatted timestamp
    return formatTimestamp(d);
  };

  return (
    <UserPreferencesContext.Provider value={{
      timestampFormat,
      setTimestampFormat,
      formatTimestamp,
      formatRelativeTime
    }}>
      {children}
    </UserPreferencesContext.Provider>
  );
}

export function useUserPreferences() {
  const context = useContext(UserPreferencesContext);
  if (!context) {
    throw new Error('useUserPreferences must be used within a UserPreferencesProvider');
  }
  return context;
}
