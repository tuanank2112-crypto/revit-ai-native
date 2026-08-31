#!/usr/bin/env node
/**
 * Autodesk Native Agent MCP Server
 *
 * Bridges AI agents (Antigravity, Claude, etc.) to Revit 2024 through the
 * named-pipe protocol. Exposes Revit inspection and plan execution as MCP tools.
 */

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ListToolsRequestSchema, ErrorCode, McpError } from '@modelcontextprotocol/sdk/types.js';
import { PipeClient } from './pipe-client.js';

// --- Pipe client management ---

let pipeClient: PipeClient | null = null;

function getPipeClient(): PipeClient {
  if (!pipeClient || !pipeClient.isConnected()) {
    const user = process.env.USERNAME || process.env.USER || 'default';
    const pipeName = `autodesk-native-agent-${sanitizeUser(user)}`;
    console.error(`[MCP] username=${user}`);
    console.error(`[MCP] pipeName=${pipeName}`);
    console.error(`[MCP] pipePath=\\.\pipe\${pipeName}`);
    pipeClient = new PipeClient(pipeName, { requestTimeoutMs: 30000 });
  }

  return pipeClient;
}

function sanitizeUser(user: string): string {
  const cleaned = user.toLowerCase().replace(/[^a-z0-9]/g, '');
  return cleaned || 'default';
}

// --- Tool definitions ---

const tools = [
  {
    name: 'revit_get_status',
    description: 'Returns the connection status, active document title/path, and busy state of the Revit add-in.',
    inputSchema: { type: 'object', properties: {} }
  },
  {
    name: 'revit_get_capabilities',
    description: 'Returns the protocol version, supported methods, and registered operations.',
    inputSchema: { type: 'object', properties: {} }
  },
  {
    name: 'revit_inspect_document',
    description: 'Returns identity of the active document: title, path, project info, fingerprint, read-only/workshared flags.',
    inputSchema: { type: 'object', properties: {} }
  },
  {
    name: 'revit_inspect_selection',
    description: 'Returns the current Revit selection as element summaries (elementId, uniqueId, category, name, typeName).',
    inputSchema: { type: 'object', properties: {} }
  },
  {
    name: 'revit_query_elements',
    description: 'Runs a structured query against the document using FilteredElementCollector. Returns matching element summaries.',
    inputSchema: {
      type: 'object',
      required: ['categories'],
      properties: {
        categories: { type: 'array', items: { type: 'string' }, description: 'Revit category names (e.g. "Walls", "Doors")' },
        where: { type: 'object', description: 'Filter conditions with all/any clauses' },
        limit: { type: 'integer', default: 100, description: 'Max results (1-1000)' }
      }
    }
  },
  {
    name: 'revit_validate_plan',
    description: 'Validates a plan structurally and against the command allowlist. Does not execute.',
    inputSchema: {
      type: 'object',
      required: ['plan'],
      properties: {
        plan: {
          type: 'object',
          description: 'The agent plan. Must have: schemaVersion, requestId, description, document, units, coordinateSystem, executionMode, operations[], safety{}.',
          properties: {
            schemaVersion: { type: 'string', const: '1.0' },
            requestId: { type: 'string' },
            description: { type: 'string' },
            document: { type: 'object' },
            units: { type: 'string', enum: ['mm', 'cm', 'm', 'inch', 'ft'] },
            coordinateSystem: { type: 'string', enum: ['project', 'internal'] },
            executionMode: { type: 'string', enum: ['preview', 'validate', 'commit'] },
            operations: { type: 'array', items: { type: 'object' } },
            safety: { type: 'object' }
          }
        }
      }
    }
  },
  {
    name: 'revit_preview_plan',
    description: 'Dry-runs a plan: resolution only, never mutates the model. Returns a preview report with resolved types/levels.',
    inputSchema: {
      type: 'object',
      required: ['plan'],
      properties: {
        plan: {
          type: 'object',
          description: 'The agent plan. Must have: schemaVersion, requestId, description, document, units, coordinateSystem, executionMode, operations[], safety{}.',
          properties: {
            schemaVersion: { type: 'string', const: '1.0' },
            requestId: { type: 'string' },
            description: { type: 'string' },
            document: { type: 'object' },
            units: { type: 'string', enum: ['mm', 'cm', 'm', 'inch', 'ft'] },
            coordinateSystem: { type: 'string', enum: ['project', 'internal'] },
            executionMode: { type: 'string', enum: ['preview', 'validate', 'commit'] },
            operations: { type: 'array', items: { type: 'object' } },
            safety: { type: 'object' }
          }
        }
      }
    }
  },
  {
    name: 'revit_commit_plan',
    description: 'Executes a plan inside a transaction group after confirmation. Returns a job id for tracking.',
    inputSchema: {
      type: 'object',
      required: ['plan'],
      properties: {
        plan: {
          type: 'object',
          description: 'The agent plan. Must have: schemaVersion, requestId, description, document, units, coordinateSystem, executionMode, operations[], safety{}.',
          properties: {
            schemaVersion: { type: 'string', const: '1.0' },
            requestId: { type: 'string' },
            description: { type: 'string' },
            document: { type: 'object' },
            units: { type: 'string', enum: ['mm', 'cm', 'm', 'inch', 'ft'] },
            coordinateSystem: { type: 'string', enum: ['project', 'internal'] },
            executionMode: { type: 'string', enum: ['preview', 'validate', 'commit'] },
            operations: { type: 'array', items: { type: 'object' } },
            safety: { type: 'object' }
          }
        }
      }
    }
  },
  {
    name: 'revit_confirm_plan',
    description: 'Accepts or rejects a pending plan confirmation. Call job.status first to obtain the confirmationToken, then accept to run the plan or reject to cancel it.',
    inputSchema: {
      type: 'object',
      required: ['jobId', 'action'],
      properties: {
        jobId: { type: 'string' },
        action: { type: 'string', enum: ['accept', 'reject'], description: "'accept' to run the plan, 'reject' to cancel" },
        token: { type: 'string', description: 'The confirmationToken from job.status' }
      }
    }
  },
  {
    name: 'revit_get_job_status',
    description: 'Returns the current job status and latest execution result.',
    inputSchema: { type: 'object', required: ['jobId'], properties: { jobId: { type: 'string' } } }
  },
  {
    name: 'revit_cancel_job',
    description: 'Requests cancellation of a queued or running job.',
    inputSchema: { type: 'object', required: ['jobId'], properties: { jobId: { type: 'string' } } }
  },
  {
    name: 'revit_rollback_job',
    description: 'Rolls back a completed job when the runtime supports it.',
    inputSchema: { type: 'object', required: ['jobId'], properties: { jobId: { type: 'string' } } }
  },
  {
    name: 'revit_get_audit_log',
    description: 'Returns sanitized audit log entries.',
    inputSchema: {
      type: 'object',
      properties: {
        actionPrefix: { type: 'string', description: 'Filter by action prefix (e.g. "plan.")' },
        limit: { type: 'integer', default: 500, description: 'Max entries to return' }
      }
    }
  }
];

