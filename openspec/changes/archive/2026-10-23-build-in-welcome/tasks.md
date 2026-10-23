## 1. Version form

- [x] 1.1 `ProductVersion.BuildForm` maps the informational version to the short-build form and leaves a hashless build alone.
- [x] 1.2 `ProductVersion.Build` is the header's form; `Display` and `Informational` are unchanged.

## 2. Header

- [x] 2.1 `HeaderState` passes `ProductVersion.Build`; the renderer is untouched.
- [x] 2.2 The ASCII single-line form carries the same version token.

## 3. Tests

- [x] 3.1 Table-driven tests pin the mapping (full hash, short hash, no hash, short metadata).
- [x] 3.2 The session and budget tests assert against `ProductVersion.Build` rather than a literal.
- [x] 3.3 The end-to-end golden normalises the build token.

## 4. Docs

- [x] 4.1 `docs/guide.md` names the header's version form where it describes the header.
