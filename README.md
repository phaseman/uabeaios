# Unity Asset Editor for iOS

This is a touch-friendly Unity bundle editor intended for sideloading through LiveContainer and SideStore. The current milestone targets PvZ Heroes `recipe_decks` files.

## Current capabilities

- Open a file manually with the iOS document picker.
- Read UnityFS bundles using Unity 2022 type trees.
- List and search all deck `MonoBehaviour` assets.
- Edit deck names, factions, card IDs, GUID/name values, copy counts, filters, and superpower overrides.
- Add or remove card and override entries.
- Rebuild the serialized asset and its containing Unity bundle.
- Create a timestamped backup beside the selected file when a local path is available. For other file providers, the backup is stored in the editor's Files-visible `Documents/UnityAssetEditor/Backups` folder.

The supplied `recipe_decks_1 2` fixture was detected as Unity `2022.3.68f1`, with 109 total assets and 107 editable deck assets. A round-trip test changes a card count, rebuilds the file, reopens it, and confirms that all 107 decks remain present.

## Use on iOS

1. Completely close PvZ Heroes before editing a file.
2. Launch Unity Asset Editor and tap **Open**.
3. Choose the `recipe_decks` file from the LiveContainer/SideStore-accessible folder.
4. Select a deck and make changes.
5. Tap **Save**. Keep the generated `.bak` file until the game has loaded successfully.

## Build the IPA with Codemagic (no personal Mac required)

The included `codemagic.yaml` asks Codemagic's hosted Mac to build and package the app. It creates the required `Payload/<app name>.app` directory inside `UnityAssetEditor.ipa`, so there is no manual renaming or ZIP conversion.

1. Put this project in a GitHub, GitLab, or Bitbucket repository. The repository root must be the folder containing `codemagic.yaml`.
2. Add that repository as an application in Codemagic.
3. Start the **Unity Asset Editor IPA** workflow from `codemagic.yaml`.
4. When the build finishes, download `UnityAssetEditor.ipa` from the build artifacts.
5. Install that IPA with SideStore or LiveContainer, which performs the final signing for your device.

`Runner.app` is Flutter's default bundle name. This is an Avalonia/.NET app, so the workflow detects its actual `.app` name instead of assuming `Runner.app`.

## Build the IPA on a Mac

For a local build, install Xcode 16.2 and the .NET 8 iOS workload.

```sh
dotnet workload install ios --skip-manifest-update
dotnet publish PvZAssetEditor.iOS/PvZAssetEditor.iOS.csproj \
  -c Release \
  -f net8.0-ios \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignKey=- \
  -p:ArchiveOnBuild=true \
  -p:BuildIpa=true
```

The repository also contains a manually triggered GitHub Actions workflow that produces `UnityAssetEditor.ipa`. SideStore performs the final signing when the IPA is installed.

## Desktop verification

The desktop host is useful for testing the same UI and asset engine before installing on a phone:

```sh
dotnet run --project PvZAssetEditor.Desktop/PvZAssetEditor.Desktop.csproj
```

Run the compatibility verifier against a local test file:

```sh
dotnet run --project PvZAssetEditor.Verifier/PvZAssetEditor.Verifier.csproj -- "/path/to/recipe_decks"
```

## Scope

This is the first PvZ-focused milestone, not yet a complete port of every UABEA plug-in. The underlying asset reader is general-purpose; additional editors for TextAsset, arbitrary type-tree data, textures, audio, and other UABEA operations can be added incrementally.

## License and attribution

Application code is provided under the MIT License. It uses [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET), also under the MIT License. See `THIRD_PARTY_NOTICES.md`.
