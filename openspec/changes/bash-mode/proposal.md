## Why

The agent can run a shell command only by asking the model to call the `bash` tool. When the user already knows the command — a `git status`, a build, a test run — that costs a model turn, a tool-selection decision and a round trip, and the command is paraphrased through the model instead of typed exactly. Tau and pi both answer this with a prompt prefix: `!` switches the input into bash mode and Enter runs the line as a shell command.

This is the first slice of the prompt-workflow epic (P5): the input surface learns one new mode, and the execution path is the shell tool that already exists.

## What Changes

- A `!` at the start of an empty input enters **bash mode**: the prompt renders `! ` in the accent colour, Enter runs the line, Esc leaves without running, and the result commits as one tool block whose header carries the exit code and the duration.
- `!!` is the silent form: the command runs and renders the same way, but neither the command nor its output enters the conversation, so the model never sees it.
- The command runs through the **bash tool's executor**, not a second shell path: same approval policy, same shell resolution, same environment, same timeout and process-tree rules, same output bounding.
- Bash commands keep their **own history stream**: Up recalls them in bash mode only, and prompt history is unchanged.
- `!` anywhere but the start of the input stays literal, so "explain the `!` operator" is still a prompt.
- A `!` submission while a turn is running is refused with a notice; it does not queue and does not steer.

## What Does Not Change

- Enter still submits a prompt and steers a running turn; Alt+Enter still inserts a newline.
- The session file format, the event stream and the extension surface are untouched.
- No new configuration key: the mode is a property of the input, not a setting.
