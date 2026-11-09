# 3DHomeBuildAndView 🏠📐

**3DHomeBuildAndView** to nowoczesne, hybrydowe środowisko desktopowe typu CAD / BIM do interaktywnego projektowania budynków, automatycznego importu i wektoryzacji rzutów architektonicznych (PDF Auto-BIM), modelowania ścian, stolarki otworowej, bram garażowych, instalacji MEP oraz wirtualnych spacerów w trybie pierwszoosobowym (FPP).

---

## 🚀 Główne Funkcjonalności

### 1. 📐 Inteligentny Import Rzutu PDF (Auto-BIM)
* **Uniwersalna klasyfikacja warstw CAD:** Automatyczne rozpoznawanie warstw ścian, okien, drzwi i bram garażowych bez zahardkodowanych nazw (analiza gęstości, długości i geometrii wektorów).
* **Zaawansowany algorytm parowania korytarzy ścian (Multi-Interval Pairing & Collinear Merging):** 
  * Wielointerwałowe wykrywanie równoległych krawędzi murów o grubościach $8 - 75\text{ cm}$.
  * Łączenie współliniowych korytarzy ściennych (eliminacja ucinania długich ścian elewacyjnych przez małe ścianki działowe).
  * Wykrywanie i automatyczna separacja słupów i podpór konstrukcyjnych (np. filary tarasowe o przekroju kwadratowym).
* **Automatyczne wykrywanie skali i kalibracja:** Odczyt skali z planu (np. `1:100`, `1:50`) wraz z przeliczaniem jednostek CAD na metry oraz podglądem wymiarów gabarytowych budynku.
* **Interaktywny podgląd geometrii CAD:** Modalne okno podglądu z płynnym powiększaniem (Zoom kółkiem myszy), przesuwaniem (Pan), dopasowaniem do budynku / arkusza, celownikiem współrzędnych oraz kolorystycznym rozróżnieniem elementów:
  * 🟩 **Ściany** (`#40c057`)
  * 🟨 **Słupy** (`#fcc419`)
  * 🟦 **Okna** (`#22b8cf`)
  * 🟧 **Drzwi** (`#fd7e14`)
  * 🟪 **Bramy garażowe** (`#cc5de8` / `#e599f7`)

### 2. 🧱 Modelowanie Ścian i Konstrukcji
* **Ściany 2D/3D z dynamicznymi narożnikami:** Ściany prostopadłe z 1D corner snapping (kąty proste 0% tiltu).
* **Interaktywna zmiana długości ścian:** Chwytanie uchwytów na końcach ściany z podglądem w czasie rzeczywistym i aktualizacją pozycji otworów stolarki.
* **Słupy i podpory architektoniczne:** Obiekty słupów o definiowalnej szerokości, głębokości i wysokości z oznaczeniem przekątnym 'X' na rzucie.
* **Podkład SVG z rzutu:** Skalowany wektorowy podkład rysunku podłogi idealnie zgrany ze środkiem ciężkości budynku.

### 3. 🚪 Stolarka Otworowa i Bramy Garażowe
* **Bramy garażowe segmentowe (`GARAGE_DOOR`):**
  * Generowanie realistycznego modelu 3D z 4 przetłaczanymi segmentami, fugami cieniowymi, uszczelką dolną, uchwytem inox oraz **prowadnicami sufitowymi** wchodzącymi w głąb pomieszczenia.
  * Opcje wykończenia: Antracyt (RAL 7016), Biały, Złoty dąb, Czarny.
  * Dedykowane narzędzie na wstążce oraz skrót szybkiego wyboru.
* **Okna i drzwi:** Automatyczne wycinanie otworów w ścianach, parapety, interaktywne skrzydła drzwiowe otwierane w spacerze FPP.

### 4. ⚡ Instalacje MEP (Instalacje Sanitarne i Elektryczne)
* Punkty instalacyjne: Gniazdka 230V, Siła 400V, Włączniki światła, Lampy sufitowe LED, Przyłącza wody ciepłej i zimnej, Odpływy kanalizacyjne 50mm.
* Inteligentne trasowanie i asocjacja: Narzędzie *Łączenie Włącznik → Lampa* z dynamicznym włączaniem/gaszeniem oświetlenia i zmianą natężenia światła otoczenia.

### 5. 🛋️ Wyposażenie i Meble Parametryczne
* Wbudowany katalog szafek kuchennych, blatów dębowych, sof i sprzętów AGD z automatycznym przyciąganiem do lica ściany (Wall Snapping) i strefami kolizji.
* Możliwość importu i buforowania własnych modeli 3D w formacie `.gltf` / `.glb`.

