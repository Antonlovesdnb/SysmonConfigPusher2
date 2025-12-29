import { useState, useEffect } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { healthApi } from '../api';
import { useDeploymentQueue } from '../context/DeploymentQueueContext';
import { useTheme } from '../context/ThemeContext';
import { DeploymentQueuePanel } from './DeploymentQueuePanel';

export function Layout() {
  const [health, setHealth] = useState<string | null>(null);
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
        <div className="max-w-7xl mx-auto px-4 py-4">
          <div className="flex justify-between items-center">
            <div className="flex items-center gap-8">
              <h1 className="text-2xl font-bold">SysmonConfigPusher</h1>
              <nav className="flex gap-1">
                <NavLink
                  to="/"
                  end
                  className={({ isActive }) =>
                    `px-4 py-2 rounded-lg transition-colors ${
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
                    `px-4 py-2 rounded-lg transition-colors ${
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
                    `px-4 py-2 rounded-lg transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  Deploy
                </NavLink>
                <NavLink
                  to="/deployments"
                  className={({ isActive }) =>
                    `px-4 py-2 rounded-lg transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  History
                </NavLink>
                <NavLink
                  to="/events"
                  className={({ isActive }) =>
                    `px-4 py-2 rounded-lg transition-colors ${
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
                    `px-4 py-2 rounded-lg transition-colors ${
                      isActive
                        ? 'bg-slate-900 dark:bg-slate-800 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  Noise
                </NavLink>
              </nav>
            </div>
            <div className="flex items-center gap-4">
              {/* Dark mode toggle */}
              <button
                onClick={toggleDarkMode}
                className="p-2 rounded-lg text-slate-300 hover:bg-slate-700 transition-colors"
                title={darkMode ? 'Switch to light mode' : 'Switch to dark mode'}
              >
                {darkMode ? (
                  <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
                  </svg>
                ) : (
                  <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
                  </svg>
                )}
              </button>
              {/* Queue indicator */}
              <button
                onClick={toggleQueuePanel}
                className={`relative px-3 py-2 rounded-lg transition-colors flex items-center gap-2 ${
                  isQueuePanelOpen
                    ? 'bg-slate-900 dark:bg-slate-800 text-white'
                    : 'text-slate-300 hover:bg-slate-700'
                }`}
              >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                </svg>
                Queue
                {queue.length > 0 && (
                  <span className="absolute -top-1 -right-1 w-5 h-5 bg-orange-500 text-white text-xs rounded-full flex items-center justify-center font-medium">
                    {queue.length}
                  </span>
                )}
              </button>
              <span
                className={`px-2 py-1 rounded text-sm ${
                  health === 'Healthy' ? 'bg-green-500' : 'bg-red-500'
                }`}
              >
                {health || 'Checking...'}
              </span>
            </div>
          </div>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-4 py-6">
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
