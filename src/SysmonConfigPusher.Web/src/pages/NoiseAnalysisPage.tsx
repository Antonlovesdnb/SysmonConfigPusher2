import { useState, useEffect, useMemo, useCallback, memo } from 'react';
import { computersApi, analysisApi, configsApi, deploymentsApi } from '../api';
import type { Computer, NoiseAnalysisRun, NoiseResult, NoiseLevel, EventTypeSummary, Config } from '../types';

// Memoized helper functions outside component
const getNoiseLevelBadge = (level: NoiseLevel) => {
  switch (level) {
    case 'VeryNoisy':
      return 'bg-red-100 text-red-800';
    case 'Noisy':
      return 'bg-yellow-100 text-yellow-800';
    default:
      return 'bg-green-100 text-green-800';
  }
};

const formatTimeRange = (hours: number): string => {
  if (hours < 1) {
    const minutes = Math.round(hours * 60);
    return `${minutes}m`;
  }
  if (hours < 24) {
    return `${hours}h`;
  }
  const days = Math.round(hours / 24);
  return `${days}d`;
};

// Memoized noise score bar component
const NoiseScoreBar = memo(({ score }: { score: number }) => {
  const width = Math.min(score * 100, 100);
  let color = 'bg-green-500';
  if (score >= 0.7) color = 'bg-red-500';
  else if (score >= 0.5) color = 'bg-yellow-500';

  return (
    <div className="w-24 bg-gray-200 rounded-full h-2">
      <div className={`${color} h-2 rounded-full`} style={{ width: `${width}%` }} />
    </div>
  );
});

// Memoized pattern row component
const PatternRow = memo(({
  pattern,
  onPatternClick
}: {
  pattern: NoiseResult;
  onPatternClick: (pattern: NoiseResult, e: React.MouseEvent) => void;
}) => (
  <tr
    className="hover:bg-gray-100 dark:hover:bg-gray-600 cursor-pointer"
    onClick={(e) => onPatternClick(pattern, e)}
  >
    <td className="px-6 py-2 text-sm text-gray-900 dark:text-gray-100 font-mono max-w-md">
      <div className="truncate flex items-center gap-2" title={pattern.groupingKey}>
        <span className="text-slate-400 dark:text-slate-500 hover:text-slate-600 dark:hover:text-slate-400">
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
          </svg>
        </span>
        {pattern.groupingKey}
      </div>
    </td>
    <td className="px-4 py-2 text-sm text-gray-900 dark:text-gray-100">
      {pattern.eventCount.toLocaleString()}
    </td>
    <td className="px-4 py-2 text-sm text-gray-900 dark:text-gray-100">
      {pattern.eventsPerHour.toFixed(1)}
    </td>
    <td className="px-4 py-2">
      <div className="flex items-center gap-2">
        <NoiseScoreBar score={pattern.noiseScore} />
        <span className="text-xs text-gray-500 dark:text-gray-400">
          {(pattern.noiseScore * 100).toFixed(0)}%
        </span>
      </div>
    </td>
    <td className="px-4 py-2">
      <span className={`px-2 py-0.5 rounded text-xs font-medium ${getNoiseLevelBadge(pattern.noiseLevel)}`}>
        {pattern.noiseLevel}
      </span>
    </td>
  </tr>
));

