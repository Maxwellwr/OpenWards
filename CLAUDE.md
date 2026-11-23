# OpenWards

C# / .NET 10 reimplementation of Theme Hospital. It started from CorsixTH
(reference checkout: `C:\repos\github\CorsixTH`) but is not a port of it. Read
`ARCHITECTURE.md` first.

## Rules

- `OpenWards.Sim` (and any project with `OpenWardsDeterministic=true`) must stay
  deterministic. The analyzer (`OW0001`–`OW0003`) enforces part of this. Do not
  suppress its diagnostics. Restructure the code instead.
- The simulation must not reference the client or MonoGame.
- CorsixTH is the base and the reference for drawing (sprites, animations, map and
  interface rendering). It is not the authority on game logic: do not follow it blindly.
- Game mechanics and balance aim at the original Theme Hospital, as public descriptions
  (manual, guides, fan sites, players' observations) tell it. Where CorsixTH differs from
  the original, or the maintainer wants different behaviour, the maintainer decides. Any
  logic may be changed when the maintainer asks; say in the PR where it departs from
  CorsixTH.
- When code is taken from CorsixTH, note the source file. CorsixTH is MIT-licensed.
- Never use disassembly of the original game binaries.
- Format readers get unit tests with synthetic data. Add real-data tests too, via
  `RealGameData.Require()`, which skips when the game is not installed.
- Package versions live in `Directory.Packages.props`. Shared build settings live in
  `Directory.Build.props`. Warnings are errors.

## Commands

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```
