// The suite runs a real extension host per test (real loader, real session files). Serializing the
// suite keeps the file-system and event-ordering assertions deterministic on small CI runners.
