# Source Gen Builder

Build and install Roslyn source generators and analyzers directly from the Unity Editor.

## Requirements

- Unity 2021.3+
- A .NET SDK, either bundled with the current Unity Editor or installed on PATH

The builder first checks the current Editor's `DotNetSdk` directory, then `dotnet` installations on the Editor's PATH. Each candidate must report an installed SDK through `--list-sdks`; a runtime-only installation is insufficient. Older Editor installations may require a separate SDK from [Microsoft](https://dotnet.microsoft.com/download).

The build log shows the selected executable and available SDK versions. If no SDK is usable, it lists the failed checks and setup instructions. After installing an SDK or changing PATH, restart Unity and its launching Unity Hub to refresh their environment. A generator's `global.json` or target framework can require a specific SDK version even when another SDK is available.

## Installation

In Unity, open **Window → Package Manager**, choose **Add package from git URL…**, and enter:

```text
https://github.com/FatLarva/unity-sourcegen-builder.git
```

The package ID is `com.fatlarva.sourcegenbuilder`. You can also clone the repository and use **Add package from disk…** to select its `package.json`, or add the repository as a Git submodule at `Packages/com.fatlarva.sourcegenbuilder`.

When replacing the original `com.sourcegenbuilder.editor` package, remove that package first. Script GUIDs, namespaces, and the `SourceGenBuilder.Editor` assembly name are preserved so existing Settings and Profile assets continue to work.

## Quick Start

1. **Create a Settings asset**: Assets → Create → Source Gen Builder → Settings
2. **Create a Profile asset**: Assets → Create → Source Gen Builder → Profile
3. **Configure the Profile**:
   - Set **Display Name** for identification in the builder window
   - Set **Source Root** to the directory containing your .NET projects (relative to Unity project root, e.g. `Assets/Scripts/GeneratorSources~/MyGenerator`)
   - Add **Dependencies** (e.g. attribute libraries) with their csproj paths and install locations
   - Configure the **Generator** with its csproj path and install location
4. **Add the Profile to Settings**: drag it into the profiles list
5. **Open the builder**: Tools → Source Gen Builder
6. **Assign the Settings asset** in the left panel (auto-detected if only one exists)
7. Click **Build All**

## Profile Configuration

Each profile describes one generator/analyzer and its dependencies:

Paths refer to physical files on disk. **Source Root** is relative to the Unity project root, and **Csproj Path** is relative to Source Root. When sources belong to an external or Git-installed package, use its actual folder on disk; Unity's virtual `Packages/<package-id>` paths are not resolved by the builder.

- **Source Root**: working directory for `dotnet build`, relative to Unity project root
- **Extra Build Args**: optional arguments appended to the `dotnet build` command
- **Dependencies**: built first, in order. Each has:
  - **Csproj Path**: relative to Source Root
  - **Install Dll Path**: full Unity asset path including filename (e.g. `Assets/Analyzers/MyLib.Attributes.dll`)
  - **Dll Type**: `Runtime` (all platforms enabled) or `Analyzer` (all platforms disabled + RoslynAnalyzer label)
- **Generator**: same fields as dependencies, defaults to `Analyzer` type

## Build Log

Build logs are stored in `Library/SourceGenBuilder/`:
- `build.log` — current/last build
- `build.prev.log` — previous build

## License

[MIT](LICENSE).
