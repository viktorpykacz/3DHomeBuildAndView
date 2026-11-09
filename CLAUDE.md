# CLAUDE.md — Instructions for Claude Code / Anthropic AI

## Project Overview
**3DHomeBuildAndView** is a desktop CAD/BIM application for Windows built with .NET 9 WPF and an embedded Chromium WebView2 browser hosting a 3D Three.js CAD engine.

## Common Commands

### Build & Run
- **Build Release:** `dotnet build StandaloneApp/HomeBuilderStandalone.csproj -c Release`
- **Build Debug:** `dotnet build StandaloneApp/HomeBuilderStandalone.csproj -c Debug`
- **Run Standalone App:** `dotnet run --project StandaloneApp/HomeBuilderStandalone.csproj -c Release`
- **Run via batch script:** `.\Uruchom_3DHomeBuildAndView.bat`
- **Validate JS Syntax:** `node -c StandaloneApp/wwwroot/app.js`

### Process Management
When rebuilding, the running executable may lock the binary in `bin/`. Kill it first:
```powershell
Stop-Process -Name "3DHomeBuildAndView" -Force -ErrorAction SilentlyContinue
```

## Architecture & Codebase Map
- **`StandaloneApp/MainWindow.xaml` / `.xaml.cs`**: WPF host window, initializes WebView2 and bidirectional async JSON message bridge (`PostWebMessageAsJson`).
- **`StandaloneApp/PdfCadParser.cs`**: Vector PDF parser and Auto-BIM geometric reconstructor (layer classification, parallel corridor pairing, scale detection).
- **`StandaloneApp/wwwroot/index.html`**: UI layout (Revit-style ribbon, options bar, Auto-BIM modal, compass HUD).
- **`StandaloneApp/wwwroot/app.js`**: Core 3D engine (Three.js r128), wall geometry mesh generator, openings, sectional garage doors, columns, MEP points, FPP navigation, undo/redo history.
- **`Assets/_Project/`**: Unity procedural geometry assets and topological graph algorithms.

## Code Conventions
- **Language Stack:** C# 13 / .NET 9 for desktop backend; Vanilla ES6+ JavaScript and Three.js for frontend (no React/Vue runtime).
- **Coordinate System:** 3D World (Y=up, X=width, Z=depth). Horizontal walls align with X ($Z_1 = Z_2$), Vertical walls align with Z ($X_1 = X_2$).
- **CAD Layer Independence:** Never hardcode CAD layer names in `PdfCadParser.cs` or `app.js`. Use geometric heuristics and statistical scoring.
- **Git Hygiene:** Never track or commit `bin/` or `obj/` directories.
- **Language:** Maintain Polish UI labels and Polish documentation matching user specifications.
