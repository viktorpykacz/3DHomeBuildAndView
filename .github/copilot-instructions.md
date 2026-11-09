# GitHub Copilot Instructions for 3DHomeBuildAndView

## Project Structure
- `StandaloneApp/HomeBuilderStandalone.csproj`: .NET 9 WPF desktop host application.
- `StandaloneApp/PdfCadParser.cs`: C# vector PDF parser extracting geometry, texts, layers, and Auto-BIM walls.
- `StandaloneApp/MainWindow.xaml.cs`: WebView2 host and asynchronous JSON bridge.
- `StandaloneApp/wwwroot/app.js`: Core 3D engine (Three.js), wall drawing, openings, sectional garage doors, furniture, MEP, FPP walkthrough, and Auto-BIM modal canvas.
- `StandaloneApp/wwwroot/index.html`: Ribbon UI, options bar, Auto-BIM modal, HUD controls.

## Development Rules
- Keep JavaScript pure vanilla ES6+ without external npm bundlers.
- Ensure all walls maintain strict orthogonal axes (Horizontal walls have identical Z coordinates; Vertical walls have identical X coordinates).
- In Auto-BIM parallel line extraction, never consume an entire corridor interval greedily; use multi-interval pairing with collinear corridor merging.
- Openings in walls support `WINDOW`, `DOOR`, and `GARAGE_DOOR`.
- Always verify JavaScript syntax using `node -c StandaloneApp/wwwroot/app.js`.
- Target .NET 9.0 on Windows x64.
- Do not commit `bin/` or `obj/` directories.
