# Changelog

## [1.0.0] - 2026-03-16

### Added
- Editor window (Tools → Source Gen Builder) with split-panel layout
- ScriptableObject-based profile system for configuring generators and dependencies
- Settings asset for managing ordered profile lists
- Automatic `dotnet build` invocation with async process management
- DLL copy and Unity PluginImporter configuration (Runtime / Analyzer)
- RoslynAnalyzer label auto-application for analyzer DLLs
- Modal window during builds to prevent editor interaction
- File-backed build log with single-file rotation
- Path normalization and validation with descriptive error messages
- Build queue supporting multiple profiles with dependencies
- First-time install detection (only configures importers on initial install)
