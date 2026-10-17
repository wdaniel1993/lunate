'use strict';

// The Lunate remote extension reference: a dependency-free Node script speaking the NDJSON
// JSON-RPC 2.0 wire documented in docs/extensions-remote.md. It answers `initialize`, blocks a
// probe tool call while consuming the host's `core/workspace/get` service (the reentrant path),
// registers an `echo` service and exits on `shutdown`. Stdout is protocol-only; every diagnostic
// goes to stderr, which the host caps into the extension log.

const readline = require('readline');

const PROTOCOL_VERSION = 1;
const HOOKS = ['ToolCalling'];
const SERVICES = ['echo'];
const BLOCKED_PATH = 'block-me.txt';

const pending = new Map();
let nextChildId = 1;

function send(frame) {
    process.stdout.write(JSON.stringify(frame) + '\n');
}

function reply(id, result) {
    send({ jsonrpc: '2.0', id: id, result: result });
}

function replyError(id, code, message) {
    send({ jsonrpc: '2.0', id: id, error: { code: code, message: message } });
}

// Child -> host: separate id space (string ids) from the host's numeric ids.
function callHost(method, params) {
    return new Promise((resolve, reject) => {
        const id = 'child-' + nextChildId++;
        pending.set(id, { resolve: resolve, reject: reject });
        send({ jsonrpc: '2.0', id: id, method: method, params: params });
    });
}

async function handleInitialize(id) {
    reply(id, {
        protocolVersion: PROTOCOL_VERSION,
        hooks: HOOKS,
        services: SERVICES
    });
}

async function handleToolCalling(id, params) {
    const payload = (params && params.payload) || {};
    const args = payload.arguments || {};
    let workspace = 'unavailable';
    try {
        const answer = await callHost('service/core/workspace/get', {});
        if (answer && typeof answer.worktreeRoot === 'string') {
            workspace = answer.worktreeRoot;
        }
    } catch (error) {
        process.stderr.write('workspace lookup failed: ' + error.message + '\n');
    }

    if (args.path === BLOCKED_PATH) {
        reply(id, {
            outcome: 'block',
            reason: 'blocked by the node reference extension (workspace=' + workspace + ')'
        });
        return;
    }

    reply(id, { outcome: 'proceed', arguments: null });
}

function handleServiceCall(id, params) {
    const service = (params && params.service) || '';
    const method = (params && params.method) || '';
    const args = (params && params.arguments) || {};
    if (service === 'echo' && method === 'repeat') {
        reply(id, { repeated: typeof args.text === 'string' ? args.text : null });
        return;
    }

    replyError(id, -32601, "service '" + service + "' has no method '" + method + "'");
}

function handleFrame(frame) {
    if (typeof frame.method !== 'string') {
        const entry = pending.get(frame.id);
        if (entry) {
            pending.delete(frame.id);
            if (frame.error) {
                entry.reject(new Error(frame.error.message || 'host call failed'));
            } else {
                entry.resolve(frame.result);
            }
        }
        return;
    }

    if (frame.method === '$/cancelRequest') {
        return;
    }

    if (frame.id === undefined) {
        return;
    }

    switch (frame.method) {
        case 'initialize':
            handleInitialize(frame.id);
            return;
        case 'hook/ToolCalling':
            handleToolCalling(frame.id, frame.params);
            return;
        case 'service-call':
            handleServiceCall(frame.id, frame.params);
            return;
        case 'shutdown':
            reply(frame.id, null);
            setTimeout(function () {
                process.exit(0);
            }, 10);
            return;
        default:
            replyError(frame.id, -32601, "unknown method '" + frame.method + "'");
    }
}

const input = readline.createInterface({ input: process.stdin });
input.on('line', function (line) {
    if (line.trim().length === 0) {
        return;
    }

    let frame;
    try {
        frame = JSON.parse(line);
    } catch (error) {
        process.stderr.write('the host sent a non-JSON line: ' + error.message + '\n');
        return;
    }

    try {
        handleFrame(frame);
    } catch (error) {
        process.stderr.write('handler failed: ' + error.message + '\n');
        if (frame.id !== undefined) {
            replyError(frame.id, -32603, 'handler failed: ' + error.message);
        }
    }
});