// Memoized event type section component
const EventTypeSection = memo(({
  summary,
  isExpanded,
  onToggle,
  onPatternClick
}: {
  summary: EventTypeSummary;
  isExpanded: boolean;
  onToggle: () => void;
  onPatternClick: (pattern: NoiseResult, e: React.MouseEvent) => void;
}) => (
  <div className="bg-white dark:bg-gray-800">
    {/* Event Type Header - Clickable */}
    <div
      className="p-4 cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-700 flex items-center justify-between"
      onClick={onToggle}
    >
      <div className="flex items-center gap-4">
        <svg
          className={`w-5 h-5 text-gray-400 transition-transform ${isExpanded ? 'rotate-90' : ''}`}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
        </svg>
        <div>
          <div className="font-medium text-gray-900 dark:text-gray-100">
            {summary.eventType}
            <span className="ml-2 text-sm text-gray-500 dark:text-gray-400">
              (Event ID {summary.eventId})
            </span>
          </div>
          <div className="text-sm text-gray-500 dark:text-gray-400">
            {summary.patternCount} unique pattern{summary.patternCount !== 1 ? 's' : ''}
          </div>
        </div>
      </div>
      <div className="flex items-center gap-6">
        <div className="text-right">
          <div className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            {summary.totalCount.toLocaleString()}
          </div>
          <div className="text-xs text-gray-500 dark:text-gray-400">
            {summary.eventsPerHour.toFixed(1)}/hour
          </div>
        </div>
        {summary.topPatterns.some(p => p.noiseLevel !== 'Normal') && (
          <span className="px-2 py-1 bg-yellow-100 text-yellow-800 rounded text-xs font-medium">
            Contains Noise
          </span>
        )}
      </div>
    </div>

    {/* Expanded Pattern Details */}
    {isExpanded && summary.topPatterns.length > 0 && (
      <div className="bg-gray-50 dark:bg-gray-700 border-t dark:border-gray-600">
        <table className="min-w-full">
          <thead>
            <tr className="border-b border-gray-200 dark:border-gray-600">
              <th className="px-6 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Pattern</th>
              <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Count</th>
              <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Per Hour</th>
              <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Noise</th>
              <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Level</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 dark:divide-gray-600">
            {summary.topPatterns.map((pattern, idx) => (
              <PatternRow
                key={idx}
                pattern={pattern}
                onPatternClick={onPatternClick}
              />
            ))}
          </tbody>
        </table>
      </div>
    )}
  </div>
));

interface ParsedField {
  name: string;
  value: string;
}

