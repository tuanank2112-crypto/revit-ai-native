/**
 * Named-pipe client for talking to the Revit 2024 add-in.
 * Sends length-prefixed JSON envelopes and correlates responses by requestId.
 */

import { EventEmitter } from 'events';
import { randomFillSync } from 'crypto';

const PROTOCOL_VERSION = '1.0';
const MAX_MESSAGE_BYTES = 1048576; // 1 MiB

export interface PipeEnvelope {
  protocolVersion: string;
  requestId: string;
  type: string;
  method?: string;
  payload?: unknown;
  data?: unknown;
  error?: PipeError;
  success?: boolean;
  timestampUtc: string;
  correlationId?: string;
}

export interface PipeError {
  code: string;
  message: string;
  recoverable: boolean;
  suggestedAction?: string;
  details?: Record<string, unknown>;
}

export class PipeClient extends EventEmitter {
  private pipePath: string;
  private pipeName: string;
  private requestTimeout: number;
  private heartbeatInterval: number;
  private connected = false;
  private connecting: Promise<void> | null = null;
  private pending = new Map<string, { resolve: (v: unknown) => void; reject: (e: Error) => void; timer: NodeJS.Timeout }>();
  private heartbeatTimer: NodeJS.Timeout | null = null;
  private readerBuffer: Buffer[] = [];
  private readerExpecting = 0;
  private socket: import('net').Socket | undefined;

  constructor(pipeName: string, options?: { requestTimeoutMs?: number; heartbeatIntervalMs?: number }) {
    super();
    if (!pipeName) throw new Error('pipeName is required');
    this.pipeName = pipeName;
    this.pipePath = `\\\\.\\pipe\\${pipeName}`;
    this.requestTimeout = options?.requestTimeoutMs ?? 30000;
    this.heartbeatInterval = options?.heartbeatIntervalMs ?? 15000;
  }

  isConnected(): boolean {
    return this.connected;
  }

  async ensureConnected(timeoutMs: number = 5000): Promise<void> {
    if (this.connected && this.socket) return;
    if (this.connecting) return this.connecting;
    this.connecting = this.connect(timeoutMs);
    try {
      await this.connecting;
    } finally {
      this.connecting = null;
    }
  }

