# AGENTS.md — Developer & AI Agent Guidelines for 3DHomeBuildAndView

Ten dokument zawiera specyfikację architektoniczną, wytyczne i instrukcje dla autonomicznych agentów AI (np. Antigravity, Claude Code, Cursor, Copilot, Devin) pracujących w repozytorium **3DHomeBuildAndView**.

---

## 1. Przegląd Projektu i Architektury

Projekt to aplikacja desktopowa CAD/BIM dla systemu Windows, łącząca wydajność .NET 9 WPF z silnikiem renderującym 3D Three.js osadzonym przez Chromium WebView2.

### Główne komponenty:
1. **Host Desktopowy (C# / .NET 9 WPF):**
   * [`StandaloneApp/MainWindow.xaml.cs`](file:///C:/Users/vikto/OneDrive/Programowanie/Aplikacje_Desktopowe/3DHomeBuildAndView/StandaloneApp/MainWindow.xaml.cs): Okno główne hostujące kontrolkę WebView2, zarządzające asynchronicznym mostkiem IPC (`WebMessageReceived` / `PostWebMessageAsJson`).
   * [`StandaloneApp/PdfCadParser.cs`](file:///C:/Users/vikto/OneDrive/Programowanie/Aplikacje_Desktopowe/3DHomeBuildAndView/StandaloneApp/PdfCadParser.cs): Natywny silnik parsowania wektorów z plików PDF (strumienie `/OC` Optional Content, operatory `m`, `l`, `c`, `re`, teksty `Tj`), uniwersalna klasyfikacja warstw (ściany, okna, drzwi) oraz rekonstrukcja osi murów (multi-interval corridor matching).
2. **Frontend CAD / Three.js (Vanilla JS + HTML5 + CSS):**
   * [`StandaloneApp/wwwroot/index.html`](file:///C:/Users/vikto/OneDrive/Programowanie/Aplikacje_Desktopowe/3DHomeBuildAndView/StandaloneApp/wwwroot/index.html): Struktura UI — wstążka narzędziowa w stylu Autodesk Revit / AutoCAD, contextual options bar, interaktywne okno Auto-BIM, róża wiatrów / kompas oraz pasek telemetryczny.
   * [`StandaloneApp/wwwroot/app.js`](file:///C:/Users/vikto/OneDrive/Programowanie/Aplikacje_Desktopowe/3DHomeBuildAndView/StandaloneApp/wwwroot/app.js): Główny silnik aplikacji — sceny Three.js r128, procedury generowania geometrii ścian (z otworowaniem stolarki), słupy, meble parametryczne, instalacje MEP, silnik kolizji/spaceru FPP, historia Undo/Redo oraz Auto-BIM 2D preview canvas.
3. **Unity Engine Prototype (C#):**
   * [`Assets/_Project/`](file:///C:/Users/vikto/OneDrive/Programowanie/Aplikacje_Desktopowe/3DHomeBuildAndView/Assets/_Project/): Moduły logiki procedur Unity (solvery topologii grafu ścian, mesh buildery, generatory MEP) służące jako wzorzec algorytmiczny.

---

## 2. Układ Współrzędnych i Konwencje BIM

1. **Przestrzeń 3D (Three.js):**
   * `X`: Oś pozioma (lewo/prawo)
   * `Y`: Oś pionowa (wysokość kondygnacji, elewacja podłogi / sufitu)
   * `Z`: Oś głębokości (przód/tył)
2. **Konwencja orientacji ścian:**
   * **Ściany poziome (H):** Ściśle $Z_1 = Z_2$ (długość wzdłuż osi X; 0% odchyłki kątowej).
   * **Ściany pionowe (V):** Ściśle $X_1 = X_2$ (długość wzdłuż osi Z; 0% odchyłki kątowej).
   * **Narożniki ścian:** Corner snapping 1D (tolerancja $\approx 0{,}38\text{ m}$) przyciąga końce ścian do prostopadłych ścian stykających się.
3. **Otwory stolarki (Openings):**
   * Otwory są dziećmi obiektu ściany (`wall.openings[]`) i są definiowane przez:
     * `dist`: odległość środka otworu od punktu początkowego ściany wzdłuż jej wektora kierunkowego.
     * `width`: szerokość otworu w metrach.
     * `height`: wysokość otworu w metrach.
     * `sill`: wysokość parapetu od podłogi ($0{,}0\text{ m}$ dla drzwi i bram garażowych, $\approx 0{,}85\text{ m}$ dla okien).
     * `type`: `'WINDOW'`, `'DOOR'`, `'GARAGE_DOOR'`.
4. **Auto-BIM Corridor Pairing (Zasada krytyczna):**
   * Długie linie elewacyjne (np. 19 m) nie mogą być pochłaniane zachłannie przez pierwszy krótki interwał. Zawsze stosuj podejście wielointerwałowe (**multi-interval corridor pairing**) z późniejszym łączeniem korytarzy współliniowych (**collinear corridor merging**), aby nie gubić ścian salonu, garażu czy tarasu.

---

## 3. Kompilacja i Uruchamianie

### Budowanie projektu:
```powershell
dotnet build StandaloneApp/HomeBuilderStandalone.csproj -c Release
```

### Uruchamianie aplikacji:
```powershell
dotnet run --project StandaloneApp/HomeBuilderStandalone.csproj -c Release
```

### Walidacja składni JavaScript:
Przed commitem kodu JS zawsze sprawdź poprawność składni:
```powershell
node -c StandaloneApp/wwwroot/app.js
```

### Obsługa blokad plików .exe:
Jeśli proces `3DHomeBuildAndView.exe` działa w tle podczas kompilacji, zablokuje pliki binarne w `bin/Release/`. Przed budowaniem zamknij go poleceniem:
```powershell
Stop-Process -Name "3DHomeBuildAndView" -Force -ErrorAction SilentlyContinue
```

---

## 4. Zasady Pisania Kodu i Bezpieczeństwa

1. **Czystość repozytorium Git:**
   * Nigdy nie commituj katalogów `bin/` ani `obj/` (są ignorowane w `.gitignore`).
   * Pliki binarne bibliotek JavaScript (Three.js, GLTFLoader) znajdują się w `StandaloneApp/wwwroot/lib/`.
2. **Zachowanie integralności kodu:**
   * Nie zastępuj działających procedur Three.js zewnętrznymi bibliotekami bez wyraźnej prośby użytkownika.
   * Nie hardkoduj nazw warstw CAD z konkretnych plików architektonicznych – algorytm klasyfikacji warstw musi działać uniwersalnie dla dowolnego rzutu.
   * Utrzymuj skróty klawiszowe (np. `Shift`, `M`, `R`, `Delete`, `Esc`, `Ctrl+Z`, `Ctrl+Y`).
3. **Styl komunikacji:**
   * Odpowiadaj zwięźle, w języku polskim, w formacie Markdown z klikalnymi linkami do plików.