export function NoiseAnalysisPage() {
  const [computers, setComputers] = useState<Computer[]>([]);
  const [selectedComputer, setSelectedComputer] = useState<number | null>(null);
  const [computerSearch, setComputerSearch] = useState('');
  const [timeRange, setTimeRange] = useState<number>(24);
  const [loading, setLoading] = useState(false);
  const [analyzing, setAnalyzing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Filtered computers based on search
  const filteredComputers = useMemo(() => {
    if (!computerSearch.trim()) return computers;
    const search = computerSearch.toLowerCase();
    return computers.filter((c) =>
      c.hostname.toLowerCase().includes(search) ||
      c.operatingSystem?.toLowerCase().includes(search) ||
      c.sysmonVersion?.toLowerCase().includes(search)
    );
  }, [computers, computerSearch]);

  // Results
  const [currentRun, setCurrentRun] = useState<NoiseAnalysisRun | null>(null);
  const [results, setResults] = useState<NoiseResult[]>([]);
  const [eventSummaries, setEventSummaries] = useState<EventTypeSummary[]>([]);
  const [history, setHistory] = useState<NoiseAnalysisRun[]>([]);
  const [expandedEvents, setExpandedEvents] = useState<Set<number>>(new Set());

  // Exclusion XML
  const [exclusionXml, setExclusionXml] = useState<string | null>(null);
  const [showExclusionModal, setShowExclusionModal] = useState(false);
  const [copied, setCopied] = useState(false);

  // Pattern exclusion modal
  const [configs, setConfigs] = useState<Config[]>([]);
  const [showPatternModal, setShowPatternModal] = useState(false);
  const [selectedPattern, setSelectedPattern] = useState<NoiseResult | null>(null);
  const [parsedFields, setParsedFields] = useState<ParsedField[]>([]);
  const [selectedFieldIndex, setSelectedFieldIndex] = useState<number | null>(null);
  const [selectedConfigId, setSelectedConfigId] = useState<number | null>(null);
  const [addingExclusion, setAddingExclusion] = useState(false);
  const [exclusionSuccess, setExclusionSuccess] = useState<string | null>(null);
  const [startingDeploy, setStartingDeploy] = useState(false);
  const [deployStarted, setDeployStarted] = useState(false);

  // Purge state
  const [showPurgeConfirm, setShowPurgeConfirm] = useState(false);
  const [purging, setPurging] = useState(false);
  const [purgeDays, setPurgeDays] = useState(30);

  useEffect(() => {
    loadComputers();
    loadHistory();
    loadConfigs();
  }, []);

  const loadComputers = async () => {
    setLoading(true);
    try {
      const data = await computersApi.list();
      // Only show computers with Sysmon installed
      setComputers(data.filter((c) => c.sysmonVersion));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load computers');
    } finally {
      setLoading(false);
    }
  };

  const loadHistory = async () => {
    try {
      const data = await analysisApi.getNoiseHistory(undefined, 10);
      setHistory(data);
    } catch (err) {
      console.error('Failed to load history:', err);
    }
  };

  const loadConfigs = async () => {
    try {
      const data = await configsApi.list();
      setConfigs(data);
      if (data.length > 0) {
        setSelectedConfigId(data[0].id);
      }
    } catch (err) {
      console.error('Failed to load configs:', err);
    }
  };

  const startAnalysis = async () => {
    if (!selectedComputer) {
      setError('Please select a computer');
      return;
    }

    setAnalyzing(true);
    setError(null);
    setResults([]);
    setEventSummaries([]);
    setCurrentRun(null);
    setExpandedEvents(new Set());

    try {
      const response = await analysisApi.startNoiseAnalysis(selectedComputer, timeRange);

      if (response.success && response.run) {
        setCurrentRun(response.run);
        setResults(response.results);
        setEventSummaries(response.eventSummaries || []);
        // Expand all event types by default
        setExpandedEvents(new Set((response.eventSummaries || []).map(s => s.eventId)));
        loadHistory();
      } else {
        setError(response.errorMessage || 'Analysis failed');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start analysis');
    } finally {
      setAnalyzing(false);
    }
  };

  const loadPreviousRun = async (runId: number) => {
    setAnalyzing(true);
    setError(null);

    try {
      const response = await analysisApi.getNoiseAnalysis(runId);

      if (response.success && response.run) {
        setCurrentRun(response.run);
        setResults(response.results);
        setEventSummaries(response.eventSummaries || []);
        setSelectedComputer(response.run.computerId);
        // Expand all event types by default
        setExpandedEvents(new Set((response.eventSummaries || []).map(s => s.eventId)));
      } else {
        setError(response.errorMessage || 'Failed to load analysis');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load analysis');
    } finally {
      setAnalyzing(false);
    }
  };

  const generateExclusionXml = async () => {
    if (!currentRun) return;

    try {
      const response = await analysisApi.getExclusionXml(currentRun.id, 0.5);
      if (response.success && response.xml) {
        setExclusionXml(response.xml);
        setShowExclusionModal(true);
      } else {
        setError(response.errorMessage || 'Failed to generate exclusion XML');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to generate exclusion XML');
    }
  };

  const copyToClipboard = async () => {
    if (exclusionXml) {
      await navigator.clipboard.writeText(exclusionXml);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const handlePurge = async () => {
    setPurging(true);
    try {
      const result = await analysisApi.purgeNoiseAnalysis(purgeDays);
      if (result.success) {
        // Reload history to reflect remaining records
        await loadHistory();
        // Clear current view if it was purged
        if (currentRun) {
          const stillExists = history.some(h => h.id === currentRun.id);
          if (!stillExists) {
            setCurrentRun(null);
            setResults([]);
            setEventSummaries([]);
          }
        }
        setShowPurgeConfirm(false);
      } else {
        setError('Failed to purge analysis history');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to purge analysis history');
    } finally {
      setPurging(false);
    }
  };

  // Convert availableFields object to ParsedField array
  const getFieldsFromPattern = (pattern: NoiseResult): ParsedField[] => {
    const fields: ParsedField[] = [];
    if (pattern.availableFields) {
      for (const [name, value] of Object.entries(pattern.availableFields)) {
        if (value && value !== 'Unknown') {
          fields.push({ name, value });
        }
      }
    }
    // Fallback: parse grouping key if availableFields is empty (for historical data)
    if (fields.length === 0) {
      const parts = pattern.groupingKey.split(' | ');
      for (const part of parts) {
        const colonIndex = part.indexOf(': ');
        if (colonIndex > 0) {
          const name = part.substring(0, colonIndex).trim();
          const value = part.substring(colonIndex + 2).trim();
          if (value && value !== 'Unknown') {
            fields.push({ name, value });
          }
        }
      }
    }
    return fields;
  };

  const handlePatternClick = useCallback((pattern: NoiseResult, e: React.MouseEvent) => {
    e.stopPropagation();
    const fields = getFieldsFromPattern(pattern);
    setSelectedPattern(pattern);
    setParsedFields(fields);
    setSelectedFieldIndex(null);
    setExclusionSuccess(null);
    setDeployStarted(false);
    setShowPatternModal(true);
  }, []);

  const addExclusionToConfig = async () => {
    if (!selectedPattern || selectedFieldIndex === null || !selectedConfigId) return;

    const field = parsedFields[selectedFieldIndex];
    if (!field) return;

    setAddingExclusion(true);
    setExclusionSuccess(null);

    try {
      const result = await configsApi.addExclusion(
        selectedConfigId,
        selectedPattern.eventId,
        field.name,
        field.value,
        'is'
      );

      if (result.success) {
        setExclusionSuccess(result.message || `Added exclusion for ${field.name}: "${field.value}"`);
      } else {
        setError(result.message || 'Failed to add exclusion');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add exclusion');
    } finally {
      setAddingExclusion(false);
    }
  };

  const startDeployment = async () => {
    if (!selectedConfigId || !currentRun) return;

    setStartingDeploy(true);
    try {
      await deploymentsApi.start('update', [currentRun.computerId], selectedConfigId);
      setDeployStarted(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start deployment');
    } finally {
      setStartingDeploy(false);
    }
  };

  const toggleEventExpansion = useCallback((eventId: number) => {
    setExpandedEvents(prev => {
      const next = new Set(prev);
      if (next.has(eventId)) {
        next.delete(eventId);
      } else {
        next.add(eventId);
      }
      return next;
    });
  }, []);

  const hasNoisyPatterns = useMemo(() => results.some((r) => r.noiseLevel !== 'Normal'), [results]);

  return (
    <div className="space-y-4">
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200 mb-4">Noise Analysis</h2>
        <p className="text-gray-600 dark:text-gray-400 mb-6">
          Analyze Sysmon event patterns to identify high-volume events and configuration tuning opportunities.
        </p>

        {/* Target Computer Selection */}
        <div className="mb-6">
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Target Computer {selectedComputer && (
              <span className="text-green-600 dark:text-green-400 font-normal">
                - {computers.find(c => c.id === selectedComputer)?.hostname} selected
              </span>
            )}
          </label>

          {/* Search box */}
          <div className="mb-2">
            <input
              type="text"
              value={computerSearch}
              onChange={(e) => setComputerSearch(e.target.value)}
              placeholder="Search computers by hostname, OS, or Sysmon version..."
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            />
          </div>

          {loading ? (
            <div className="text-gray-500 dark:text-gray-400 p-4">Loading computers...</div>
          ) : computers.length === 0 ? (
            <div className="text-gray-500 dark:text-gray-400 p-4">No computers with Sysmon found</div>
          ) : (
            <div className="border dark:border-gray-600 rounded-lg overflow-hidden">
              <div className="max-h-48 overflow-y-auto">
                <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
                  <thead className="bg-gray-50 dark:bg-gray-700 sticky top-0">
                    <tr>
                      <th className="px-4 py-2 text-left w-10"></th>
                      <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Hostname</th>
                      <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">OS</th>
                      <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Sysmon</th>
                    </tr>
                  </thead>
                  <tbody className="bg-white dark:bg-gray-800 divide-y divide-gray-200 dark:divide-gray-700">
                    {filteredComputers.length === 0 ? (
                      <tr>
                        <td colSpan={4} className="px-4 py-4 text-center text-gray-500 dark:text-gray-400">
                          No computers match your search
                        </td>
                      </tr>
                    ) : (
                      filteredComputers.map((computer) => (
                        <tr
                          key={computer.id}
                          className={`cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-700 ${
                            selectedComputer === computer.id ? 'bg-slate-50 dark:bg-slate-900/30' : ''
                          }`}
                          onClick={() => setSelectedComputer(computer.id)}
                        >
                          <td className="px-4 py-2">
                            <input
                              type="radio"
                              name="targetComputer"
                              checked={selectedComputer === computer.id}
                              onChange={() => setSelectedComputer(computer.id)}
                              onClick={(e) => e.stopPropagation()}
                              className="w-4 h-4"
                            />
                          </td>
                          <td className="px-4 py-2 text-sm font-medium text-gray-900 dark:text-gray-100">
                            {computer.hostname}
                          </td>
                          <td className="px-4 py-2 text-sm text-gray-500 dark:text-gray-400">
                            {computer.operatingSystem || '-'}
                          </td>
                          <td className="px-4 py-2 text-sm text-gray-500 dark:text-gray-400">
                            {computer.sysmonVersion || '-'}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
              <div className="px-4 py-2 bg-gray-50 dark:bg-gray-700 border-t dark:border-gray-600 text-sm text-gray-500 dark:text-gray-400">
                Showing {filteredComputers.length} of {computers.length} computers
              </div>
            </div>
          )}
        </div>

        {/* Time Range and Analyze Button */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Time Range</label>
            <select
              value={timeRange}
              onChange={(e) => setTimeRange(parseFloat(e.target.value))}
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            >
              <option value={0.0833}>Last 5 minutes</option>
              <option value={0.25}>Last 15 minutes</option>
              <option value={1}>Last 1 hour</option>
              <option value={6}>Last 6 hours</option>
              <option value={24}>Last 24 hours</option>
              <option value={168}>Last 7 days</option>
            </select>
          </div>

          <div className="flex items-end">
            <button
              onClick={startAnalysis}
              disabled={analyzing || !selectedComputer}
              className="w-full px-6 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 disabled:opacity-50"
            >
              {analyzing ? 'Analyzing...' : 'Start Analysis'}
            </button>
          </div>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">{error}</div>
      )}

      {/* Results */}
      {currentRun && (
        <div className="bg-white dark:bg-gray-800 rounded-lg shadow">
          <div className="p-4 border-b dark:border-gray-700 flex justify-between items-center">
            <div>
              <h3 className="font-semibold text-lg text-gray-900 dark:text-gray-100">Event Analysis Results</h3>
              <p className="text-sm text-gray-500 dark:text-gray-400">
                {currentRun.hostname} - {currentRun.totalEvents.toLocaleString()} events over {formatTimeRange(currentRun.timeRangeHours)}
              </p>
            </div>
            {hasNoisyPatterns && (
              <button
                onClick={generateExclusionXml}
                className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 flex items-center gap-2"
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                </svg>
                Generate Exclusion XML
              </button>
            )}
          </div>

          {eventSummaries.length === 0 ? (
            <div className="p-8 text-center text-gray-500 dark:text-gray-400">
              No events found in the specified time range
            </div>
          ) : (
            <div className="divide-y divide-gray-200 dark:divide-gray-700">
              {eventSummaries.map((summary) => (
                <EventTypeSection
                  key={summary.eventId}
                  summary={summary}
                  isExpanded={expandedEvents.has(summary.eventId)}
                  onToggle={() => toggleEventExpansion(summary.eventId)}
                  onPatternClick={handlePatternClick}
                />
              ))}
            </div>
          )}
        </div>
      )}

      {/* History */}
      {history.length > 0 && (
        <div className="bg-white dark:bg-gray-800 rounded-lg shadow">
          <div className="p-4 border-b dark:border-gray-700 flex justify-between items-center">
            <h3 className="font-semibold text-gray-900 dark:text-gray-100">Previous Analyses</h3>
            <button
              onClick={() => setShowPurgeConfirm(true)}
              className="px-3 py-1 text-sm bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400 rounded hover:bg-red-200 dark:hover:bg-red-900/50 flex items-center gap-1"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
              </svg>
              Purge All
            </button>
          </div>
          <div className="divide-y divide-gray-200 dark:divide-gray-700">
            {history.map((run) => (
              <div
                key={run.id}
                className="p-4 hover:bg-gray-50 dark:hover:bg-gray-700 cursor-pointer flex justify-between items-center"
                onClick={() => loadPreviousRun(run.id)}
              >
                <div>
                  <div className="font-medium text-gray-900 dark:text-gray-100">{run.hostname}</div>
                  <div className="text-sm text-gray-500 dark:text-gray-400">
                    {run.totalEvents.toLocaleString()} events over {formatTimeRange(run.timeRangeHours)}
                  </div>
                </div>
                <div className="text-sm text-gray-500 dark:text-gray-400">
                  {new Date(run.analyzedAt).toLocaleString()}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Exclusion XML Modal */}
      {showExclusionModal && exclusionXml && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-4xl w-full max-h-[90vh] flex flex-col m-4">
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Generated Exclusion Rules</h3>
              <button
                onClick={() => setShowExclusionModal(false)}
                className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
              >
                &times;
              </button>
            </div>

            <div className="flex-1 overflow-auto p-4">
              <pre className="text-sm font-mono bg-gray-50 dark:bg-gray-700 p-4 rounded overflow-x-auto whitespace-pre text-gray-900 dark:text-gray-100">
                {exclusionXml}
              </pre>
            </div>

            <div className="p-4 border-t dark:border-gray-700 flex justify-between items-center bg-gray-50 dark:bg-gray-700">
              <p className="text-sm text-gray-500 dark:text-gray-400">
                Copy these rules to your Sysmon configuration file.
              </p>
              <div className="flex gap-2">
                <button
                  onClick={copyToClipboard}
                  className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 flex items-center gap-2"
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                  </svg>
                  {copied ? 'Copied!' : 'Copy to Clipboard'}
                </button>
                <button
                  onClick={() => setShowExclusionModal(false)}
                  className="px-4 py-2 bg-gray-200 dark:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-500"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Pattern Exclusion Modal */}
      {showPatternModal && selectedPattern && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] flex flex-col m-4">
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <div>
                <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Add Exclusion Rule</h3>
                <p className="text-sm text-gray-500 dark:text-gray-400">{selectedPattern.eventType} (Event ID {selectedPattern.eventId})</p>
              </div>
              <button
                onClick={() => setShowPatternModal(false)}
                className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
              >
                &times;
              </button>
            </div>

            <div className="flex-1 overflow-auto p-4 space-y-4">
              {/* Field Selection */}
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  Select a field value to exclude:
                </label>
                <div className="space-y-2">
                  {parsedFields.map((field, idx) => (
                    <div
                      key={idx}
                      className={`p-3 border rounded-lg cursor-pointer transition-colors ${
                        selectedFieldIndex === idx
                          ? 'border-slate-500 bg-slate-50 dark:bg-slate-800 ring-2 ring-slate-500 dark:ring-slate-600'
                          : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500 hover:bg-gray-50 dark:hover:bg-gray-700'
                      }`}
                      onClick={() => setSelectedFieldIndex(idx)}
                    >
                      <div className="flex items-center gap-2">
                        <input
                          type="radio"
                          checked={selectedFieldIndex === idx}
                          onChange={() => setSelectedFieldIndex(idx)}
                          className="text-slate-600"
                        />
                        <div>
                          <span className="font-medium text-gray-700 dark:text-gray-300">{field.name}:</span>
                          <span className="ml-2 font-mono text-sm text-gray-900 dark:text-gray-100 break-all">{field.value}</span>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* Config Selection */}
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  Add exclusion to config:
                </label>
                {configs.length === 0 ? (
                  <p className="text-sm text-gray-500 dark:text-gray-400 italic">No configs available. Upload a config first.</p>
                ) : (
                  <select
                    value={selectedConfigId || ''}
                    onChange={(e) => setSelectedConfigId(e.target.value ? parseInt(e.target.value) : null)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                  >
                    {configs.map((config) => (
                      <option key={config.id} value={config.id}>
                        {config.filename} {config.tag && `(${config.tag})`}
                      </option>
                    ))}
                  </select>
                )}
              </div>

              {/* Success Message */}
              {exclusionSuccess && (
                <div className="p-3 bg-green-100 text-green-800 rounded-lg">
                  <div className="flex items-center gap-2">
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                    </svg>
                    {exclusionSuccess}
                  </div>

                  {/* Deploy Button */}
                  {currentRun && !deployStarted && (
                    <div className="mt-3 pt-3 border-t border-green-200">
                      <button
                        onClick={startDeployment}
                        disabled={startingDeploy}
                        className="w-full px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 flex items-center justify-center gap-2"
                      >
                        {startingDeploy ? (
                          <>
                            <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                            </svg>
                            Starting...
                          </>
                        ) : (
                          <>
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
                            </svg>
                            Deploy to {currentRun.hostname}
                          </>
                        )}
                      </button>
                    </div>
                  )}

                  {/* Deploy Started Confirmation */}
                  {deployStarted && (
                    <div className="mt-3 pt-3 border-t border-green-200 flex items-center gap-2 text-green-700">
                      <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                      </svg>
                      Deployment started! Check the History page for results.
                    </div>
                  )}
                </div>
              )}
            </div>

            <div className="p-4 border-t dark:border-gray-700 flex justify-end items-center gap-2 bg-gray-50 dark:bg-gray-700">
              <button
                onClick={() => setShowPatternModal(false)}
                className="px-4 py-2 bg-gray-200 dark:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-500"
              >
                Close
              </button>
              <button
                onClick={addExclusionToConfig}
                disabled={selectedFieldIndex === null || !selectedConfigId || addingExclusion || configs.length === 0}
                className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 disabled:opacity-50 flex items-center gap-2"
              >
                {addingExclusion ? (
                  <>
                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                    Adding...
                  </>
                ) : (
                  <>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
                    </svg>
                    Add Exclusion
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Purge Confirmation Modal */}
      {showPurgeConfirm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-md w-full m-4 p-6">
            <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Purge Analysis History
            </h3>
            <p className="text-gray-600 dark:text-gray-400 mb-4">
              This will permanently delete noise analysis runs and their results older than the specified number of days.
            </p>
            <div className="mb-4">
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                Delete runs older than:
              </label>
              <select
                value={purgeDays}
                onChange={(e) => setPurgeDays(Number(e.target.value))}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
              >
                <option value={7}>7 days</option>
                <option value={14}>14 days</option>
                <option value={30}>30 days</option>
                <option value={60}>60 days</option>
                <option value={90}>90 days</option>
                <option value={0}>All runs</option>
              </select>
            </div>
            <div className="flex gap-3 justify-end">
              <button
                onClick={() => setShowPurgeConfirm(false)}
                disabled={purging}
                className="px-4 py-2 text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
              >
                Cancel
              </button>
              <button
                onClick={handlePurge}
                disabled={purging}
                className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50"
              >
                {purging ? 'Purging...' : 'Purge'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
