# Code quality and metrics

How BuildNexus measures code quality automatically, what each number means, and how to read it. Part of the SE3112 testing pipeline (see "Test automation pipeline" in the root [README](../README.md)).

## Tools

| Tool | What it measures | Where it runs |
|---|---|---|
| **.NET analyzers** (`CAxxxx` rules, built into the SDK) | Bugs, performance and design problems in the C# source, as build warnings | Every `dotnet build`, locally and in CI |
| **oxlint** | Problems in the React/TypeScript source | `npm run lint` in `frontend/`, and the `Lint` step in CI |
| **Coverlet** | Which lines and branches the tests execute | The unit and integration test jobs |
| **ReportGenerator** | Turns the coverage files into one HTML report and a per-service table | The unit and integration jobs, and the Test report job |
| **SonarCloud** | Bugs, vulnerabilities, code smells, technical debt, duplication, coverage, and a pass/fail quality gate | The `static-analysis` job |

## What was added for this

| File | Purpose |
|---|---|
| `Directory.Build.props` | Switches the stricter analyzer level (`AnalysisMode=Recommended`) on for every production project. Test projects stay at the default level. |
| `.editorconfig` | Shared editor settings, and the two analyzer rules deliberately kept quiet (see below) |
| `SonarQube.Analysis.xml` | SonarCloud settings: organization, project key, coverage report paths, exclusions, wait for the quality gate |
| `.github/workflows/ci.yml` | `static-analysis` job, both coverage formats, and the per-service coverage table in the Test report |

The analyzers produce **warnings, never errors**, so the build stays green. They are a list to work through, not a gate.

## The metrics

| Metric | Meaning | Where to see it |
|---|---|---|
| **Coverage** | Share of lines (and branches) the tests run | Test report (per service), SonarCloud, the HTML coverage artifacts |
| **Bugs** | Code that is probably wrong | SonarCloud, Issues |
| **Vulnerabilities** and **Security Hotspots** | Code that may be exploitable, and code that needs a human to judge | SonarCloud |
| **Code smells** | Code that works but is hard to maintain | SonarCloud, and the analyzer warnings in the build |
| **Technical debt** | SonarCloud's estimate of the time needed to fix all the smells | SonarCloud, Overview |
| **Duplication** | Share of lines that are copies of other lines | SonarCloud, Measures |
| **Ratings A to E** | Reliability, Security and Maintainability grades. A is best. | SonarCloud, Overview |

## The quality gate

SonarCloud's default gate ("Sonar way") judges **new code**, meaning code changed since the last baseline, so old code does not block you:

- coverage on new code is at least 80%
- duplicated lines on new code are at most 3%
- no new bugs, vulnerabilities or security hotspots left unreviewed (ratings of A)

The `static-analysis` job waits for the gate verdict and **fails if the gate fails**. In the Test report summary the row shows PASS or FAIL.

## Reading the SonarCloud dashboard

1. **Overview**: the gate verdict at the top, then Reliability, Security, Maintainability, Coverage and Duplication for new code and overall.
2. **Issues**: every finding with its severity and the line, and a "Why is this an issue?" explanation. Filter by type or file.
3. **Measures**: every metric per file and per folder. Use it to find the services with the lowest coverage or the most duplication.
4. **Code**: browse a file with coverage shown line by line (green is covered, red is not).
5. **Activity**: the trend over time, which shows whether quality is improving.

## One-time SonarCloud setup

Done once, by one person with admin access to the GitHub organization:

1. Sign in at sonarcloud.io with GitHub, create or choose the organization, and import this repository.
2. Check that the organization key and project key shown on SonarCloud match `sonar.organization` and `sonar.projectKey` in `SonarQube.Analysis.xml`. Edit the file if they differ.
3. Under Administration, Analysis Method, turn **Automatic Analysis off**. CI analysis and automatic analysis cannot run together.
4. Create a token (My Account, Security) and save it in the GitHub repository as the secret `SONAR_TOKEN` (Settings, Secrets and variables, Actions). **Never put the token in a file.**

Until the secret exists, the job skips itself with a visible warning and the pipeline stays green.

## Before and after: analyzer warnings

Counted from a clean build of every project, duplicates removed.

| Rule | Meaning | Before | After |
|---|---|---|---|
| CA1305 | Number conversions that depend on the machine's locale (`Convert.ToInt32(x)`) | 41 | 0 |
| CA1859 | A private helper declared to return `IActionResult` where it always returns one concrete type | 19 | 2 |
| CA1822 | A method that uses no instance data and could be `static` | 10 | 0 |
| xUnit2031 | `Assert.Single(items.Where(...))` instead of the overload that takes the filter | 2 | 0 |
| CA1716 | A parameter named `error` clashes with a keyword in other .NET languages | 4 | 4 |
| CA1000 | A static member on a generic class | 4 | 4 |
| **Total** | | **80** | **10** |

What was fixed, and why it matters:

- **CA1305**: `Convert.ToInt32(value)` on a database value is correct on most machines, but the rule exists because the same call can behave differently under another locale. Passing `CultureInfo.InvariantCulture` makes the result the same everywhere.
- **CA1859**: the helper methods that build error responses now declare the exact result type they return, so the compiler can call them directly instead of treating the result as a generic interface.
- **CA1822**: the `DelayAsync` and `Refused` helpers do not touch the object's fields, so they are now `static`. That is both clearer and slightly faster.

Left alone on purpose:

- **CA1716 and CA1000** (8): fixing them means renaming a parameter on a public interface, or reshaping a generic type, in all five services at once. That is a design change, not a cleanup.
- **CA1859 on two fields**: the `Allowed` and `CancellableFrom` fields in `ProjectStatusTransitions` are typed as read-only on purpose, so nothing can change the status rules. A concrete type would give that up.
- **CA1848 and CA1873** (logging performance, about 230 calls): downgraded to suggestions in `.editorconfig`. They ask for every log call to become a source-generated method, which is a speed refactor and not a defect.

## SonarCloud findings

Filled in after the first analysis, from the Overview and Measures pages.

| Measure | First analysis | After fixes |
|---|---|---|
| Quality gate | | |
| Bugs | | |
| Vulnerabilities | | |
| Code smells | | |
| Technical debt | | |
| Duplication | | |
| Coverage (overall) | | |