// --- MCP Server ---

const server = new Server(
  { name: 'autodesk-native-agent', version: '1.0.0' },
  { capabilities: { tools: {} } }
);

// List tools
server.setRequestHandler(ListToolsRequestSchema, async () => {
  return { tools };
});

// Call tool
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: argsRaw } = request.params;
  const args: Record<string, any> = argsRaw ?? {};

  try {
    const client = getPipeClient();
    let method: string;
    let payload: unknown;

    switch (name) {
      case 'revit_get_status':
        method = 'status';
        payload = {};
        break;
      case 'revit_get_capabilities':
        method = 'capabilities';
        payload = {};
        break;
      case 'revit_inspect_document':
        method = 'document.get_info';
        payload = {};
        break;
      case 'revit_inspect_selection':
        method = 'selection.get';
        payload = {};
        break;
      case 'revit_query_elements':
        method = 'element.query';
        payload = args;
        break;
      case 'revit_validate_plan':
        method = 'plan.validate';
        payload = args.plan;
        break;
      case 'revit_preview_plan':
        method = 'plan.preview';
        payload = args.plan;
        break;
      case 'revit_commit_plan':
        method = 'plan.commit';
        payload = args.plan;
        break;
      case 'revit_confirm_plan':
        method = 'plan.confirm';
        payload = { jobId: args.jobId, action: args.action, token: args.token };
        break;
      case 'revit_get_job_status':
        method = 'job.status';
        payload = { jobId: args.jobId };
        break;
      case 'revit_cancel_job':
        method = 'job.cancel';
        payload = { jobId: args.jobId };
        break;
      case 'revit_rollback_job':
        method = 'job.rollback';
        payload = { jobId: args.jobId };
        break;
      case 'revit_get_audit_log':
        method = 'audit.log';
        payload = { actionPrefix: args.actionPrefix, limit: args.limit };
        break;
      default:
        throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${name}`);
    }

    const data = await client.request(method, payload);
    return { content: [{ type: 'text', text: JSON.stringify(data, null, 2) }] };
  } catch (error) {
    const err = error as Error & { code?: string; recoverable?: boolean; suggestedAction?: string };
    const errorData = {
      error: {
        code: err.code || 'INTERNAL_ERROR',
        message: err.message,
        recoverable: err.recoverable ?? false,
        suggestedAction: err.suggestedAction
      }
    };
    return {
      content: [{ type: 'text', text: JSON.stringify(errorData, null, 2) }],
      isError: true
    };
  }
});

// --- Start ---

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  // The server runs over stdio; it stays alive until the client disconnects.
}

main().catch((err) => {
  console.error('Fatal error:', err);
  process.exit(1);
});
