import { useState, useEffect } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { healthApi } from '../api';

export function Layout() {
  const [health, setHealth] = useState<string | null>(null);

  useEffect(() => {
    healthApi.check().then(setHealth);
    const interval = setInterval(() => healthApi.check().then(setHealth), 30000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="min-h-screen bg-gray-100">
      <header className="bg-slate-800 text-white shadow-lg">
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
                        ? 'bg-slate-900 text-white'
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
                        ? 'bg-slate-900 text-white'
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
                        ? 'bg-slate-900 text-white'
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
                        ? 'bg-slate-900 text-white'
                        : 'text-slate-300 hover:bg-slate-700'
                    }`
                  }
                >
                  History
                </NavLink>
              </nav>
            </div>
            <div className="flex items-center gap-4">
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
    </div>
  );
}
