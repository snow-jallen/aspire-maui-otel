# PublicOtel.Templates

A `dotnet new` template pack for an Aspire solution with a .NET MAUI client wired for
OpenTelemetry.

## What the template generates

| Project | Purpose |
|---|---|
| `<Name>.AppHost` | Aspire AppHost. Wires the API, web frontend, and MAUI Windows/Android targets, plus dev tunnels for mobile. |
| `<Name>.ApiService` | Minimal API serving `/weatherforecast`. |
| `<Name>.Web` | Blazor frontend. |
| `<Name>.Mobile` | .NET MAUI app. One **Get Weather** button that calls the API and renders the results. |
| `<Name>.ServiceDefaults` | Shared Aspire defaults for the ASP.NET Core projects. |
| `<Name>.MauiServiceDefaults` | Aspire defaults adapted for MAUI — including the environment-variable configuration source that `MauiApp.CreateBuilder()` does not add on its own. |
| `<Name>.ClientLogic` | UI-agnostic client logic: the API client, the telemetry instruments, and a `WeatherViewModel` built on **CommunityToolkit.Mvvm**. Referenced by `<Name>.Mobile`. |
| `<Name>.ClientTests` | Unit and BDD tests for `ClientLogic`, using **NSubstitute**, **Shouldly**, and **Reqnroll** (xUnit v3). |
| `<Name>.Tests` | xUnit integration tests against the AppHost. |

The page heading shows the name the app was generated with, so `dotnet new publicotel-maui -n Demo5` produces an app titled **Demo5**.

Pressing the button produces a single distributed trace spanning the mobile app and the API
service, structured logs, and a custom meter, all visible in the Aspire dashboard.

## Running it

```bash
aspire start
```

Then in the dashboard, start the platform you want.

**Windows** works directly: start the `mobile-windows` resource, press **Get Weather**, and
the trace appears under Traces.

**Android**: on the `mobile-android-emulator` resource, use its **▶ Run on Android**
command — *not* Start. Start always fails with NETSDK1085 because of a bug in
`Aspire.Hosting.Maui` 13.5.3-preview.1 (see below). The command does the whole thing in one
step and is the only action you need.

Equivalently, from a terminal at the repo root:

```powershell
./scripts/run-android.ps1
```

You will see the NETSDK1085 failure scroll past partway through. **That is expected** — the
script triggers that build deliberately to produce the environment file, then launches the
app itself. Success looks like `Running on Android emulator.` at the end.

### If Android telemetry does not appear

Android reaches the dashboard through **dev tunnels**, which need a Microsoft login the
first time. Two things bite:

**1. There is a hard cap of 10 tunnels per account.** Every app generated from this template
creates **two** (`mobile` and `mobile-api`), with IDs derived from the folder path — so a new
folder means two more tunnels. Five apps fills the account. Over the cap, the service
reports it confusingly as a rate limit:

```
Rate limit exceeded. Please wait about a minute and try again.
```

Check and clean up with the `devtunnel` CLI:

```bash
devtunnel list                 # if this says "Found 10 tunnels", you are at the cap
devtunnel delete <tunnel-id>   # tunnels are recreated automatically on the next run
```

**2. Genuine rate limiting** on rapid restarts, which does clear after about a minute.

In both cases the tunnel resources show `FailedToStart`, and `run-android.ps1` stops with an
explanation rather than launching an app that silently reports nothing.

The **Windows** target uses no tunnels at all — no login, no cap, no rate limit. **In a
classroom, demo Windows first and treat Android as the stretch goal.** If every student
generates their own app, they will each need their own dev tunnel login, and anyone reusing
a shared account will hit the 10-tunnel cap quickly.

## The Android bug, in one paragraph

