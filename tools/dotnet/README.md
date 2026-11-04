# Engine-free C# build

Compiles the Unity assemblies that have `noEngineReferences: true`
(Contracts, Core, Domain, Application) with plain
.NET — C# 9, .NET Standard 2.1, warnings as errors, like Unity 6 — and runs their
EditMode tests without the editor.

```bash
dotnet build tools/dotnet/Farmverse.TestHost -c Release
dotnet tools/dotnet/Farmverse.TestHost/bin/Release/net8.0/Farmverse.TestHost.dll [filter]
```

`NUnitShim.cs` implements the subset of NUnit 3 the tests use so this needs no
NuGet access; Unity compiles the same test files against the real NUnit.
