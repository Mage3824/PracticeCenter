# solutions.md — проблемы и решения во подготавли environment (Stage 0)

WorkTime.sln, 4 проектов: `WorkTime.Domain`, `WorkTime.Application`, `WorkTime.Infrastructure`, `WorkTime.Tests` (xUnit).
Все проблем ниже были решени manually до writing code Stage 1.

---

## 1. No access to NuGet (no network)

**Problem.** `dotnet add package xunit` / `dotnet build` tries to resolve packages online.
There is no network access in the environment, so NuGet returns an error:

```
Downloading packages failed.
The package could not be found in the NuGet repository (offline mode).
```

**Solution.** Packages were installed offline, without NuGet resolution:

- `<PackageReference>` entries were written into `WorkTime.Tests.csproj` manually
  (`xunit 2.5.3`, `xunit.runner.visualstudio 2.5.3`, `Microsoft.NET.Test.Sdk 17.8.0`, `coverlet.collector 6.0.0`);
- the required `.dll` files were taken from the local NuGet cache
  (`%USERPROFILE%\.nuget\packages\...`) and placed into the build output;
- `dotnet build` after that works fully offline, because all dependencies are already resolved
  and stored in `obj/project.assets.json` + `obj/*.nuget.cache`.

**Rule for the future:** do not run `dotnet add package` / `dotnet restore` with network resolution.
If a new dependency is needed, add it manually to `.csproj` and take it from the local cache.

---

## 2. `UnauthorizedAccessException` when accessing `AppData` and `.dotnet` folders

**Problem.** Any `dotnet` command tries to write into:

- `%USERPROFILE%\AppData\Local\Temp\...` (temporary build files);
- `%USERPROFILE%\.dotnet\` (SDK / toolchain caches).

In the sandboxed shell these paths are not writable:

```
Unhandled exception. System.IOError: [WinError 5] Access is denied:
    'C:\Users\bavk2\AppData\Local\Temp\...'
```

**Solution.**

1. The command was run in a terminal started **as Administrator** (the user runs it themselves);
2. inside the sandbox only paths under the workspace `E:\AItest\progcentr\project\backend` are writable,
   so all build artifacts (`bin`, `obj`) are written there and are not a problem;
3. no command was used that writes to `AppData` / `.dotnet` — `dotnet build` was run with
   already-existing caches, so no cache writing was needed.

---

## 3. Project reference syntax issue (`<Import Project>` vs `<ProjectReference Include>`)

**Problem.** To link projects in the solution, the tag `<Import Project="..." />` was used:

```xml
<!-- WRONG -->
<Import Project="..\WorkTime.Domain\WorkTime.Domain.csproj" />
```

The build fails: `<Import Project>` is not a project dependency — it is an element
for importing a shared build file (`.mk` / `.props`), and the compiler does not see
types from other projects (`WorkTime.Domain.User` — "identifier not found").

**Solution.** The correct tag for a project dependency is `<ProjectReference Include>`:

```xml
<ItemGroup>
  <ProjectReference Include="..\WorkTime.Domain\WorkTime.Domain.csproj" />
</ItemGroup>
```

In `WorkTime.Tests.csproj` the path is relative to the `tests\` folder, so it contains two levels:

```xml
<ProjectReference Include="..\..\src\WorkTime.Domain\WorkTime.Domain.csproj" />
```

After this fix the dependency chain works:
`Domain` ← `Application` ← `Infrastructure`, and `Tests` depends on all three.

---

## 4. `Access denied` when running `testhost`

**Problem.** `dotnet test` starts a separate process `testhost` and writes IPC sockets
and temporary files into `AppData\Local\Temp`. In the sandbox:

```
Unhandled exception. [WinError 5] Access is denied
    ... testhost ...
```

**Solution.** Tests must be run by the user in a terminal started **as Administrator**:

```
dotnet test
```

This command is **not** run inside the sandbox — it causes access denied and does not
mean that the code is wrong. Compilation is checked only with `dotnet build`.

---

## Итого

| Problem | Solution |
| --- | --- |
| No NuGet | packages installed offline from local cache, `.csproj` edited manually |
| `UnauthorizedAccessException` (`AppData`, `.dotnet`) | run the terminal as Administrator; sandbox writes only to the workspace |
| `<Import Project>` instead of `<ProjectReference Include>` | replaced with `<ProjectReference Include>` |
| `Access denied` when running `testhost` | `dotnet test` run by the user in an Administrator terminal |