  async connect(timeoutMs: number = 5000): Promise<void> {
    // If a previous socket exists, drop it before reconnecting.
    if (this.socket) {
      this.socket.removeAllListeners();
      this.socket.destroy();
      this.socket = undefined;
    }

    const net = await import('net');
    console.error(`[MCP] connecting to ${this.pipePath} (timeout ${timeoutMs}ms)...`);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        reject(createPipeError('CONNECT_TIMEOUT',
          `Could not connect to ${this.pipePath} within ${timeoutMs}ms.`, true,
          'Ensure Revit is running and the add-in is loaded.'));
      }, timeoutMs);

      let settled = false;
      const onConnected = () => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        this.connected = true;
        this.startHeartbeat();
        this.startReader();
        console.error('[MCP] transport connected');
        this.emit('connected');
        resolve();
      };

      const onError = (err: Error) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        console.error(`[MCP] connection error: ${err.message}`);
        this.connected = false;
        reject(err);
      };

      const onClose = () => {
        this.connected = false;
        this.stopHeartbeat();
        this.failPending(new Error('Pipe connection closed'));
        console.error('[MCP] pipe disconnected');
        this.emit('disconnected');
      };

      this.socket = net.createConnection({ path: this.pipePath }, onConnected);
      this.socket.on('error', onError);
      this.socket.on('close', onClose);
    });
  }

  async request(method: string, payload?: unknown, correlationId?: string): Promise<unknown> {
    await this.ensureConnected();

    if (!this.connected || !this.socket) {
      throw createPipeError('REVIT_NOT_CONNECTED', 'The Revit add-in is not connected.', true,
        'Ensure Revit is running and the add-in is loaded.');
    }

    const requestId = generateRequestId();
    const envelope = buildEnvelope('request', requestId, method, payload, undefined, undefined, correlationId);

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(requestId);
        reject(createPipeError('REQUEST_TIMEOUT',
          `Request '${method}' timed out after ${this.requestTimeout}ms.`, true,
          'Check whether Revit is busy with a modal dialog.'));
      }, this.requestTimeout);

      this.pending.set(requestId, { resolve, reject, timer });

      const json = JSON.stringify(envelope);
      const body = Buffer.from(json, 'utf8');
      if (body.length > MAX_MESSAGE_BYTES) {
        clearTimeout(timer);
        this.pending.delete(requestId);
        throw createPipeError('MESSAGE_TOO_LARGE', 'Request exceeds the pipe message limit.', false);
      }

      const header = Buffer.allocUnsafe(4);
      header.writeUInt32LE(body.length, 0);
      if (!this.socket) {
        clearTimeout(timer);
        this.pending.delete(requestId);
        throw createPipeError('REVIT_NOT_CONNECTED', 'The pipe connection was lost.', true);
      }
      this.socket.write(Buffer.concat([header, body]));
    });
  }

  /** Polls a Revit job until it reaches a terminal state or the caller timeout expires. */
  async waitForJob(
    jobId: string,
    options?: { timeoutMs?: number; initialPollMs?: number; maxPollMs?: number; correlationId?: string }
  ): Promise<unknown> {
    if (!jobId) throw new Error('jobId is required');

    const timeoutMs = clamp(options?.timeoutMs ?? 120000, 1000, 900000);
    const initialPollMs = clamp(options?.initialPollMs ?? 250, 50, 10000);
    const maxPollMs = clamp(options?.maxPollMs ?? 3000, initialPollMs, 30000);
    const startedAt = Date.now();
    let delayMs = initialPollMs;

    while (true) {
      const status = await this.request('job.status', { jobId }, options?.correlationId) as Record<string, any>;
      const wireStatus = String(status?.status ?? '');
      if (TERMINAL_JOB_STATUSES.has(wireStatus)) return status;

      const elapsedMs = Date.now() - startedAt;
      if (elapsedMs >= timeoutMs) {
        throw createPipeError(
          'JOB_WAIT_TIMEOUT',
          `Job '${jobId}' did not reach a terminal state within ${timeoutMs}ms.`,
          true,
          'Call job.status again or retry wait with a larger timeout.'
        );
      }

      await sleep(Math.min(delayMs, timeoutMs - elapsedMs));
      delayMs = Math.min(maxPollMs, Math.ceil(delayMs * 1.5));
    }
  }

  async disconnect(): Promise<void> {
    this.stopHeartbeat();
    this.failPending(new Error('Pipe connection closed by client'));
    if (this.socket) {
      this.socket.end();
      this.socket = undefined;
    }
    this.connected = false;
  }

  private startHeartbeat(): void {
    this.heartbeatTimer = setInterval(() => {
      if (!this.connected || !this.socket) return;
      const hbId = generateRequestId();
      const env = buildEnvelope('heartbeat', hbId, 'heartbeat', undefined);
      const json = JSON.stringify(env);
      const body = Buffer.from(json, 'utf8');
      const header = Buffer.allocUnsafe(4);
      header.writeUInt32LE(body.length, 0);
      try {
        this.socket.write(Buffer.concat([header, body]));
      } catch {
        // Heartbeat failure is silent
      }
    }, this.heartbeatInterval);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
  }

  private startReader(): void {
    if (!this.socket) return;
    this.socket.on('data', (chunk: Buffer) => {
      this.readerBuffer.push(chunk);
      this.processReaderBuffer();
    });
  }

  private processReaderBuffer(): void {
    while (true) {
      const combined = Buffer.concat(this.readerBuffer);
      if (combined.length < 4) break;

      if (this.readerExpecting === 0) {
        this.readerExpecting = combined.readUInt32LE(0);
      }

      if (combined.length < 4 + this.readerExpecting) break;

      const bodyBytes = combined.subarray(4, 4 + this.readerExpecting);
      const remaining = combined.subarray(4 + this.readerExpecting);

      this.readerBuffer = remaining.length > 0 ? [remaining] : [];
      this.readerExpecting = 0;

      try {
        const text = bodyBytes.toString('utf8');
        const envelope = JSON.parse(text) as PipeEnvelope;
        this.handleEnvelope(envelope);
      } catch {
        // Malformed frame; ignore
      }
    }
  }

  private handleEnvelope(envelope: PipeEnvelope): void {
    const pending = this.pending.get(envelope.requestId);
    if (!pending) return;

    clearTimeout(pending.timer);
    this.pending.delete(envelope.requestId);

    if (envelope.error) {
      const err = createPipeError(envelope.error.code, envelope.error.message,
        envelope.error.recoverable, envelope.error.suggestedAction);
      pending.reject(err);
    } else {
      pending.resolve(envelope.data);
    }
  }

  private failPending(error: Error): void {
    for (const [id, entry] of this.pending) {
      clearTimeout(entry.timer);
      entry.reject(error);
    }
    this.pending.clear();
  }
}

// --- Helpers ---

function generateRequestId(): string {
  const bytes = new Uint8Array(8);
  randomFillSync(bytes);
  return Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
}

function buildEnvelope(
  type: string,
  requestId: string,
  method: string | undefined,
  payload: unknown,
  data?: unknown,
  error?: PipeError,
  correlationId?: string
): PipeEnvelope {
  const env: PipeEnvelope = {
    protocolVersion: PROTOCOL_VERSION,
    requestId,
    type,
    method,
    timestampUtc: new Date().toISOString(),
  };

  if (correlationId) env.correlationId = correlationId;
  if (payload !== undefined) env.payload = payload;
  if (data !== undefined) { env.data = data; env.success = true; }
  if (error) { env.error = error; env.success = false; }

  return env;
}

const TERMINAL_JOB_STATUSES = new Set([
  'completed',
  'failed',
  'cancelled',
  'rolled_back',
  'timed_out',
]);

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.max(minimum, Math.min(maximum, Math.floor(value)));
}

function sleep(milliseconds: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function createPipeError(code: string, message: string, recoverable: boolean, suggestedAction?: string): Error {
  const err = new Error(message) as Error & { code: string; recoverable: boolean; suggestedAction?: string };
  err.code = code;
  err.recoverable = recoverable;
  err.suggestedAction = suggestedAction;
  return err;
}
