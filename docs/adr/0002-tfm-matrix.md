# TFM matrix follows Microsoft's support lifecycle, plus net472 and netstandard2.0

Date: 2026-09-29

The packages target every modern .NET version Microsoft supports, LTS and STS alike, plus `net472`
and `netstandard2.0`. A version leaves the matrix when its support ends and a new one joins at its
GA. For 2026 that means `net8.0` and `net9.0` leave when their support ends on 2026-11-10 and
`net11.0` joins at its GA, giving `netstandard2.0;net472;net10.0;net11.0`. net472 exists for
Framework-exact compile paths (`#if NET472`: ServicePointManager connection limits, process
tree-kill). netstandard2.0 rides the same downlevel tax already paid for net472 and is a
compatibility asset, not a supported runtime of its own: consumers on other runtimes (Unity, Mono,
.NET versions past their support) can install it, but support and testing cover the targeted
runtimes only. The net472 CI leg is its proxy coverage. The downlevel tax is paid once via Polyfill
(source-only, internal; BCL API polyfills on top of the compiler-support attributes — chosen over
PolySharp, which covers only the latter). Microsoft's own BCL packages ship the same exact+bridge
TFM pattern. Evidence: internal research, 2026-08-08, "Does adding net472 force a netstandard2.0
target?" and "PolySharp or SimonCropp/Polyfill for the downlevel TFMs?"; for the lifecycle policy,
internal research, 2026-09-29, "What stands between the preview and 1.0, and in what order?".

## Considered Options

- **Promise .NET 5–7 through the netstandard2.0 asset.** Rejected: those runtimes are past their
  support, the `System.Text.Json` and `Microsoft.Extensions` 10.x dependencies do not support them,
  and no leg tests them, so the promise would claim more than the package delivers.
- **Keep an ended version as a target until 1.0.** Rejected: a target past its support keeps a CI
  leg and a public-API check for a runtime nobody should deploy, and the lifecycle rule makes each
  change predictable instead of a yearly decision.
