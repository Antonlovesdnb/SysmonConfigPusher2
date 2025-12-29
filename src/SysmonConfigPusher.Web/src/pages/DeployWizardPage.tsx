import { useState, useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { computersApi, configsApi, deploymentsApi } from '../api';
import type { Computer, Config, DeploymentOperation } from '../types';
import { DEPLOYMENT_OPERATIONS } from '../types';
import { useDeploymentQueue } from '../context/DeploymentQueueContext';
import { useAuth } from '../context/AuthContext';

type WizardStep = 'computers' | 'operation' | 'config' | 'confirm';

export function DeployWizardPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const { addToQueue, queue, setQueuePanelOpen } = useDeploymentQueue();
  const { canDeploy } = useAuth();

  // State
  const [step, setStep] = useState<WizardStep>('computers');
  const [computers, setComputers] = useState<Computer[]>([]);
  const [configs, setConfigs] = useState<Config[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(
    new Set(location.state?.computerIds || [])
  );
  const [operation, setOperation] = useState<DeploymentOperation | null>(null);
  const [configId, setConfigId] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [deploying, setDeploying] = useState(false);
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    setLoading(true);
    try {
      const [computersData, configsData] = await Promise.all([
        computersApi.list(),
        configsApi.list(),
      ]);
      setComputers(computersData);
      setConfigs(configsData);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data');
    } finally {
      setLoading(false);
    }
  };

  const toggleSelection = (id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const selectAll = () => {
    const filtered = filteredComputers;
    if (filtered.every((c) => selectedIds.has(c.id))) {
      setSelectedIds((prev) => {
        const next = new Set(prev);
        filtered.forEach((c) => next.delete(c.id));
        return next;
      });
    } else {
      setSelectedIds((prev) => {
        const next = new Set(prev);
        filtered.forEach((c) => next.add(c.id));
        return next;
      });
    }
  };

  const filteredComputers = computers.filter(
    (c) => !search || c.hostname.toLowerCase().includes(search.toLowerCase())
  );

  const selectedComputers = computers.filter((c) => selectedIds.has(c.id));
  const selectedOperation = DEPLOYMENT_OPERATIONS.find((o) => o.value === operation);
  const selectedConfig = configs.find((c) => c.id === configId);

  const canProceed = () => {
    switch (step) {
      case 'computers':
        return selectedIds.size > 0;
      case 'operation':
        return operation !== null;
      case 'config':
        return !selectedOperation?.requiresConfig || configId !== null;
      case 'confirm':
        return true;
      default:
        return false;
    }
  };

  const nextStep = () => {
    if (step === 'computers') setStep('operation');
    else if (step === 'operation') {
      if (selectedOperation?.requiresConfig) {
        setStep('config');
      } else {
        setStep('confirm');
      }
    } else if (step === 'config') setStep('confirm');
  };

  const prevStep = () => {
    if (step === 'operation') setStep('computers');
    else if (step === 'config') setStep('operation');
    else if (step === 'confirm') {
      if (selectedOperation?.requiresConfig) {
        setStep('config');
      } else {
        setStep('operation');
      }
    }
  };

  const startDeployment = async () => {
    if (!operation) return;

    setDeploying(true);
    try {
      const job = await deploymentsApi.start(
        operation,
        Array.from(selectedIds),
        configId ?? undefined
      );
      navigate(`/deployments/${job.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start deployment');
      setDeploying(false);
    }
  };

  const handleAddToQueue = () => {
    if (!operation || !selectedOperation) return;

    addToQueue({
      operation,
      operationLabel: selectedOperation.label,
      config: selectedConfig
        ? { id: selectedConfig.id, filename: selectedConfig.filename, tag: selectedConfig.tag }
        : null,
      computers: selectedComputers.map((c) => ({ id: c.id, hostname: c.hostname })),
    });

    // Reset wizard for next deployment
    setStep('computers');
    setSelectedIds(new Set());
    setOperation(null);
    setConfigId(null);
    setQueuePanelOpen(true);
  };

  if (loading) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="text-center py-8 text-gray-500 dark:text-gray-400">Loading...</div>
      </div>
    );
  }

  if (!canDeploy) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="text-center py-8">
          <svg className="w-16 h-16 mx-auto text-gray-400 dark:text-gray-500 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
          </svg>
          <h2 className="text-xl font-semibold text-gray-700 dark:text-gray-300 mb-2">Access Denied</h2>
          <p className="text-gray-500 dark:text-gray-400 mb-4">
            You don't have permission to deploy Sysmon configurations.
          </p>
          <p className="text-sm text-gray-400 dark:text-gray-500">
            Contact your administrator to request Operator or Admin access.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Progress indicator */}
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-4">
        <div className="flex items-center justify-between">
          {['computers', 'operation', 'config', 'confirm'].map((s, i) => {
            const stepLabels: Record<string, string> = {
              computers: 'Select Computers',
              operation: 'Choose Operation',
              config: 'Select Config',
              confirm: 'Confirm & Deploy',
            };
            const isActive = step === s;
            const isPast =
              ['computers', 'operation', 'config', 'confirm'].indexOf(step) > i;
            const isSkipped = s === 'config' && !selectedOperation?.requiresConfig;

            if (isSkipped) return null;

            return (
              <div key={s} className="flex items-center">
                <div
                  className={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-medium ${
                    isActive
                      ? 'bg-slate-700 text-white'
                      : isPast
                      ? 'bg-green-500 text-white'
                      : 'bg-gray-200 dark:bg-gray-600 text-gray-600 dark:text-gray-300'
                  }`}
                >
                  {isPast ? '✓' : i + 1}
                </div>
                <span
                  className={`ml-2 text-sm ${
                    isActive ? 'text-slate-700 dark:text-slate-300 font-medium' : 'text-gray-500 dark:text-gray-400'
                  }`}
                >
                  {stepLabels[s]}
                </span>
                {i < 3 && !isSkipped && (
                  <div className="w-12 h-0.5 bg-gray-200 dark:bg-gray-600 mx-4" />
                )}
              </div>
            );
          })}
        </div>
      </div>

      {error && (
        <div className="p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">{error}</div>
      )}

      {/* Step content */}
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        {step === 'computers' && (
          <div>
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Select Target Computers</h2>
              <input
                type="text"
                placeholder="Search hostname..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
              />
            </div>

            <div className="max-h-96 overflow-y-auto border dark:border-gray-700 rounded-lg">
              <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
                <thead className="bg-gray-50 dark:bg-gray-700 sticky top-0">
                  <tr>
                    <th className="px-4 py-3 text-left">
                      <input
                        type="checkbox"
                        checked={
                          filteredComputers.length > 0 &&
                          filteredComputers.every((c) => selectedIds.has(c.id))
                        }
                        onChange={selectAll}
                        className="w-4 h-4 rounded border-gray-300 dark:border-gray-600"
                      />
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">
                      Hostname
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">
                      OS
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">
                      Sysmon
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
                  {filteredComputers.map((computer) => (
                    <tr
                      key={computer.id}
                      className={`hover:bg-gray-50 dark:hover:bg-gray-700 cursor-pointer ${
                        selectedIds.has(computer.id) ? 'bg-slate-50 dark:bg-slate-800' : ''
                      }`}
                      onClick={() => toggleSelection(computer.id)}
                    >
                      <td className="px-4 py-3">
                        <input
                          type="checkbox"
                          checked={selectedIds.has(computer.id)}
                          onChange={() => toggleSelection(computer.id)}
                          onClick={(e) => e.stopPropagation()}
                          className="w-4 h-4 rounded border-gray-300 dark:border-gray-600"
                        />
                      </td>
                      <td className="px-4 py-3 text-sm font-medium text-gray-900 dark:text-gray-100">
                        {computer.hostname}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500 dark:text-gray-400">
                        {computer.operatingSystem || '-'}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500 dark:text-gray-400">
                        {computer.sysmonVersion || '-'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="mt-4 text-sm text-gray-500 dark:text-gray-400">
              {selectedIds.size} of {computers.length} computers selected
            </div>
          </div>
        )}

        {step === 'operation' && (
          <div>
            <h2 className="text-lg font-semibold mb-4 text-gray-900 dark:text-gray-100">Choose Deployment Operation</h2>
            <div className="grid gap-4">
              {DEPLOYMENT_OPERATIONS.map((op) => (
                <label
                  key={op.value}
                  className={`flex items-start p-4 border rounded-lg cursor-pointer transition-colors ${
                    operation === op.value
                      ? 'border-slate-500 bg-slate-50 dark:bg-slate-800 dark:border-slate-600'
                      : 'border-gray-200 dark:border-gray-700 hover:border-gray-300 dark:hover:border-gray-600'
                  }`}
                >
                  <input
                    type="radio"
                    name="operation"
                    value={op.value}
                    checked={operation === op.value}
                    onChange={() => setOperation(op.value)}
                    className="mt-1 w-4 h-4 accent-slate-600"
                  />
                  <div className="ml-3">
                    <div className="font-medium text-gray-900 dark:text-gray-100">{op.label}</div>
                    <div className="text-sm text-gray-500 dark:text-gray-400">{op.description}</div>
                    {op.requiresConfig && (
                      <div className="text-xs text-slate-600 dark:text-slate-400 mt-1">Requires config file</div>
                    )}
                  </div>
                </label>
              ))}
            </div>
          </div>
        )}

        {step === 'config' && (
          <div>
            <h2 className="text-lg font-semibold mb-4 text-gray-900 dark:text-gray-100">Select Configuration</h2>
            {configs.length === 0 ? (
              <div className="text-center py-8 text-gray-500 dark:text-gray-400">
                No configs available. Please upload a config first.
              </div>
            ) : (
              <div className="grid gap-4">
                {configs.map((config) => (
                  <label
                    key={config.id}
                    className={`flex items-start p-4 border rounded-lg cursor-pointer transition-colors ${
                      configId === config.id
                        ? 'border-slate-500 bg-slate-50 dark:bg-slate-800 dark:border-slate-600'
                        : 'border-gray-200 dark:border-gray-700 hover:border-gray-300 dark:hover:border-gray-600'
                    }`}
                  >
                    <input
                      type="radio"
                      name="config"
                      value={config.id}
                      checked={configId === config.id}
                      onChange={() => setConfigId(config.id)}
                      className="mt-1 w-4 h-4 accent-slate-600"
                    />
                    <div className="ml-3">
                      <div className="font-medium text-gray-900 dark:text-gray-100">{config.filename}</div>
                      {config.tag && (
                        <span className="inline-block px-2 py-0.5 bg-slate-100 dark:bg-slate-700 text-slate-700 dark:text-slate-300 rounded text-xs mt-1">
                          {config.tag}
                        </span>
                      )}
                      <div className="text-xs text-gray-400 dark:text-gray-500 font-mono mt-1">
                        {config.hash.substring(0, 16)}...
                      </div>
                    </div>
                  </label>
                ))}
              </div>
            )}
          </div>
        )}

        {step === 'confirm' && (
          <div>
            <h2 className="text-lg font-semibold mb-4 text-gray-900 dark:text-gray-100">Confirm Deployment</h2>
            <div className="space-y-4">
              <div className="p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
                <h3 className="font-medium text-gray-700 dark:text-gray-300 mb-2">Operation</h3>
                <div className="text-lg text-gray-900 dark:text-gray-100">{selectedOperation?.label}</div>
                <div className="text-sm text-gray-500 dark:text-gray-400">{selectedOperation?.description}</div>
              </div>

              {selectedConfig && (
                <div className="p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
                  <h3 className="font-medium text-gray-700 dark:text-gray-300 mb-2">Configuration</h3>
                  <div className="text-lg text-gray-900 dark:text-gray-100">{selectedConfig.filename}</div>
                  {selectedConfig.tag && (
                    <span className="inline-block px-2 py-0.5 bg-slate-100 dark:bg-slate-600 text-slate-700 dark:text-slate-200 rounded text-xs mt-1">
                      {selectedConfig.tag}
                    </span>
                  )}
                </div>
              )}

              <div className="p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
                <h3 className="font-medium text-gray-700 dark:text-gray-300 mb-2">
                  Target Computers ({selectedComputers.length})
                </h3>
                <div className="max-h-40 overflow-y-auto">
                  <div className="flex flex-wrap gap-2">
                    {selectedComputers.map((c) => (
                      <span
                        key={c.id}
                        className="px-2 py-1 bg-white dark:bg-gray-600 border dark:border-gray-500 rounded text-sm text-gray-900 dark:text-gray-100"
                      >
                        {c.hostname}
                      </span>
                    ))}
                  </div>
                </div>
              </div>

              <div className="p-4 bg-yellow-50 dark:bg-yellow-900/30 border border-yellow-200 dark:border-yellow-800 rounded-lg">
                <div className="text-yellow-800 dark:text-yellow-400">
                  This will execute the <strong>{selectedOperation?.label}</strong> operation
                  on <strong>{selectedComputers.length}</strong> computer
                  {selectedComputers.length !== 1 ? 's' : ''}.
                </div>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Navigation */}
      <div className="flex justify-between">
        <button
          onClick={step === 'computers' ? () => navigate('/') : prevStep}
          className="px-6 py-2 border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-300 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700"
        >
          {step === 'computers' ? 'Cancel' : 'Back'}
        </button>

        {step === 'confirm' ? (
          <div className="flex gap-3">
            <button
              onClick={handleAddToQueue}
              disabled={deploying}
              className="px-6 py-2 bg-slate-600 text-white rounded-lg hover:bg-slate-700 disabled:opacity-50 flex items-center gap-2"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
              </svg>
              Add to Queue {queue.length > 0 && `(${queue.length})`}
            </button>
            <button
              onClick={startDeployment}
              disabled={deploying}
              className="px-6 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50"
            >
              {deploying ? 'Starting...' : 'Start Deployment'}
            </button>
          </div>
        ) : (
          <button
            onClick={nextStep}
            disabled={!canProceed()}
            className="px-6 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 disabled:opacity-50"
          >
            Next
          </button>
        )}
      </div>
    </div>
  );
}
