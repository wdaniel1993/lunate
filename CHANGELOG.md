# Changelog

Notable changes to Lunate, one section per release. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to use
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are tagged on
[GitHub Releases](https://github.com/lunate-agent/lunate/releases); the release process itself is in
[`docs/distribution.md`](docs/distribution.md).

## [Unreleased]

## [0.4.0] - 2026-11-14

### Added

- **The setup wizard walks back, forward and in modes.** Every step keeps its answers, `←`/`→` move
  between answered steps, a returning entry menu edits several settings in one pass, a multi-select
  binds several routing profiles, and a Quick walk asks the minimum while naming every default it
  skipped and the command that changes it (#681, #682).

- **One wizard run configures several providers.** A second provider is collected in the same walk,
  a duplicate name asks for a rename, a credential already stored is reused by reference, and every
  provider lands in one commit and one review (#683).

- **Fast and decision usages can come from any provider.** Each usage step offers the providers
  configured in this run, so a provider set up inline reaches the same review and the same commit,
  and `usages.chat` keeps its own profile (#684).

- **The wizard shows a file manifest and a write receipt.** Before the summary confirms, every
  staged value is grouped by the file it will reach, with the routing profiles the multi-select
  confirmed and never a typed key; after the commit, a receipt names what was written and what was
  left alone (#685).

- **A normal writer no longer loses the wizard's commit.** The commit takes the settings and
  provider-store claims before it seeds its bodies, and an unclaimable commit refuses without
  writing (#668).

- **A documentation checker and navigation manifest.** `scripts/docs-check` keeps every relative
  link, `#anchor`, redirect stub and `docs/docs.json` navigation entry honest, and its self-test plus
  Python suite pin the failure paths; it runs in the docs and platform verify profiles and the
  always-on tree checks (#749).

- **Per-PR changelog fragments.** Each pull request writes `changes/<number>.md` and never edits
  `CHANGELOG.md`; `scripts/changelog-assemble` folds the fragments into the unreleased section in
  pull-request order, the release pull request runs it, and `ci-ok` refuses a changelog edit from
  any other pull request (#794).

- **A fragment bullet may cite several pull requests.** The line ends with one or more references —
  `(#681, #682).`, the changelog's own existing shape — so a change that landed under several pull
  requests keeps its wording, and the assembler's `--check` is proven on a copy of this repository's
  own fragments (#794).

- **A release tag must come from a merged release pull request.** The Release workflow refuses a
  tag whose commit is not on `main` or not associated with a merged release pull request, and
  `scripts/release-contract` reads the published release back after the assets are attached; the
  lane's own release and merge execution stays tracked in lunate-lane#21 (#794).

- **The Automate and Reference documentation groups are complete.** `print.md`, `json-events.md`,
  `acp.md` and `ci.md` document one-shot, machine-readable, editor-driven and unattended use, and
  `cli.md`, `providers.md` and `environment-variables.md` carry the look-up tables; the pages they
  moved out of link them, and a `scripts/docs-check --run` pass compares every option in `cli.md`
  with `lunate --help` in both directions (#827).

- **The first published SWE-bench Verified run is pinned, reproducible and committed.** The run
  definition names five Verified instances (every 100th id in lexicographic order), one budget
  OpenCode Go model and one frontier OpenRouter model, a single US$5.00 cost cap and a 50-step
  parity limit; `scripts/eval/swebench_run.py` runs the Lunate and mini-swe-agent arms on that
  same definition, prices every row, and takes every resolved count from the official harness.
  The published run `rerun-624-bosgame-2` resolved 2/5 and 4/5 for Lunate against 5/5 and 5/5 for
  the mini-swe-agent baseline, spent US$1.6157 of the cap, and records every failure; the README
  benchmark sentence now cites that measured pilot, including that the baseline was ahead, instead
  of the saturated C# suite (#624, #839).

- **A run's evidence is committed where its summary links it.** `rerun-624-bosgame-2` ships each
  instance's submitted `predictions.jsonl` and the official `report.json` for every instance that
  produced a patch under `eval/swebench/runs/`, every instance row of the committed summary and of
  the write-up points at it instead of a machine-local path, and the orchestrator's default work
  tree is the tracked `eval/swebench/runs`, so a clean checkout can follow a published resolved
  count to its official verdict (#624, #839).

### Changed

- **A code-owner review gates the files that can change what runs.** `.github/CODEOWNERS` names
  `@wdaniel1993` as the owner of `adr/`, `.github/`, `AGENTS.md` and `.agents/`, so the
  `protect-default` ruleset can require review from a code owner on the decisions, the workflows
  and the machinery that decides what CI enforces, and the instructions and skills every agent in
  this repository follows; every other path stays on the model review (#804).

- **The documentation check is written down as a contract, not just wired into CI.** `repo-quality`
  now states that every relative link and `#anchor` on the user and maintainer pages resolves, that
  every redirect is the four-line stub, that `docs/docs.json` lists each user-path page exactly once
  and that every page carries a runnable marker or a `proseOnly` reason; `ci-pipeline` names
  `scripts/docs-check --check` in the OS-independent set, in the docs profile and in the
  documentation-only push, so deleting the step is a spec change instead of a quiet regression
  (#818).

- **The maintainer documents live under `docs/dev/`.** The documentation map, the implementation
  plan, the architecture diagrams, the release runbook and the media set move there, their relative
  links follow and `AGENTS.md` names the new paths; `docs/README.md` and `docs/guide.md` keep a
  moved-page stub for one release so older links still land, and the repository-quality and
  distribution specs name the moved pages so the source of truth matches the tree (#751).

- **A misdeclared reasoning style cannot change the wire parameter.** A harness test sends a model
  declared `budget` on an OpenAI-compatible family and one declared `effort` on the Anthropic
  Messages family, so a regression back to the declared style fails the suite instead of shipping
  (#601, #828).

- **Settings and key bindings have a reference page of their own.** `settings.md` and
  `keybindings.md` carry the generated tables, and the guide, the TUI comments and the remaining
  cross-links point there instead of at the pages those tables left (#776, #829).

- **The tool child withholds credential-shaped variables by default.** A `bash` child no longer
  inherits a variable whose name has a `KEY`, `TOKEN`, `SECRET`, `PASSWORD` or `CREDENTIAL` part
  (split on `_` and `-`, case-insensitive), so `GITHUB_TOKEN` and `AWS_SECRET_ACCESS_KEY` stay out of
  it while `MONKEY` and `OPENAI_BASE_URL` arrive; one exact name in `toolEnvironment.passthrough`
  admits a withheld variable back (#741).

- **The picker lifecycle has one owner.** Open, move, filter, confirm, navigate and dismiss are
  messages applied in order by one consumer, `PickerOwner`, which is the only writer of the picker
  state and the only poster of `SetPicker(...)`. A dismissal can no longer clear or replace a picker
  another thread is installing, the wizard's pick is completed by the same owner, and the two
  regression seams that paused a transition mid-flight are gone (#735).

- **The interactive session has a page per task.** `quickstart.md`, `models-and-providers.md`,
  `decisions.md`, `export-copy-debug.md` and `terminal-setup.md` each own one job the first-run and
  session pages used to carry together — the wizard walk, the provider, credential, profile and
  catalog choices, the optional decision layer, `/copy`, `/export` and `/debug`, and the terminal
  matrix with the shell the `bash` tool resolves. `usage.md` keeps only the session, opens with what
  it answers and links the neighbours from Related instead of repeating them, and `first-run.md`
  keeps a four-line moved stub for one release (#753, #842).

- **The CI gate is sized by the event.** A pull request builds and tests on `ubuntu-latest` only, the
  merge queue runs the Ubuntu test gate without repeating the pull request's changes decision,
  changelog guard, platform/docs checks or PowerShell twin, and the full Ubuntu/Windows/macOS matrix
  with the PowerShell twin and the perf measurement runs on a push to `main` and the nightly. CodeQL
  leaves pull requests for the push to `main` and its weekly schedule (#831).

- **A red nightly files its own bug card.** One `kind/bug` card per failing job in milestone `0.4.0`,
  naming the job and the run link, commented on while an open card for that job exists instead of
  filed again (#831).

- **The documentation has an entry point.** `docs/index.md` opens the user path with the governing
  thought — the docs are organised around what you want to do — and lists every task group with the
  page that owns each one; `scripts/docs-check` keeps that list equal to `docs/docs.json`.
  `docs/how-lunate-works.md` gives the mental model the guide held alone: the turn loop, the tools
  and approvals, the context and compaction, sessions and the three modes, each linking the page that
  owns the detail. `configuration.md` moves from Use it to Customize, the README's Get started and
  capability links lead into the index, and the moved-page stubs stay for one more release (#757, #844).

- **Every user-path page closes with Related, and its example is checked.** The extension authoring
  and reference pages now end with a **Related** section like the rest of the user path, and the
  pointer at the end of `docs/dev/README.md`'s writing guideline records the pyramid shape the
  0.4.0 information architecture asks for. Seven pages that quote a real config document or command
  gained the marker `scripts/docs-check` reads — the Roslyn MCP host config and the remote manifest
  are config examples, the package, trust, extension-test and troubleshooting commands are
  `run: no` with their reason — and the remaining `proseOnly` entries state why the page has
  nothing the checker can run instead of announcing a slice that already landed (#614, #845).

- **`AGENTS.md` and the `openspec-git-discipline` skill describe the lane the repository actually
  runs on.** One implementation pull request per card, the archive as its last commit landing before
  review, a rework redoing it, and the reviewer reading the proposal delta first; an unattended worker
  decides within the card, records its `grill-me` answers under **Intent decisions** in the pull
  request (a missing list is a send-back), and keeps propose-and-stop only for an ADR-level decision;
  a conflict under `openspec/specs/` is reverted and re-archived, never hand-edited. `CONTRIBUTING.md`
  stops describing the proposal waiting on `main` and the archive added after review, and the
  `repo-quality` spec records the same rules (#849).

- **The merge queue runs the tree checks, and `ci-ok` requires them.** A `merge_group` run now runs
  ADR numbering, the spec-archive rule, the generated docs regions, the docs link/navigation check and
  the docs lint against the batch's base commit, so a batch in which two pull requests each took the
  same free ADR number is rejected by the queue instead of reaching `main` (#854).

- **One command list for the tree checks.** `scripts/tree-checks.sh` holds the five commands, and both
  the merge-queue job and the push-to-`main` workflow call it, so the two triggers cannot drift
  (#854).

### Fixed

- **Reasoning parameters follow the resolved API family.** A model whose declared `reasoning`
  style disagrees with its resolved API family now sends the parameter that family accepts —
  Anthropic extended thinking for `anthropic-messages`, reasoning effort for the OpenAI-compatible
  and Google families — with the declared style kept as metadata and fallback (#601).

- **`--help` names the `cache prune` and `extensions reload` subcommands.** Both commands ran but
  neither appeared in the process synopsis; `lunate --help` now lists each with its one-line
  description, and a test pins the two lines against the help output (#780).

- **The first-run flake no longer depends on which task the thread pool runs first.**
  `InteractiveSessionFirstRunTests.Escape_dismisses_the_first_run_offer_without_a_write` waited for
  the picker's frame and then typed `/quit` in the window before the walk behind the offer had
  cleared the session's turn flag; the quit took the follow-up queue's path, which ends the run
  without clearing the input line, so the prompt still held the text. The fixture now waits for the
  dismissal to finish, and says in the test and its support file what it depends on (#817).

- **The project MCP composition tests record trust where a session reads it.** The four
  project-file scenarios now key their trust record by the repository identity — the git common
  directory when the temp workspace sits inside a repository, the worktree root otherwise — and
  name the file by the canonical path the composition reads, so the suite passes in a checkout as
  well as outside one. A dedicated scenario pins that identity seam without relying on where
  `TMPDIR` points (#824).

- **No current document names the pre-move `docs/spec/`, `docs/spikes/` or `docs/eval/` paths.** The
  `extensions` spec points at the extensibility contract and the Roslyn migration baseline in their
  `docs/dev/` homes, and `scripts/docs-check` no longer lists the old prefixes as records or
  maintainer pages, so a page reintroduced there is reported instead of silently exempt (#825).

- **The scripted-setup example runs as written.** The `lunate setup` example in `docs/ci.md` names
  a model that declares a reasoning style, carries a placeholder key and `--no-test`, and is marked
  `run: offline`, so `scripts/docs-check --run` executes it instead of a reader meeting exit 2
  (#826).

- **`ci-ok` judges the live head, not the run's payload.** A rerun of a draft or older run now reads
  the current head's newest required-job attempts through the API, a still-running ready gate fails
  with the jobs named, and draft and ready runs use separate concurrency groups, so a draft run can
  neither redden nor cancel the ready verdict (#830).

- **An incomplete gate is red.** `ci-ok` reads the verify matrix's shard count from the legs that ran
  and fails, naming it, when a leg the plan requires has no check run on the head, so a missing shard
  can no longer ride on the shards that passed (#830).

- **The tool-environment tests no longer race a session's settings resolution.** The
  `tool-process-environment` collection now runs alone instead of merely sequentially with its own
  tests, so a session composed by another test cannot clear the process-wide pass-through list
  between a test's configure and its spawn, and a guard test pins the collection definition (#846).

- **The specs name the documentation pages that exist.** The CI prose example points at
  `docs/dev/guide.md`, and the key-table requirement names `docs/keybindings.md` instead of the guide,
  so the source of truth no longer sends a maintainer to a redirect stub or to a page that does not
  own the table (#848).

- **The CI-gate decision record gets its own number.** Two records landed on `main` as ADR-0036, which
  fails `scripts/check-adr-numbers.py` and so reddens the `verify-docs` profile; the later one, the
  per-event CI gate, becomes `adr/0037-ci-gate-sized-by-event.md` while the earlier tool-child record
  keeps `0036` (#848).

- **The `ask_user` repaint test no longer depends on the question picker's 300 ms arm window.**
  `LiveSurfaceRepaintTests.The_question_tool_repaints_without_leftover_rows_at_40_columns` advanced
  the fake clock exactly 300 ms past the picker's frame and then typed its answer. When the arm
  landed inside that advance the answer fell on the window's closed edge and was dropped, so the run
  waited for a decision no key could deliver and the next chat call never started. The picker now has
  the approval panel's seam (the next key's arm and an expire that does not move the clock), and the
  harness's `WaitForQuestionAsync` returns only once the next key decides (#863).

- **The spawn descriptor-list test no longer reads another test's pipe ends.** Its
  `spawn-descriptor-table` collection now runs alone, so no process another collection starts can
  leave a descriptor of its own in the parent's table while the probe children start, and a
  reflection test pins the collection definition (#865).

- **The setup-wizard multi-select test answers the prompt it waited for.** The routing-profile
  description loop waited on the generic `What is profile '` prefix, which the previous prompt's
  frame still carries, so a loaded runner could accept the typed answer before the next prompt
  opened and queue it as a follow-up instead of answering the prompt; the menu row then never
  arrived. Each round now waits for its own profile's prompt, and the test passes under de-AT and
  en-US (#866).

- **Settings and provider-store writes survive a second process.** Both stores held only an
  in-process lock, so two Lunate processes that each read first could each replace the file and the
  later one silently dropped the earlier change. Each read-modify-write now takes an owner-only
  `settings.lock` / `providers.lock` claim beside the file, waits a bounded time and merges, or
  refuses without writing (#584). The setup wizard's commit takes the same two claims before it
  seeds its staged bodies, so its three-file replace merges with another process's change instead
  of replacing it.

## [0.3.0] - 2026-11-09

The 0.3.0 quality release: a fresh install gets one guided setup path, a configured install gets a re-runnable entry menu, each model can route to its own native API family, and the decision layer's native System One transport lands behind the existing seam. The release also carries every rc.1 review fix, the rc.2 proof pass, the rc.3 proof pass, and the docs/spec agreement work the milestone asked for. The rc sections below record every change; highlights of the line:

### Added

- **The setup wizard.** A fresh interactive install opens one nine-step walk — provider, credential,
  connection test, model, a minimal model check, chat profile and thinking, optional fast and
  decision profiles, the decisions layer, and a summary. Nothing is written before the summary
  confirms, `Esc` writes nothing, an environment key is offered before typing one, and a skipped
  model check never counts as verified. `lunate setup` runs the same walk non-interactively from
  explicit flags and exits 2 naming the answer that only interaction can supply; `/setup` opens the
  full walk on a first run and an entry menu (add a provider, add a profile, change the chat profile,
  edit decisions, walk everything) on a configured install (#524).
- **Provider API families.** A provider entry carries one base URL per family — `openai-chat`,
  `openai-responses`, `anthropic-messages` and `google` — and a model's family comes from the
  profile, discovery or the catalog. Custom providers can carry literal or templated headers
  (`{session.id}`, `{run.id}`, `{lunate.version}`) and choose where the credential goes (`bearer`,
  `x-api-key`, a named header or a query parameter); the value is never part of a header template.
  The OpenAI Responses and Anthropic Messages paths are wired, and an Anthropic-compatible custom
  provider is accepted (#474, #484, #490, #495, #498).
- **Google Gemini directly.** The Google family ships with its native SDK adapter, per-family base
  URL and query-string credential placement, so a Gemini model can run without an OpenAI-compatible
  proxy (#474, #500).
- **OpenCode Go routes each model to its native API.** Discovery reads
  `https://opencode.ai/inference/go/v1/models`, and a request goes to the model's declared API
  family — Chat Completions, Responses, Anthropic Messages or Google — with the session header
  captured on the request (#473).
- **Native System One decisions.** `systemone` is the fifth provider family, served by a native
  decision transport rather than an `IChatClient`. It adds the four question kinds (noul, choice,
  score, box), confidence computed from the distribution, confidence-gated routing, atomic approval
  questions, provider-reported decision cost, exact-version profile pinning, request limits and
  `429`/`529` backoff. Jev-1.14 is preset on OpenRouter, Jev-1.13 on OpenCode, and provider quirks
  (criteria shape, image-only headers) stay preset data (#487).
- **Thinking per profile.** A profile's `reasoningLevel` seeds every session that uses it;
  `/thinking` changes the current session only, while `/settings thinking` and the wizard persist
  the profile. An unsupported model refuses the level by name instead of silently accepting it
  (#476).

### Changed

- **Tool processes no longer inherit provider API keys.** Every preset `KeyVariable` and its alias,
  plus each configured custom provider's `env:` reference, is stripped from a tool child's environment
  on all platforms, including Windows, where the spawn built its own environment; the user's own
  variables are untouched. An explicit pass-through list for named variables is #642 (0.4.0).
- **MCP stdio child environment is allow-listed.** A stdio server no longer inherits the session
  environment minus Lunate's known credential values (the 0.2 behaviour of #449/#455); it receives
  exactly the base set the shared `EnvironmentScrubber` builds — `PATH`, `HOME`/`USERPROFILE`,
  `SystemRoot`, `TEMP`/`TMP`/`TMPDIR`, `LANG` — plus its own declared `env` map and nothing else,
  configured or not (#472). The shared scrubber is annotated with the reason for each base entry,
  and a source-scan test keeps it the only child-environment code path.
- **Question suggestions stay interactive.** The suggestion consumer is wired only where a person
  answers; print mode and ACP have no surface to attach one to (#446).
- **CI: one platform job, and PowerShell only when it can break.** The platform-independent checks
  run once on Linux; the Windows PowerShell leg is path-filtered by a deny-by-default change
  detector and skips when the platform job already proved the same checks (#435).
- **The inert `McpServerOptions.WithheldCredentialValues` option is removed** from
  `Lunate.Protocols`; the allow-list scrub is the only child-environment path (#483).

- **`decisions.approvalsThreshold` is a confidence.** The key now carries what the spec always said it
  did: the confidence a risk signal's chosen answer must reach to escalate. The default `0` is the
  `2t − 1` form of the shipped `0.5` probability gate, so behaviour is unchanged unless the key is
  set; an explicit `0.5` now means a `0.75` probability of the risk (#573).

### Fixed

- **Setup and settings.** The wizard now persists the chosen region, honours `--api-family` for presets, runs the decision-profile model check, refuses a thinking level that conflicts with an existing `reasoningBudget`, and merges a re-run profile instead of replacing it (#568, #569, #570, #579, #580).
- **Runtime and providers.** `--no-session` with a `{session.id}` header resolves an ephemeral id; Anthropic prompt caching and turn merge follow the resolved family; a model/profile switch re-seeds thinking; a discovered endpoint survives profile resolution; the Windows MCP child environment includes `PATHEXT`, `ComSpec`, `APPDATA` and `LOCALAPPDATA` (#565, #571, #598, #599, #600).
- **Decisions.** Failed adapter attempts still record their usage; question-suggestion and extension decisions record usage and cost; the provider cost is persisted and survives `--resume`; typed transport errors reach the operator; approval state carries the untrusted arguments once; `approvalsThreshold` is confidence; a shadow capability failure records the failure and leaves the call unchanged; discovery refuses a scheme or port redirect (#559–#562, #572, #573, #558, #563).
- **Docs and specs.** `systemone` is the fifth API family everywhere; the architecture map and `AGENTS.md` record the 0.3 project graph; the guide no longer documents a `timeout_s` argument, `/shell` command or `shell` setting, and names `output.toolResultLimit` correctly (#574, #575, #577).

### CI

- The macOS `perf` leg runs on release events only; pull-request verify shards keep the startup budget, and gate self-tests retry once with a flake ledger.

## [0.3.0-rc.3] - 2026-11-08

Proof-pass fixes on top of rc.2, found by a real phone session over SSH as well as by CI.

### Fixed

- **Only the credential prompt masks.** The wizard, `/auth` and `/login` were masking every input, so a
  typo in a profile name hid behind asterisks and only showed up in the summary. Provider, endpoint,
  model id, profile name, region and descriptions echo normally; a secret's plaintext still never
  reaches a frame (#617).
- **The model check sends what the session sends.** The check built its client on a path of its own and
  reported `HTTP 400 — Model is unavailable` for a profile whose session then ran fine, because the
  request carried the provider-qualified id in the model field. It now builds through the production
  path (profile → provider → family → client), with a test asserting the check's request and the first
  session request are identical except the prompt (#613).
- **`/quit` during a running turn** cancels the turn and exits, per the in-force tui spec (#635).
- **SIGTERM and SIGHUP restore the terminal** before exit, so a killed session no longer leaves the
  terminal raw (#636).
- **Esc and Ctrl+C cancel a running tool call**, including a long `dotnet test`: a lone Esc byte is
  recognised after a short timeout because mobile terminals send it that way, the process tree is
  terminated, the tool result is recorded as cancelled by the user, and the live area shows
  `esc to interrupt` while a run is in progress (#621).
- **Repeated lines at narrow width.** The live area counted a wrapped line as one row, so every redraw
  left its overflow behind — a wizard walk at 40 columns printed the same notice 10–20 times (#617).
- **Skills notices** are setting-only and go to stderr: an over-limit description warns without
  disabling the skill, and an unknown context window means no threshold and the full index (#631).

### Changed

- **Tool processes no longer inherit provider API keys.** Every preset `KeyVariable` and its alias,
  plus each configured custom provider's `env:` reference, is stripped from a tool child's environment
  on all platforms — including Windows, where the spawn built its own environment. The user's own
  variables are untouched. An explicit pass-through list for named variables is #642 (0.4.0).

### Added

- **OpenCode Go / Jev.** The systemone family joins the opencode-go preset with `jev-1.13` pinned, and
  the wizard prints one line when it offers it: billed to the pay-as-you-go balance, not the Go plan
  (#618).

## [0.2.1] - 2026-11-02

Security: the single-file binaries bundle the .NET runtime, so a runtime advisory reaches users
through the installer, not through their own SDK. 0.2.0 was built with SDK 10.0.402 → runtime
10.0.13; this release builds with SDK 10.0.403 → runtime **10.0.14**, the out-of-band servicing
release for **CVE-2026-111497** (Brotli decoder). No Lunate code changes.

## [0.2.0] - 2026-11-02

The first stable release of the 0.2 line. Its contents are the two candidates' entries below —
[0.2.0-rc.1] and [0.2.0-rc.2] — which record every change, and the fixes that landed after rc.1 was
cut are in the rc.2 section. Highlights of the line:

- **Bash guard**: every command runs inside an OS sandbox (Seatbelt on macOS, bubblewrap/Landlock on
  Linux), with the guard's state in the header and a probe that proves it works.
- **Decision layer**: one optional profile, four consumers (approvals, routing, the decide tool,
  question suggestions), the same gate in the TUI, print mode and ACP.
- **Provider store and profiles**: providers and credentials live in user scope only; a project may
  reference them, never define one.
- **Session work**: fork/clone with images and redaction, an owner-only state directory, and a
  history that stays consistent under concurrent appends.
- **Extensions**: single-file extensions with `#:package`, remote (out-of-process) extensions, a
  self-contained template, and a published contract at `1.15.0`.
- **MCP**: stdio children keep the environment they had, minus the credential values Lunate itself
  knows unless the server's own `env` map names them.
- **Docs**: a configuration reference generated from the settings schema, and a gate that loads every
  tagged config example through the real parser.

## [0.2.0-rc.2] - 2026-11-02

The third candidate. The decision layer ships behind one settings block, and the external review's
security findings are closed: control bytes cannot reach the terminal from a model or a tool, session
state and recordings are owner-only, and a branched session no longer writes back what its source file
had redacted.

### Added

- The decision layer. One `decisions` block in `settings.json` names the profile that decides, then
  opts into any of four consumers: approvals (`off`, `shadow`, `enforce`, with `approvalsThreshold`
  deciding how certain a deny must be), routing (`off`, `shadow`, `enforce` over `routingProfiles`),
  the information-only decide tool, and display-only suggestions beside a question's top option. A
  decision can only tighten an approval, never grant one; without `profile` the layer stays off;
  with no user present an `enforce` deny stands; an escalation may override a remembered
  reject-always. The same gate runs in the TUI, print mode and ACP (#398, #399, #400, #402, #410,
  #444, #458, #443). Settings are JSON only, and `docs/configuration.md` is generated from the
  schema that parses them.

### Fixed

- Text bound for the terminal is scrubbed at every render boundary — live frame, committed markup,
  tool output, diffs, the trust prompt, the select list, the git branch footer and bracketed paste —
  so an OSC 52 clipboard write or an OSC 8 link in a model's or tool's output renders as inert text
  instead of executing in the terminal (#447).
- Session history and recordings are created owner-only (0600 files, 0700 directories), and a file
  left world-readable by an older build is narrowed the next time it is written (#448).
- A forked session file is redacted like its source and carries that session's image blobs, so a
  fork resumes instead of failing on the first request; a fork fails loudly when a blob is missing
  rather than producing a session that can never resume (#438).
- The four session history lists stay consistent when an append throws, so the next turn builds a
  request instead of crashing with an out-of-range index (#441).
- The bash guard denies a target that does not exist yet without creating it first, and reports
  Landlock as `unavailable` rather than claiming a sandbox it does not have (#437).
- A release candidate is created as a prerelease, and the pack dry run refuses `/`, the home
  directory, the repository or its ancestors instead of deleting them (#434).
- Stdio MCP children keep inheriting Lunate's environment so existing setups continue to work, but
  credential values Lunate knows (provider preset and provider-store `env:` values, auth-store keys,
  extension secrets and configured MCP `env` values) are removed unless the server's own `env` map
  declares that variable. A declared `${VAR}` still resolves from Lunate's environment (#449, #455).

## [0.2.0-rc.1] - 2026-11-01

The second release: the extension contract becomes a published surface, the bash guard lands on macOS
and Linux, and the tool gains a pack-only gate that proves every package version before a tag does.

### Added

- The bash guard: a bash command is wrapped in an OS-level deny that refuses writes under the state
  root (`LUNATE_HOME`) and the install prefix and read-denies `auth.json` — `sandbox-exec` on macOS,
  bubblewrap on Linux with `setpriv`'s Landlock rules as the fallback, Windows reported unsupported
  rather than run unconfined silently. The mechanism is probed once per session, so a restricted
  setup (Ubuntu 23.10+'s AppArmor user-namespace restriction) is detected and reported, never
  assumed away (#241).
- The interactive session checks GitHub Releases at most once a day, in the background, and names a
  newer release as one dim linked line in the welcome header (`v0.2.0 available · changelog`). The
  answer is cached under the state root for 24 hours; the request carries no telemetry beyond
  `User-Agent: lunate/<version>`. Opt out with `updateCheck: false` or `LUNATE_UPDATE_CHECK=0`;
  `--offline`, print mode, ACP and CI never check (#238).

### Fixed

- A follow-up queued in the instant a turn ended could be left behind: the turn's drain found the
  queue empty and the queueing continuation read the turn as still running, so neither started the
  entry — it sat in the queue until another turn ended, though the spec says a queued message runs
  when the turn completes. Taking the queue's head and releasing the turn now happen in one step
  under the gate the enqueue path holds too, so the queue and the turn can no longer both leave the
  entry to the other, and a handed-off turn can no longer clear the new turn's run handle.
- The `lunate` tool package shipped its nuget.org page with no licence and no rendered readme:
  `0.1.0` packed with "License missing" and "Readme missing" while the extension package carried
  both. The tool's project now declares the MIT expression, packs the repository README at the
  package root, and names its project and git repository, and
  `scripts/check-package-metadata.sh` — run by the verify gate on both shipped packages and by the
  pack job before the push — opens the packed `.nupkg` and fails when it loses one of the three.
- The slow-hook warning accused an extension of time the machine spent. A dispatch was timed with a
  wall clock around the handler, so a loaded thread pool — where a queued continuation waits for a
  worker — inflated the number: a hook whose own work is nothing reported 648–1392 ms past a 20 ms
  threshold. A crossing is now only called slow when the pool is not the reason it crossed: while
  more work waits for the pool than it has workers, the dispatch is counted but never feeds the
  maximum, the slow count, the warning or the warn-once latch, so a loaded machine cannot name an
  extension for the scheduler's delay and the first crossing on an idle machine still warns.
- A code-mode run that produced nothing at all was reported as "The script timed out", which hid
  the cause. A run that never reached the `hello` handshake now says so, naming the runtime, the
  elapsed time and the missing handshake, and every failure path (timeout, runtime that died, failed
  script) reports the child's exit code and stderr. `StructuredContent` carries `helloSeen` and
  `elapsedMs`, so a loaded runner's slow spawn is visible instead of masquerading as a slow script.
- Three `CodeModeExtension.Tests` cases failed together on a loaded Windows runner: the `node`
  child was started and stayed silent past the 30 s default deadline, so the invitation, the bridged
  ask and the pid never arrived. The tests that are not about the deadline now run with a ceiling
  (120 s) rather than the product default, the kill test yields once so its pid frame is flushed
  before the script blocks the event loop, and an empty pid or ask fails with the run's own report
  instead of a bare assertion.
- The release smoke waits for nuget.org to index the tag's extension package before installing it —
  polling the flatcontainer index, the package content and the registration resource under one
  ten-minute budget — instead of retrying the install five times into the lag. `v0.1.0`'s smoke went
  red on all three operating systems because the package had been pushed but not yet indexed; an
  exhausted budget now fails naming the version and the state of each feed resource, which tells a
  missing package apart from a slow feed.

## [0.1.0] - 2026-10-27

The first stable release. It is `0.1.0-rc.2` plus one fix found on a loaded runner: `ProcessRunner`
abandoned a child's stdout when a write to its stdin hit a closed pipe, so a child that exited while
an answer was in flight looked like one that never answered.

A release candidate is not `latest`, so both installers resolve this build when no version is given.
The `lunate` tool and `Lunate.Extensions.Subagents` publish from the same tag.

## [0.1.0-rc.2] - 2026-10-27

### Added

- The subagents extension ships beside the tool as `Lunate.Extensions.Subagents` — an officially
  supported extension, not a sample. `lunate install Lunate.Extensions.Subagents` installs it from
  NuGet, `lunate packages` lists it with its entry point and API version, `lunate packages --verify`
  recomputes the install record, and `lunate uninstall` removes it. The release smoke proves that
  path from the published feed on all three operating systems.

### Fixed

- A hyphenated tag is published as a prerelease and kept out of `latest`. `v0.1.0-rc.1` published
  as a full release, so `latest/download` — what both installers use when no version is given —
  served a candidate.
- `A_space_after_the_command_closes_the_list` read the input line and the autocomplete list between
  the two writes of one handler, and failed on a loaded runner. The test now waits for the list to
  follow the edit.

## [0.1.0-rc.1] - 2026-10-27

The first release candidate. It exists so the install channels and the recorded media can be
verified against a real, versioned build; the first stable release is `0.1.0`.

### Added

- A native terminal coding agent in one self-contained binary, with an interactive session,
  a live area that keeps the transcript while tools run, prompt editing in `$EDITOR`, and
  session resume and fork.
- C# depth without a language server: `cs_diagnostics`, `cs_find_symbol`, `cs_find_references`,
  `cs_outline` and `cs_rename` from Roslyn, offered only in a C# workspace.
- Extensions in C#: whole packages from NuGet, or a single `.cs` file that declares its tools and
  dependencies, is approved once, compiled and loaded into the running session. Tools, commands,
  hooks, TUI renderers and widgets, MCP servers, model providers and skills all come from the
  same public API, and `Lunate.Roslyn` is the flagship extension built only on it.
- Open protocols: an MCP client, an ACP server for editors, `AGENTS.md` project rules and
  `SKILL.md` skills.
- A trust boundary with a visible approval panel: every write and command asks, edits show their
  diff, a repository asks once, and calls that would extend their own trust (or write the state
  root) always ask. `--yolo` turns the panel off and says so.
- `--print` and `--json` for scripting a run, with a priced footer when the provider reports cost.

### Security

- The approval panel is the boundary: `--yolo` removes it, and a process running with the user's
  shell can write the state root. There is no sandbox in this release; 0.2 adds a bash guard
  profile for macOS and Linux.

[0.1.0]: https://github.com/wdaniel1993/lunate/compare/v0.1.0-rc.2...v0.1.0
[0.1.0-rc.2]: https://github.com/wdaniel1993/lunate/compare/v0.1.0-rc.1...v0.1.0-rc.2
[0.3.0]: https://github.com/wdaniel1993/lunate/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/wdaniel1993/lunate/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/wdaniel1993/lunate/compare/v0.1.0...v0.2.0
[0.2.0-rc.2]: https://github.com/wdaniel1993/lunate/compare/v0.2.0-rc.1...v0.2.0-rc.2
[0.2.0-rc.1]: https://github.com/wdaniel1993/lunate/compare/v0.1.0...v0.2.0-rc.1
[0.1.0-rc.1]: https://github.com/wdaniel1993/lunate/releases/tag/v0.1.0-rc.1
