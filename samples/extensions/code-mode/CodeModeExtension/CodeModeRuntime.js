// The code-mode runtime: it owns the framed JSON-lines protocol between the child process and the
// extension. Every stdout line is one frame; script output can never be mistaken for protocol
// because console.* and process.stdout.write both become log frames, while the protocol itself
// writes through the saved original stdout handle.
(async () => {
  "use strict";

  const __originalWrite = process.stdout.write.bind(process.stdout);

  function __emit(frame) {
    try {
      __originalWrite(JSON.stringify(frame) + "\n");
    } catch (error) {
      // The protocol pipe is gone; the exit path decides what the run means.
    }
  }

  function __format(value) {
    if (typeof value === "string") {
      return value;
    }
    try {
      return JSON.stringify(value);
    } catch (error) {
      return String(value);
    }
  }

  process.stdout.write = function (chunk, encoding, callback) {
    const text =
      typeof chunk === "string" ? chunk : Buffer.from(chunk, encoding).toString("utf8");
    __emit({ type: "log", text: text });
    if (typeof callback === "function") {
      callback();
    }
    return true;
  };
  for (const method of ["log", "info", "warn", "error", "debug"]) {
    console[method] = (...args) => {
      __emit({ type: "log", text: args.map(__format).join(" ") + "\n" });
    };
  }

  // The handshake goes first, before any script code can run.
  __emit({ type: "hello", protocol: 1 });

  const __pending = new Map();
  let __nextId = 1;
  const __reader = require("readline").createInterface({ input: process.stdin });
  __reader.on("line", (line) => {
    let frame;
    try {
      frame = JSON.parse(line);
    } catch (error) {
      return;
    }
    if (frame && frame.type === "result" && __pending.has(frame.id)) {
      const resolve = __pending.get(frame.id);
      __pending.delete(frame.id);
      resolve(frame);
    }
  });

  function __call(tool, args) {
    const id = __nextId++;
    const promise = new Promise((resolve) => __pending.set(id, resolve));
    __emit({
      type: "call",
      id: id,
      tool: tool,
      args: args === undefined || args === null ? {} : args,
    });
    return promise.then((result) => {
      if (result.isError) {
        const error = new Error(result.output);
        error.tool = tool;
        throw error;
      }
      return result.output;
    });
  }

  // The generated stubs of this run's programmatically callable tools.
  // __LUNATE_STUBS__

  let __result;
  let __hasResult = false;
  let __failed = false;
  try {
    __result = await (async () => {
      // __LUNATE_SCRIPT__
    })();
    __hasResult = typeof __result !== "undefined";
  } catch (error) {
    __failed = true;
    __emit({ type: "log", text: (error && error.stack ? error.stack : String(error)) + "\n" });
  }

  __reader.close();

  let __finalResult = null;
  if (!__failed && __hasResult) {
    try {
      __finalResult = JSON.parse(JSON.stringify(__result));
    } catch (error) {
      __finalResult = String(__result);
    }
  }
  __emit({ type: "final", result: __finalResult, hasResult: __hasResult && !__failed, failed: __failed });
  process.exitCode = __failed ? 1 : 0;
})();
