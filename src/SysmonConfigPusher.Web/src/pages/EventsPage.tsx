import { useState, useEffect } from 'react';
import { computersApi, eventsApi } from '../api';
import type { Computer, SysmonEvent, EventQueryRequest } from '../types';
import { SYSMON_EVENT_TYPES } from '../types';

export function EventsPage() {
  const [computers, setComputers] = useState<Computer[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [events, setEvents] = useState<SysmonEvent[]>([]);
  const [loading, setLoading] = useState(false);
  const [querying, setQuerying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedEvent, setSelectedEvent] = useState<SysmonEvent | null>(null);
  const [showRawXml, setShowRawXml] = useState(false);

  // Filters
  const [eventId, setEventId] = useState<number | undefined>();
  const [timeRange, setTimeRange] = useState<string>('1h');
  const [processName, setProcessName] = useState('');
  const [destinationIp, setDestinationIp] = useState('');
  const [dnsQueryName, setDnsQueryName] = useState('');
  const [maxResults, setMaxResults] = useState(500);

  useEffect(() => {
    loadComputers();
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

  const getTimeRange = (): { startTime: string; endTime: string } => {
    const now = new Date();
    const end = now.toISOString();
    let start: Date;

    switch (timeRange) {
      case '15m':
        start = new Date(now.getTime() - 15 * 60 * 1000);
        break;
      case '1h':
        start = new Date(now.getTime() - 60 * 60 * 1000);
        break;
      case '6h':
        start = new Date(now.getTime() - 6 * 60 * 60 * 1000);
        break;
      case '24h':
        start = new Date(now.getTime() - 24 * 60 * 60 * 1000);
        break;
      case '7d':
        start = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
        break;
      default:
        start = new Date(now.getTime() - 60 * 60 * 1000);
    }

    return { startTime: start.toISOString(), endTime: end };
  };

  const queryEvents = async () => {
    if (selectedIds.size === 0) {
      setError('Please select at least one computer');
      return;
    }

    setQuerying(true);
    setError(null);

    try {
      const { startTime, endTime } = getTimeRange();
      const request: EventQueryRequest = {
        computerIds: Array.from(selectedIds),
        startTime,
        endTime,
        maxResults,
      };

      if (eventId) request.eventId = eventId;
      if (processName.trim()) request.processName = processName.trim();
      if (destinationIp.trim()) request.destinationIp = destinationIp.trim();
      if (dnsQueryName.trim()) request.dnsQueryName = dnsQueryName.trim();

      const result = await eventsApi.query(request);

      if (result.success) {
        setEvents(result.events);
        if (result.errorMessage) {
          setError(result.errorMessage);
        }
      } else {
        setError(result.errorMessage || 'Failed to query events');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to query events');
    } finally {
      setQuerying(false);
    }
  };

  const getEventTypeColor = (eventId: number): string => {
    switch (eventId) {
      case 1:
        return 'bg-blue-100 text-blue-800';
      case 3:
        return 'bg-green-100 text-green-800';
      case 7:
        return 'bg-purple-100 text-purple-800';
      case 11:
        return 'bg-yellow-100 text-yellow-800';
      case 22:
        return 'bg-orange-100 text-orange-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const formatTime = (dateStr: string) => {
    return new Date(dateStr).toLocaleString();
  };

  return (
    <div className="space-y-4">
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200 mb-4">Sysmon Event Viewer</h2>

        {/* Computer Selection */}
        <div className="mb-4">
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Select Computers ({selectedIds.size} selected)
          </label>
          {loading ? (
            <div className="text-gray-500 dark:text-gray-400">Loading computers...</div>
          ) : computers.length === 0 ? (
            <div className="text-gray-500 dark:text-gray-400">No computers with Sysmon found</div>
          ) : (
            <div className="flex flex-wrap gap-2 max-h-32 overflow-y-auto p-2 bg-gray-50 dark:bg-gray-700 rounded-lg">
              {computers.map((computer) => (
                <label
                  key={computer.id}
                  className={`flex items-center gap-2 px-3 py-1.5 rounded-lg cursor-pointer border transition-colors ${
                    selectedIds.has(computer.id)
                      ? 'bg-slate-100 dark:bg-slate-800 border-slate-400 dark:border-slate-600'
                      : 'bg-white dark:bg-gray-600 border-gray-200 dark:border-gray-500 hover:border-gray-300 dark:hover:border-gray-400'
                  }`}
                >
                  <input
                    type="checkbox"
                    checked={selectedIds.has(computer.id)}
                    onChange={() => toggleSelection(computer.id)}
                    className="w-4 h-4 rounded"
                  />
                  <span className="text-sm text-gray-900 dark:text-gray-100">{computer.hostname}</span>
                </label>
              ))}
            </div>
          )}
        </div>

        {/* Filters */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Event Type</label>
            <select
              value={eventId || ''}
              onChange={(e) => setEventId(e.target.value ? parseInt(e.target.value) : undefined)}
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            >
              <option value="">All Events</option>
              {SYSMON_EVENT_TYPES.map((type) => (
                <option key={type.id} value={type.id}>
                  {type.name}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Time Range</label>
            <select
              value={timeRange}
              onChange={(e) => setTimeRange(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            >
              <option value="15m">Last 15 minutes</option>
              <option value="1h">Last 1 hour</option>
              <option value="6h">Last 6 hours</option>
              <option value="24h">Last 24 hours</option>
              <option value="7d">Last 7 days</option>
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Process Name</label>
            <input
              type="text"
              value={processName}
              onChange={(e) => setProcessName(e.target.value)}
              placeholder="e.g., chrome.exe"
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Destination IP</label>
            <input
              type="text"
              value={destinationIp}
              onChange={(e) => setDestinationIp(e.target.value)}
              placeholder="e.g., 192.168.1.1"
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">DNS Query</label>
            <input
              type="text"
              value={dnsQueryName}
              onChange={(e) => setDnsQueryName(e.target.value)}
              placeholder="e.g., google.com"
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Max Results</label>
            <select
              value={maxResults}
              onChange={(e) => setMaxResults(parseInt(e.target.value))}
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            >
              <option value="100">100</option>
              <option value="500">500</option>
              <option value="1000">1000</option>
              <option value="2000">2000</option>
            </select>
          </div>
        </div>

        <button
          onClick={queryEvents}
          disabled={querying || selectedIds.size === 0}
          className="px-6 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 disabled:opacity-50"
        >
          {querying ? 'Querying...' : 'Query Events'}
        </button>
      </div>

      {error && (
        <div className="p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">{error}</div>
      )}

      {/* Results */}
      {events.length > 0 && (
        <div className="bg-white dark:bg-gray-800 rounded-lg shadow">
          <div className="p-4 border-b dark:border-gray-700">
            <h3 className="font-semibold text-gray-900 dark:text-gray-100">Results ({events.length} events)</h3>
          </div>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
              <thead className="bg-gray-50 dark:bg-gray-700">
                <tr>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Time</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Host</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Event</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Details</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
                {events.map((event, index) => (
                  <tr
                    key={`${event.computerId}-${event.timeCreated}-${index}`}
                    className="hover:bg-gray-50 dark:hover:bg-gray-700 cursor-pointer"
                    onClick={() => setSelectedEvent(event)}
                  >
                    <td className="px-4 py-3 text-sm text-gray-500 dark:text-gray-400 whitespace-nowrap">
                      {formatTime(event.timeCreated)}
                    </td>
                    <td className="px-4 py-3 text-sm font-medium text-gray-900 dark:text-gray-100">
                      {event.hostname}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`px-2 py-1 rounded text-xs font-medium ${getEventTypeColor(event.eventId)}`}>
                        {event.eventType}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-500 dark:text-gray-400 max-w-md truncate">
                      {event.eventId === 1 && (event.image || event.commandLine)}
                      {event.eventId === 3 && `${event.image} -> ${event.destinationIp}:${event.destinationPort}`}
                      {event.eventId === 7 && `${event.image} loaded ${event.imageLoaded}`}
                      {event.eventId === 11 && `${event.image} -> ${event.targetFilename}`}
                      {event.eventId === 22 && `${event.image} -> ${event.queryName}`}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Event Detail Modal */}
      {selectedEvent && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-4xl w-full max-h-[90vh] flex flex-col m-4">
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <div>
                <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">{selectedEvent.eventType}</h3>
                <span className="text-sm text-gray-500 dark:text-gray-400">
                  {selectedEvent.hostname} - {formatTime(selectedEvent.timeCreated)}
                </span>
              </div>
              <button
                onClick={() => {
                  setSelectedEvent(null);
                  setShowRawXml(false);
                }}
                className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
              >
                &times;
              </button>
            </div>

            <div className="flex-1 overflow-auto p-4">
              {!showRawXml ? (
                <div className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    {selectedEvent.processName && (
                      <div>
                        <label className="text-xs text-gray-500 dark:text-gray-400">Process</label>
                        <div className="font-mono text-sm text-gray-900 dark:text-gray-100">{selectedEvent.processName} (PID: {selectedEvent.processId})</div>
                      </div>
                    )}
                    {selectedEvent.image && (
                      <div className="col-span-2">
                        <label className="text-xs text-gray-500 dark:text-gray-400">Image</label>
                        <div className="font-mono text-sm break-all text-gray-900 dark:text-gray-100">{selectedEvent.image}</div>
                      </div>
                    )}
                    {selectedEvent.commandLine && (
                      <div className="col-span-2">
                        <label className="text-xs text-gray-500 dark:text-gray-400">Command Line</label>
                        <div className="font-mono text-sm break-all bg-gray-50 dark:bg-gray-700 p-2 rounded text-gray-900 dark:text-gray-100">{selectedEvent.commandLine}</div>
                      </div>
                    )}
                    {selectedEvent.user && (
                      <div>
                        <label className="text-xs text-gray-500 dark:text-gray-400">User</label>
                        <div className="font-mono text-sm text-gray-900 dark:text-gray-100">{selectedEvent.user}</div>
                      </div>
                    )}
                    {selectedEvent.parentImage && (
                      <div className="col-span-2">
                        <label className="text-xs text-gray-500 dark:text-gray-400">Parent</label>
                        <div className="font-mono text-sm break-all text-gray-900 dark:text-gray-100">{selectedEvent.parentImage} (PID: {selectedEvent.parentProcessId})</div>
                      </div>
                    )}
                    {selectedEvent.destinationIp && (
                      <div>
                        <label className="text-xs text-gray-500 dark:text-gray-400">Destination</label>
                        <div className="font-mono text-sm text-gray-900 dark:text-gray-100">{selectedEvent.destinationIp}:{selectedEvent.destinationPort}</div>
                      </div>
                    )}
                    {selectedEvent.sourceIp && (
                      <div>
                        <label className="text-xs text-gray-500 dark:text-gray-400">Source</label>
                        <div className="font-mono text-sm text-gray-900 dark:text-gray-100">{selectedEvent.sourceIp}:{selectedEvent.sourcePort}</div>
                      </div>
                    )}
                    {selectedEvent.targetFilename && (
                      <div className="col-span-2">
                        <label className="text-xs text-gray-500 dark:text-gray-400">Target File</label>
                        <div className="font-mono text-sm break-all text-gray-900 dark:text-gray-100">{selectedEvent.targetFilename}</div>
                      </div>
                    )}
                    {selectedEvent.queryName && (
                      <div className="col-span-2">
                        <label className="text-xs text-gray-500 dark:text-gray-400">DNS Query</label>
                        <div className="font-mono text-sm text-gray-900 dark:text-gray-100">{selectedEvent.queryName}</div>
                      </div>
                    )}
                    {selectedEvent.imageLoaded && (
                      <div className="col-span-2">
                        <label className="text-xs text-gray-500 dark:text-gray-400">Image Loaded</label>
                        <div className="font-mono text-sm break-all text-gray-900 dark:text-gray-100">{selectedEvent.imageLoaded}</div>
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <pre className="text-xs font-mono bg-gray-50 dark:bg-gray-700 p-4 rounded overflow-x-auto whitespace-pre text-gray-900 dark:text-gray-100">
                  {selectedEvent.rawXml}
                </pre>
              )}
            </div>

            <div className="p-4 border-t dark:border-gray-700 flex justify-between items-center bg-gray-50 dark:bg-gray-700">
              <button
                onClick={() => setShowRawXml(!showRawXml)}
                className="px-4 py-2 text-slate-600 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200"
              >
                {showRawXml ? 'Show Details' : 'Show Raw XML'}
              </button>
              <button
                onClick={() => {
                  setSelectedEvent(null);
                  setShowRawXml(false);
                }}
                className="px-4 py-2 bg-gray-200 dark:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-500"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
