# Third-party licences

Lunate itself is MIT (see [LICENSE](../LICENSE)). This page records the NuGet dependencies and their licences, kept current as dependencies change. Everything is permissive; the non-MIT entries are called out so they are never a surprise.

| Package | Version | Licence | Used by |
| --- | --- | --- | --- |
| Anthropic | 12.53.0 | MIT | Lunate.Ai, tests |
| Markdig | 1.4.0 | **BSD-2-Clause** | Lunate.Tui |
| Microsoft.Build.Framework | 17.11.48 | MIT | Lunate.Roslyn |
| Microsoft.Build.Locator | 1.11.2 | MIT | Lunate.Roslyn |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.9.0 | MIT | Lunate.Roslyn |
| Microsoft.CodeAnalysis.PublicApiAnalyzers | 5.6.0 | MIT | build tooling |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.9.0 | MIT | Lunate.Roslyn |
| Microsoft.Extensions.AI | 10.10.0 | MIT | Lunate.Ai, tests |
| Microsoft.Extensions.AI.Abstractions | 10.10.1 | MIT | Lunate.Agent, Lunate.Ai, Lunate.Extensibility.Abstractions, tests |
| Microsoft.Extensions.AI.OpenAI | 10.10.1 | MIT | Lunate.Ai, tests |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | MIT | Lunate.Ai |
| Microsoft.Reactive.Testing | 7.0.0 | MIT | tests |
| ModelContextProtocol | 2.2.0 | **Apache-2.0** | Lunate.Protocols |
| Spectre.Console | 0.57.2 | MIT | Lunate.Tui |
| Spectre.Console.Testing | 0.57.2 | MIT | tests |
| System.Reactive | 7.0.0 | MIT | Lunate.Tui |
| xunit.v3 | 4.0.1 | **Apache-2.0** | tests |

Notes:

- The transitive closure of these packages may include further Microsoft (MIT) and System (MIT) assemblies; this table records direct references.
- Spikes under `docs/spikes/` pin their own packages for evaluation only; their licences are recorded in the spike reports.
