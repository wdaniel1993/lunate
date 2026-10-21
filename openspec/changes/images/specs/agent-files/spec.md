## MODIFIED Requirements

### Requirement: read tool

`read` SHALL take `path`, `offset` (1-based, default 1) and `limit` (default 2,000, clamped to 2,000), and return the requested window as numbered lines in the exact format `{n,6}|{text}` (number right-aligned to `max(6, digits(total))`, culture-invariant) with the footer `[lines {first}–{last} of {total}, use offset to continue]` when more lines remain. It SHALL refuse: paths outside the workspace; missing files; directories (pointing at `bash ls`); binary files (a NUL byte — the first 8,192 bytes of a local read, or anywhere in the text a client-backed read received — with the size named); `offset < 1`, `limit < 1`, and `offset` past the end (naming the total). Display normalizes line endings (one trailing `\r` stripped), strips a UTF-8 BOM and replaces invalid bytes with U+FFFD; the file itself is never modified. An empty file returns `[empty file]`.

Before the binary check, `read` SHALL sniff the file's magic bytes for PNG, JPEG, GIF or WebP. For a supported image it SHALL require `ToolContext.Model` to declare `SupportsVision`; without it the result SHALL be an error naming the model id and saying the model does not support images. With vision it SHALL parse the width and height from the image header, enforce the 5 MB and 4096-pixels-per-side limits, and return exactly `[image <width>x<height>]` as `Output` with the bytes in `Images`. An image read SHALL reject `offset` or `limit` arguments, naming the parameter and saying images have no lines. An oversize image SHALL say `downscaling is not available yet`. A file that is neither text nor a supported image SHALL keep the existing binary error.

#### Scenario: Numbered lines and the continuation footer

- **GIVEN** a text file with more lines than the limit
- **WHEN** it is read with default parameters
- **THEN** the output contains numbered lines and the footer names the window and total

#### Scenario: Paging with offset and limit

- **GIVEN** a file of many lines
- **WHEN** it is read with an offset and limit
- **THEN** exactly that window is returned and the footer reflects it

#### Scenario: Binary, directory and missing files fail instructively

- **GIVEN** a binary file, a directory and a missing path
- **WHEN** each is read
- **THEN** each returns an error result whose text says what the file is (size for binary) and what to do instead

#### Scenario: Offset past the end is an error

- **GIVEN** a file with fewer lines than the requested offset
- **WHEN** it is read
- **THEN** the error names the offset and the total line count

#### Scenario: A supported image returns the placeholder and the bytes

- **GIVEN** a 1024×768 PNG and a model with vision
- **WHEN** it is read
- **THEN** `Output` is `[image 1024x768]`, `Images` carries the PNG bytes as `image/png`, and `IsError` is false

#### Scenario: A text-only model gets a clear refusal

- **GIVEN** a PNG and a model without vision
- **WHEN** it is read
- **THEN** the result is an error naming the model id and saying the model does not support images

#### Scenario: An oversize image says downscaling is unavailable

- **GIVEN** a 6 MB PNG and a model with vision
- **WHEN** it is read
- **THEN** the error names the size, the 5 MB limit and `downscaling is not available yet`

#### Scenario: Offset and limit do not apply to images

- **GIVEN** a PNG and a read call with `offset`
- **WHEN** it is read
- **THEN** the error names `offset` and says images have no lines
