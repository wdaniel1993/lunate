# Tasks

## 1. The command surface

- [ ] One built-in descriptor `/editor` in the session group, with a one-line description
- [ ] One dispatch case that calls the existing editor flow and returns no message
- [ ] `/` completion and `/help` list it from the descriptor table, with no second list

## 2. The round-trip over a scripted child

- [ ] `/editor` writes the temp file, runs the scripted child and puts its text back in the prompt
- [ ] A non-zero exit leaves the draft alone, posts one notice with the exit code and deletes the file
- [ ] No run starts and no request reaches the model

## 3. Documents

- [ ] The guide's `Ctrl+G` row names `/editor` as the way in when the terminal does not deliver the key
- [ ] The editor notes state which draft the command acts on

## 4. Gate

- [ ] Targeted suites, `dotnet csharpier check .`, `openspec validate editor-command --strict`
- [ ] Full `scripts/verify.sh`
- [ ] Sync the specs and archive the change as the final commit of the PR
