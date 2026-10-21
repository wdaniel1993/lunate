# Tasks — tool-blocks-call-only

The implementation landed in PR #107 on `change/tool-blocks-call-only`; this change records it in
the specs. No `src/` or `tests/` file changes.

## 1. Spec deltas

- [x] 1.1 Update the `tui` "Tool block rendering" requirement: header-only default, `ctrl+o` hint
  only when expanded details exist, expanded mode keeps the excerpt and diff. Evidence: PR #107;
  `ToolBlockRendererTests` (`Collapsed_details_*`, `Expanded_details_show_the_output_that_collapsed_hides`),
  the collapsed tool-block goldens and `InteractiveSessionToolBlockTests`.
- [x] 1.2 Update the `tui` "Diff rendering with match tier" requirement: collapsed hides the diff
  behind the hint, expanded renders the red/green panel and the tier label. Evidence: PR #107;
  collapsed edit/write goldens and `edit-diff-*-80-no-color-expanded.txt`.
- [x] 1.3 Update the `agent-config` "Settings file and precedence" requirement with `toolDetails`
  (`collapsed` default, exact lowercase literals) and `LUNATE_TOOL_DETAILS`, including the
  collected invalid-value error. Evidence: PR #107; `SettingsStoreTests`
  (`The_default_tool_details_are_collapsed`, `A_file_can_expand_the_tool_details`,
  `An_invalid_tool_details_in_the_file_is_reported`, the environment-override tests).

## 2. Documentation

- [x] 2.1 Confirm `docs/guide.md` already describes the collapsed block, the hint and the setting
  (PR #107, commit `7582b41`).

## 3. Gate

- [x] 3.1 Run `openspec validate tool-blocks-call-only --strict` and `markdownlint-cli2` over the
  new files; both clean.
