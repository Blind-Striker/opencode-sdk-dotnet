# TFM matrix: netstandard2.0 + net472 + net8/9/10

Date: 2026-08-08

The packages target `netstandard2.0;net472;net8.0;net9.0;net10.0`, with `net11.0` light-up
planned post-GA (2026-11-10). net472 exists for Framework-exact compile paths (`#if NET472`:
ServicePointManager connection limits, process tree-kill); netstandard2.0 rides the same
downlevel tax already paid for net472 and reaches consumers who otherwise could not install
the package at all (net5–net7 stragglers, Unity, Mono). ns2.0 has no runtime of its own — the
net472 CI leg is its proxy coverage. The downlevel tax is paid once via Polyfill (source-only,
internal; BCL API polyfills on top of the compiler-support attributes — chosen over PolySharp,
which covers only the latter). Microsoft's own BCL packages ship the same exact+bridge TFM
pattern. Evidence: research log Q16/Q17.