The MAUI integration pre-builds the project, then asks DCP to launch it with
`-p:NoBuild=true`. On Windows that is harmless, because `/t:Run` resolves to the SDK's `Run`
target, which never reaches `Build`. On Android, `Run` depends on `Install`, which pulls in
the full build chain, so `Build` **is** invoked and the SDK's `_CheckForBuildWithNoBuild`
guard hard-errors. The flag arrives as an MSBuild global property and the guard is defined
after any user `Directory.Build.targets`, so it cannot be overridden from the repo. The
workaround simply runs the same command without that one flag. A physical device
(`AddAndroidDevice`) fails identically — the error is in the MSBuild target chain, before
adb target selection matters. Remove `scripts/run-android.ps1` and the `WithCommand` block
in `AppHost.cs` once the integration is fixed upstream.

## Install

```bash
dotnet pack PublicOtel.Templates/PublicOtel.Templates.csproj -c Release
dotnet new install PublicOtel.Templates/bin/Release/PublicOtel.Templates.1.8.0.nupkg
```

Once installed the template appears in `dotnet new list` and in the Visual Studio 2022
(17.9+) New Project dialog.

## Use

```bash
dotnet new publicotel-maui -n Contoso.Telemetry --application-id-prefix com.contoso
```

| Option | Default | Effect |
|---|---|---|
| `-n, --name` | `AspireMauiApp` | Renames every project, namespace, and the solution. Also drives the lower-cased MAUI `ApplicationId`, the OpenTelemetry meter and instrument names, and the dashboard's `*.dev.localhost` host. |
| `--application-id-prefix` | `com.companyname` | Reverse-DNS prefix for the MAUI `ApplicationId`. |

Project GUIDs, the solution GUID, the AppHost `UserSecretsId`, and all ten launch-profile
ports are regenerated per instantiation, so two apps from this template can be open and
running side by side.

## Uninstall

```bash
dotnet new uninstall PublicOtel.Templates
```

## Maintaining the template

The template content under `templates/publicotel-aspire-maui/` is a copy of the source
solution. When you change the real solution and want those changes in the template, copy the
files across and then re-apply the two placeholders that deliberately differ from working
code:

- `Projects.GeneratedClassNamePrefix_*` in `AppHost.cs` and `WebTests.cs` — the Aspire source
  generator replaces dots with underscores, so `sourceName` substitution alone cannot produce
  a valid identifier.
- The placeholder ports in the three `launchSettings.json` files (`15000`, `17000`, `19000`,
  `20000`, `21000`, `22000`, `5000`, `7000`, `5301`, `7301`), which the port symbols match on.
- `<ApplicationId>com.companyname.APPIDNAME.mobile</ApplicationId>` in the Mobile csproj — the working copy holds a literal name; the template holds the token.
- `tunnelId: "mobile-api-TUNNELSUFFIX"` in `AppHost.cs` — the working copy holds a real
  number; the template holds the token, which the `tunnelSuffix` symbol replaces with a
  random six-digit value per generated app.

## Dev tunnel IDs

Each generated app pins its API tunnel to `mobile-api-<random six digits>`, chosen once at
generation time. Left unpinned, Aspire derives the ID from a hash of the AppHost folder, so
moving or renaming the folder silently orphans the old tunnel — and orphans still count
against the cap for 20–30 days.

Two caveats:

- **The OTLP tunnel cannot be pinned.** `WithOtlpDevTunnel()` takes no `tunnelId`
  parameter, so that one is still `mobile-<folder hash>`.
- **Pinning does not raise the 10-tunnel cap.** Each app still needs two tunnels. Generating
  a *new* app still draws a new random suffix and therefore a new tunnel.

## Android ApplicationId and Java keywords

The `ApplicationId` is built as `<prefix>.<project name, lower-cased, separators removed>.mobile`
— note **separators removed**, not just lower-cased. Each dot starts a new Java package
segment, and the Android build generates an `R.java` containing `package <ApplicationId>;`.
A segment that happens to be a Java reserved word will not compile, and the error is
unhelpful:

```
R.java:8: error: <identifier> expected
package com.companyname.final.check.mobile;
                        ^
```

Joining the name into one segment avoids this for multi-word names — `Final.Check` becomes
`com.companyname.finalcheck.mobile`. A single-word project named exactly after a Java
keyword (`Final`, `New`, `Class`, `Public`, `Static`) would still collide; set
`--application-id-prefix` or rename the project if you hit it.
