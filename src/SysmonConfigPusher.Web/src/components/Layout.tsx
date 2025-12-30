import { useState, useEffect } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { healthApi } from '../api';
import { useAuth } from '../context/AuthContext';
import { useDeploymentQueue } from '../context/DeploymentQueueContext';
import { useTheme } from '../context/ThemeContext';
import { DeploymentQueuePanel } from './DeploymentQueuePanel';

export function Layout() {
  const [health, setHealth] = useState<string | null>(null);
  const { user, loading: authLoading, isAdmin } = useAuth();
  const { queue, toggleQueuePanel, isQueuePanelOpen } = useDeploymentQueue();
  const { darkMode, toggleDarkMode } = useTheme();

  useEffect(() => {
    healthApi.check().then(setHealth);
    const interval = setInterval(() => healthApi.check().then(setHealth), 30000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="min-h-screen bg-gray-100 dark:bg-gray-900 transition-colors">
      <header className="bg-slate-800 dark:bg-slate-950 text-white shadow-lg">
        <div className="w-full px-6 py-3">
          <div className="flex justify-between items-center">
            <div className="flex items-center gap-6">
              <div className="flex items-center gap-2">
                <svg className="w-6 h-6" viewBox="0 0 24 24" fill="none">
                  <path d="M12 2L4 6v6c0 5.25 3.4 10.15 8 11.5 4.6-1.35 8-6.25 8-11.5V6l-8-4z" fill="#3b82f6" stroke="#1e40af" strokeWidth="1"/>
                  <ellipse cx="12" cy="11" rx="4" ry="2.5" fill="none" stroke="white" strokeWidth="1.5"/>
                  <circle cx="12" cy="11" r="1.2" fill="white"/>
                </svg>
                <h1 className="text-xl font-bold whitespace-nowrap">SysmonConfigPusher</h1>
              </div>
              <nav className="flex gap-1">
                <NavLink
                  to="/"
                  end
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded-md text-sm transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  Inventory
                </NavLink>
                <NavLink
                  to="/configs"
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded-md text-sm transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  Configs
                </NavLink>
                <NavLink
                  to="/deploy"
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded-md text-sm transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  Deploy
                </NavLink>
                <NavLink
                  to="/events"
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded-md text-sm transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  Events
                </NavLink>
                <NavLink
                  to="/analysis"
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded-md text-sm transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  Noise
                </NavLink>
                <NavLink
                  to="/deployments"
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded-md text-sm transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  History
                </NavLink>
              </nav>
            </div>
            <div className="flex items-center gap-3">
              {/* User info */}
              {!authLoading && user && (
                <div className="flex items-center gap-2 text-sm">
                  <svg className="w-4 h-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                  </svg>
                  <span className="text-slate-300">{user.displayName}</span>
                  <span className={`px-2 py-0.5 rounded text-xs font-medium ${
                    user.highestRole === 'Admin' ? 'bg-purple-500 text-white' :
                    user.highestRole === 'Operator' ? 'bg-blue-500 text-white' :
                    'bg-gray-500 text-white'
                  }`}>
                    {user.highestRole}
                  </span>
                </div>
              )}
              <div className="w-px h-5 bg-slate-600" />
              {/* Dark mode toggle */}
              <button
                onClick={toggleDarkMode}
                className="p-1.5 rounded-md text-slate-300 hover:bg-slate-700 transition-colors"
                title={darkMode ? 'Switch to light mode' : 'Switch to dark mode'}
              >
                {darkMode ? (
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
                  </svg>
                ) : (
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
                  </svg>
                )}
              </button>
              {/* Queue indicator */}
              <button
                onClick={toggleQueuePanel}
                className={`relative px-2 py-1.5 rounded-md text-sm transition-colors flex items-center gap-1.5 ${
                  isQueuePanelOpen
                    ? 'bg-slate-900 dark:bg-slate-800 text-white'
                    : 'text-slate-300 hover:bg-slate-700'
                }`}
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                </svg>
                Queue
                {queue.length > 0 && (
                  <span className="absolute -top-1 -right-1 w-4 h-4 bg-orange-500 text-white text-xs rounded-full flex items-center justify-center font-medium">
                    {queue.length}
                  </span>
                )}
              </button>
              <span
                className={`px-2 py-0.5 rounded text-xs font-medium ${
                  health === 'Healthy' ? 'bg-green-500' : 'bg-red-500'
                }`}
              >
                {health || '...'}
              </span>
              {/* Settings (Admin only) */}
              {isAdmin && (
                <NavLink
                  to="/settings"
                  className={({ isActive }) =>
                    `p-1.5 rounded-md transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                  title="Settings"
                >
                  <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                  </svg>
                </NavLink>
              )}
            </div>
          </div>
        </div>
      </header>

      <main className="w-full px-6 py-6">
        <Outlet />
      </main>

      {/* Queue panel */}
      <DeploymentQueuePanel />

      {/* Backdrop when queue panel is open */}
      {isQueuePanelOpen && (
        <div
          className="fixed inset-0 bg-black/20 dark:bg-black/40 z-40"
          onClick={toggleQueuePanel}
        />
      )}
    </div>
  );
}
