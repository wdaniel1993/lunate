'use strict';

// The Lunate remote extension reference: a dependency-free Node script speaking the NDJSON
// JSON-RPC 2.0 wire documented in docs/extensions-remote.md. It answers `initialize` (protocol 2,
// advertising the `commands` capability), blocks a probe tool call while consuming the host's
// `core/workspace/get` service (the reentrant path), registers an `echo` service, serves the
// `ping` / `wait` / `fail` commands over `command/invoke`, and exits on `shutdown`. Stdout is
// protocol-only; every diagnostic goes to stderr, which the host caps into the extension log.

const readline = require('readline');

const PROTOCOL_VERSION = 2;
const HOOKS = ['ToolCalling'];
const SERVICES = ['echo'];
const BLOCKED_PATH = 'block-me.txt';

const pending = new Map();
// Command invocations waiting for their cancellation: request id -> the function that answers
// `cancelled` and forgets the id. A cancel for an id that is not here is a no-op.
const waitingCommands = new Map();
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
        services: SERVICES,
        commands: true
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

// command/invoke: the bare name, the argument remainder and the request id the host framed it
// under. The reply carries exactly the output, the optional failure message and the disposition.
function handleCommandInvoke(id, params) {
    const name = (params && params.name) || '';
    const argument = (params && params.argument) || '';
    switch (name) {
        case 'ping':
            reply(id, {
                output: 'pong ' + argument,
                error: null,
                disposition: 'completed'
            });
            return;
        case 'wait':
            // Stays in flight until $/cancelRequest names this request id; then it answers
            // `cancelled` rather than failing, which is what the host renders as a silent end.
            waitingCommands.set(String(id), function () {
                waitingCommands.delete(String(id));
                reply(id, { output: null, error: null, disposition: 'cancelled' });
            });
            return;
        case 'fail':
            reply(id, {
                output: null,
                error: 'the node reference extension fails this command on purpose',
                disposition: 'failed'
            });
            return;
        default:
            reply(id, {
                output: null,
                error: "the node reference extension has no command '" + name + "'",
                disposition: 'failed'
            });
    }
}

function handleCancel(params) {
    const target = params && params.id;
    const end = waitingCommands.get(String(target));
    if (end) {
        end();
    }
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
        handleCancel(frame.params);
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
        case 'command/invoke':
            handleCommandInvoke(frame.id, frame.params);
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
