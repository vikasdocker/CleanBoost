# Third-Party Notices

CleanBoost's own source code is MIT licensed (see [LICENSE](LICENSE)). This file
covers the third-party material that ships with, or is used to build, CleanBoost.

---

## Bundled data: `rules/winapp2.ini`

| | |
|---|---|
| **Component** | Winapp2.ini — community cleaning-recipe database |
| **Source** | <https://github.com/MoscaDotTo/Winapp2> |
| **License** | Creative Commons Attribution-ShareAlike 4.0 (CC-BY-SA-4.0) |
| **License text** | <https://github.com/MoscaDotTo/Winapp2/blob/master/License.md> |
| **Copyright** | The winapp2 project contributors |

`rules/winapp2.ini` is redistributed **unmodified** inside this repository and is
copied next to the executable at build time (`CleanBoost.App.csproj`). CleanBoost
does not alter it.

The upstream license requires that you may copy, modify, remix, share, show and
transmit Winapp2.ini, but you must:

1. **Redistribute it under the same license** (CC-BY-SA-4.0), and
2. **Attribute the original work** to the winapp2 project.

CleanBoost satisfies both by shipping the file unmodified alongside this notice.
The in-file header carries the same attribution for anyone who has received
`winapp2.ini` without the rest of this repository.

`WinappParser` consumes this file at runtime only when the user explicitly turns
on **Community rules** in the Cleaner. The rules are parsed into CleanBoost's own
`CleanCategory` model; no winapp2 text is embedded in the compiled binaries.

> Note: CleanBoost's own license does not relicense this file. If you redistribute
> CleanBoost, you must continue to include `rules/winapp2.ini` and this notice, and
> `winapp2.ini` remains CC-BY-SA-4.0 regardless of the MIT terms applied to the
> surrounding code.

---

## Runtime and build dependencies

These are not redistributed in CleanBoost's own installers; they are resolved from
NuGet or the Windows SDK at build time and carry their own licenses.

| Component | Role | License |
|---|---|---|
| [.NET 8 / .NET Runtime](https://github.com/dotnet/runtime) | Runtime and base class libraries | MIT |
| [Windows App SDK 1.8](https://github.com/microsoft/WindowsAppSDK) | WinUI 3 framework | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Referenced by `CleanBoost.App.csproj` | MIT |
| [xunit](https://github.com/xunit/xunit) | Test framework | Apache-2.0 |
| [WiX Toolset](https://github.com/wixtoolset/wix) | MSI authoring (build-time only) | MS-RL |
| [Windows SDK](https://learn.microsoft.com/windows/win32/winapi/) | `makeappx`, `signtool` (build-time only) | Microsoft EULA |

---

## Community cleaning data

The 22 hand-curated cleanup categories in `src/CleanBoost.Core/Catalog/BuiltInCatalog.cs`
are original work and fall under the MIT license. Some of them cover the same
well-known temporary-file and browser-cache locations that winapp2 also describes;
that is independent factual information about Windows, not derived from winapp2.

---

## Reporting an attribution problem

If you believe an attribution is missing or incorrect, please open an issue.
Attribution problems are treated as bugs, not feature requests.