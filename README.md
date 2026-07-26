# crap4csharp

## Attribution

`crap4csharp` continues the lineage of Robert C. ("Uncle Bob") Martin's original **crap4clj**, and is
a C# port of its Java sibling **crap4java**.

---

`crap4csharp` is a standalone CRAP metric tool for C# projects, modeled after `crap4java`.

It combines method cyclomatic complexity with Coverlet (Cobertura) method coverage and reports CRAP
scores. On each run it deletes stale coverage artifacts, runs coverage, then analyzes the selected
files.

## Formula

`CRAP = CC^2 * (1 - coverage)^3 + CC`

- `CC` is cyclomatic complexity.
- `coverage` is method coverage fraction from Cobertura line counters (the .NET analog of JaCoCo
  `INSTRUCTION` counters).

## Coverage Pipeline

For each invocation, per module (nearest `.sln`, else `.csproj`, else the project root):

1. Delete stale coverage artifacts:
   - `coverage/`
2. Run `dotnet test --collect:"XPlat Code Coverage" --results-directory coverage`
3. Read the produced `coverage.cobertura.xml`
4. Analyze the selected C# files

## Build and Test

```bash
dotnet test
```

## Run

Build:

```bash
dotnet build -c Release
```

From the project root you want to analyze:

```bash
dotnet run --project src/Crap4CSharp -c Release
```

## CLI

```text
--help                Print usage to stdout
(no args)             Analyze all C# files under src/
--changed             Analyze changed C# files under src/
<file ...>            Analyze only these files
<directory ...>       Analyze all C# files under each directory's src/ subtree
```

Examples:

```bash
dotnet run --project src/Crap4CSharp -c Release -- --help
dotnet run --project src/Crap4CSharp -c Release
dotnet run --project src/Crap4CSharp -c Release -- --changed
dotnet run --project src/Crap4CSharp -c Release -- src/Sample.cs
dotnet run --project src/Crap4CSharp -c Release -- project-a project-b
```

## Exit codes

- `0` success, threshold respected
- `1` invalid CLI usage, or a fatal error (e.g. no tests ran / no coverage produced — see Notes)
- `2` CRAP threshold exceeded (`> 8.0`)

## Notes

- **Fail fast:** if a module runs no tests or produces no coverage, `crap4csharp` exits non-zero
  rather than continuing — a deliberate, stricter departure from `crap4java`. A method simply absent
  from an otherwise-populated report is still reported as `N/A`.
- Report output is sorted by CRAP descending, with `N/A` at the bottom.
