# Recording: first run

This is the capture script for the onboarding clip in the media set. The clip is a manual
artifact: `docs/media/first-run.gif` does not exist until a maintainer records it with a real
provider key. Do not replace it with a replay fixture — the point of this clip is the real
first-run path, including the picker, the masked prompt and the first model answer.

## Before recording

- Use a Release build or the installed `lunate` binary. Do not record a Debug build.
- Use a dedicated, throwaway API key for a provider you are willing to spend one short turn on.
  Never use a personal key or show an account email, token count or cost.
- Start from empty state so the picker is genuinely the first-run offer:

  ```bash
  rm -rf /tmp/lunate-first-run-home /tmp/lunate-first-run-demo
  mkdir -p /tmp/lunate-first-run-home /tmp/lunate-first-run-demo
  cd /tmp/lunate-first-run-demo
  ```

- Record at 1400×700, font size 16, Dracula theme, padding 12 — the same frame as the feature
  tapes in `docs/media/tapes/`.
- Target length: 45–60 seconds. Keep the final GIF under 5 MB; the README media guard rejects
  anything larger.

## Commands, in order

Run each step in one terminal. Ellipses are timing, not commands.

1. Fresh state.

   ```bash
   export HOME=/tmp/lunate-first-run-home
   cd /tmp/lunate-first-run-demo
   ```

2. Show the installed build.

   ```bash
   lunate --version
   ```

   Hold the line long enough to read the version.

3. Start the session.

   ```bash
   lunate
   ```

   The welcome header appears and the first-run auth picker opens in the live area. The header
   must name `/auth` as the next step because nothing is configured.

4. Move through the picker.

   Press `j` once and `k` once so the movement is visible, then select
   `openai — OpenAI` (or the provider whose throwaway key you are using) and press `Enter`.

5. Type the key.

   Type the key at `API key for openai`. Only the asterisk mask may be visible; the key text must
   never appear in the frame, the scrollback or the terminal title.

6. Save.

   Press `Enter`. The notice must name the provider and point at `/model`; it must not contain the
   key. If the optional verification question appears, choose `Skip verification` so the clip
   spends no request there.

7. Choose the model.

   Type `/model`, press `Enter`, and pick `claude-sonnet-5-5` (or the model that belongs to the selected
   provider).

8. Send the first prompt.

   Type a short question that needs no tools, for example:

   ```text
   Reply with one sentence: what is a coding agent?
   ```

   Press `Enter` and let one complete answer render.

9. Leave.

   Type `/quit` and press `Enter`.

## What the clip must show

- The empty starting state and the first-run picker opening on its own.
- The picker lists the presets and the `Custom OpenAI-compatible endpoint…` row.
- Movement between rows and the selected row marker.
- The masked key prompt and only asterisks.
- The confirmation notice naming the provider and `/model`.
- The `/model` picker and the chosen model.
- One complete first answer.

## What the clip must not show

- The key value, in any frame or overlay.
- The contents of `~/.lunate/auth.json`, a session file or an export.
- A real account email, organisation id, cost line or token count.

After recording, export the GIF to `docs/media/first-run.gif` and check the file size. Add the
image to `README.md` and `docs/first-run.md` only in the change that records it, so neither page
points at a missing file before the capture exists.
