using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace HomeBuilderStandalone
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnMainWindowLoaded;
        }

        private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "3DHomeBuildAndView_WebView2");

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await CadWebView.EnsureCoreWebView2Async(env);

                CadWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                CadWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                string devWwwrootPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\wwwroot"));
                string wwwrootPath = Directory.Exists(devWwwrootPath)
                    ? devWwwrootPath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");

                await CadWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                await CadWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");

                CadWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "homebuilder.app",
                    wwwrootPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                CadWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                long cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                CadWebView.CoreWebView2.Navigate($"https://homebuilder.app/index.html?v={cacheBuster}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Błąd inicjalizacji silnika graficznego WebView2:\n{ex.Message}",
                    "3DHomeBuildAndView - Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("type", out JsonElement typeProp)) return;

                string? messageType = typeProp.GetString();
                switch (messageType)
                {
                    case "SAVE_PROJECT_JSON":
                        HandleSaveProjectJson(root);
                        break;
                    case "LOAD_PROJECT_JSON":
                        HandleLoadProjectJson();
                        break;
                    case "OPEN_BLUEPRINT_IMAGE":
                        HandleOpenBlueprintImage();
                        break;
                    case "PARSE_BLUEPRINT_PDF_DATA":
                        HandleParseBlueprintPdfData(root);
                        break;
                    case "IMPORT_GLTF_MODEL":
                        HandleImportGltfModel();
                        break;
                }
            }
            catch (Exception ex)
            {
                SendToWebView(new { type = "HOST_ERROR", message = ex.Message });
            }
        }

        private void HandleSaveProjectJson(JsonElement root)
        {
            string payload = root.TryGetProperty("payload", out JsonElement p) ? (p.GetString() ?? "{}") : "{}";
            var dialog = new SaveFileDialog
            {
                Title = "Zapisz Projekt Domu BIM (JSON)",
                Filter = "Projekt 3DHomeBuildAndView (*.json)|*.json",
                FileName = "ProjektDomu_BIM.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog(this) == true)
            {
                File.WriteAllText(dialog.FileName, payload);
                SendToWebView(new
                {
                    type = "PROJECT_SAVED",
                    filePath = dialog.FileName
                });
            }
        }

        private void HandleLoadProjectJson()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Wczytaj Projekt Domu BIM (JSON)",
                Filter = "Projekt 3DHomeBuildAndView (*.json)|*.json|Wszystkie pliki (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
            {
                string json = File.ReadAllText(dialog.FileName);
                SendToWebView(new
                {
                    type = "PROJECT_LOADED",
                    filePath = dialog.FileName,
                    payload = json
                });
            }
        }

        private void HandleOpenBlueprintImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Wybierz Rzut Architektoniczny (PDF / PNG / JPG / BMP)",
                Filter = "Wszystkie Rzuty i Pliki (*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.*)|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.*|Pliki PDF CAD (*.pdf)|*.pdf|Obrazy (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Wszystkie pliki (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();

                if (ext == ".pdf")
                {
                    var parsed = PdfCadParser.ParsePdf(bytes, Path.GetFileName(dialog.FileName));
                    if (parsed.Success)
                    {
                        string svgBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(parsed.BlueprintSvg));
                        string svgDataUrl = $"data:image/svg+xml;base64,{svgBase64}";

                        SendToWebView(new
                        {
                            type = "BLUEPRINT_PDF_CAD_PARSED",
                            fileName = Path.GetFileName(dialog.FileName),
                            dataUrl = svgDataUrl,
                            pdfData = parsed
                        });
                        return;
                    }
                }

                string mime = ext == ".png" ? "image/png" : (ext == ".pdf" ? "application/pdf" : "image/jpeg");
                string dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

                SendToWebView(new
                {
                    type = "BLUEPRINT_IMAGE_LOADED",
                    fileName = Path.GetFileName(dialog.FileName),
                    dataUrl
                });
            }
        }

        private void HandleParseBlueprintPdfData(JsonElement root)
        {
            try
            {
                string fileName = root.TryGetProperty("fileName", out JsonElement f) ? (f.GetString() ?? "rzut.pdf") : "rzut.pdf";
                string dataUrl = root.TryGetProperty("dataUrl", out JsonElement d) ? (d.GetString() ?? "") : "";
                if (dataUrl.Contains(","))
                {
                    string base64 = dataUrl.Substring(dataUrl.IndexOf(",") + 1);
                    byte[] bytes = Convert.FromBase64String(base64);
                    var parsed = PdfCadParser.ParsePdf(bytes, fileName);
                    if (parsed.Success)
                    {
                        string svgBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(parsed.BlueprintSvg));
                        string svgDataUrl = $"data:image/svg+xml;base64,{svgBase64}";

                        SendToWebView(new
                        {
                            type = "BLUEPRINT_PDF_CAD_PARSED",
                            fileName,
                            dataUrl = svgDataUrl,
                            pdfData = parsed
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SendToWebView(new { type = "HOST_ERROR", message = ex.Message });
            }
        }

        private void HandleImportGltfModel()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Importuj Własny Model 3D Mebla (.glb / .gltf)",
                Filter = "Modele 3D glTF/GLB (*.glb;*.gltf)|*.glb;*.gltf"
            };

            if (dialog.ShowDialog(this) == true)
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                string base64 = Convert.ToBase64String(bytes);

                SendToWebView(new
                {
                    type = "GLTF_MODEL_LOADED",
                    fileName = Path.GetFileName(dialog.FileName),
                    base64Data = base64
                });
            }
        }

        private void SendToWebView(object messageObj)
        {
            string json = JsonSerializer.Serialize(messageObj);
            CadWebView.CoreWebView2?.PostWebMessageAsJson(json);
        }
    }
}