### 6. 🚶 Wirtualny Spacer Pierwszoosobowy (FPP Walkthrough)
* Płynna eksploracja wnętrza (sterowanie WASD + Mysz, Shift = bieg).
* Interaktywne otwieranie/zamykanie drzwi (klawisz E lub LPM).
* Symulacja oświetlenia dziennego w zależności od pory dnia (`06:00 - 22:00`).

### 7. 🖥️ Interfejs Użytkownika (Autodesk Revit / CAD Style)
* **Wstążka (Ribbon UI):** Pogrupowane zakładki (*Architektura*, *Konstrukcja i Meble*, *Instalacje MEP*, *Wstaw*, *Zarządzaj*).
* **Pasek opcji (Options Bar):** Kontekstowy panel dostosowujący widoczne pola (grubość, wysokość, wykończenie) w zależności od aktywnego narzędzia lub zaznaczonego obiektu.
* **Dolny panel HUD:** Róża wiatrów / kompas, zmiana rzutu 2D CAD / widok 3D Orbit / elewacja ściany, obsługa wielokondygnacyjności budynku.
* **Pełny system Undo/Redo:** Historia poleceń z obsługą skrótów `Ctrl+Z` i `Ctrl+Y`.

---

## 🛠️ Stos Technologiczny i Architektura

Projekt składa się z dwóch zsynchronizowanych warstw:

```
┌────────────────────────────────────────────────────────┐
│             Desktop Host (C# / .NET 9 WPF)             │
│  - MainWindow.xaml / MainWindow.xaml.cs                │
│  - Microsoft.Web.WebView2 (Chromium Embedded)          │
│  - PdfCadParser.cs (Natywny parser wektorów PDF)       │
└──────────────────────────┬─────────────────────────────┘
                           │ Asynchroniczny JSON IPC
┌──────────────────────────┴─────────────────────────────┐
│          Web CAD Engine (HTML5 / Three.js / JS)        │
│  - wwwroot/index.html (Ribbon UI, Canvas, HUD)         │
│  - wwwroot/app.js (Silnik renderujący, fizyka, raycast)│
│  - Three.js r128 (PBR Materials, Cienie, Oświetlenie)  │
└────────────────────────────────────────────────────────┘
```

Dodatkowo w katalogu `Assets/_Project` znajduje się silnik procedur Unity (opracowany pod kątem solverów topologii ścian i algorytmów siatek).

---

## 📦 Wymagania Systemowe

* **System operacyjny:** Windows 10 lub Windows 11 (64-bit)
* **Środowisko:** [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
* **Składnik systemu:** [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (zazwyczaj zainstalowany fabrycznie w Windows 10/11)

---

## 🏃 Uruchomienie i Kompilacja

### Szybkie uruchomienie skompilowanej wersji:
Uruchom skrypt znajdujący się w głównym katalogu projektu:
```cmd
Uruchom_3DHomeBuildAndView.bat
```

### Uruchomienie ze źródeł (.NET CLI):
```powershell
dotnet run --project StandaloneApp/HomeBuilderStandalone.csproj -c Release
```

### Kompilacja projektu:
```powershell
dotnet build StandaloneApp/HomeBuilderStandalone.csproj -c Release
```

---

## ⌨️ Przydatne Skróty Klawiszowe

| Skrót | Akcja |
| :--- | :--- |
| **LPM** | Rysowanie / Zaznaczanie / Interakcja / Zatwierdzenie |
| **PPM / Esc** | Zakończenie łańcucha rysowania ścian / Odznaczenie |
| **Shift** | Wymuszenie kątów ortogonalnych (45° / 90°) podczas rysowania ścian |
| **M** | Narzędzie przesuwania zaznaczonego obiektu |
| **R** | Obrót zaznaczonego mebla o 45° |
| **Delete / Backspace** | Usunięcie zaznaczonego elementu |
| **Ctrl + Z** | Cofnij (Undo) |
| **Ctrl + Y** | Ponów (Redo) |
| **W, A, S, D** | Poruszanie się w trybie spaceru FPP |
| **E** | Otwórz / Zamknij drzwi w trybie spaceru |

---

## 📄 Licencja

Projekt prywatny przeznaczony do projektowania i wizualizacji architektonicznej. Wszelkie prawa zastrzeżone.
