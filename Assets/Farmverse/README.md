# Assets/Farmverse

The Unity side of Farmverse. Architecture: `Docs/architecture` (start with 00
and 02). Every folder below is one assembly or one kind of content.

```
Runtime/
  Contracts/        generated from contracts/ (never edit; `npm run contracts:gen`)
  Core/             results, reactive state, clock, paging, logging   (engine-free)
  Domain/           game rules, mirrored by the server, proven by vectors (engine-free)
  Application/      stores, action queue, session, services, ports      (engine-free)
  Infrastructure/   Firebase/ and Unity/ adapters for the ports
  Presentation/
    Core/           presenters, navigation, localization, world model    (engine-free)
    UI/             UI Toolkit shell, components and views
    World/          3D farm: WorldRoot, camera, input, pooling
  Bootstrap/        GameRoot composition root, EnvironmentConfig
Editor/             Farmverse ▸ Set Up Boot Scene, art import settings, package installer
Tests/EditMode/     NUnit tests (also run headless: tools/dotnet/Farmverse.TestHost)
Content/
  Config/           Firebase app configs per platform
  Localization/     en.json (checked by tools/codegen/check_strings.py)
  UI/               FarmverseTheme.tss, Styles/ (tokens, components, generated_art),
                    Fonts/ (Nunito, Lilita One + OFL licences),
                    Art/Painted (OpenAI-generated icons, illustrations, backgrounds and brand art; imported as sprites by Editor/FarmverseArtImporter.cs)
Scenes/             Boot.unity (built by the setup menu)
```

Rules: engine-free assemblies never reference UnityEngine; views hold no game
logic; colours and sizes come from `tokens.uss`; generated files are never
edited by hand.
