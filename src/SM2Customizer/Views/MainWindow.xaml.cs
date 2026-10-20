using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using SM2Customizer.Core;
using SM2Customizer.Models;
using SM2Customizer.Services;

namespace SM2Customizer.Views
{
    public partial class MainWindow : Window
    {
        private GameWorkspace? _workspace;
        private readonly GameScannerService _scanner = new GameScannerService();
        private readonly ModDeployService _deployService = new ModDeployService();
        private readonly CustomModelImportService _importer = new CustomModelImportService();
        private readonly GameModelService _gameModelService = new GameModelService();
        private UserConfig _config = new UserConfig();
        private string _activeArmorSlotKey = "HELMET";

        private PaintColorModel? _selectedColor;
        private bool _isUpdatingUi = false;
        private bool _isDarkMode = true;

        public MainWindow()
        {
            _isUpdatingUi = true;
            InitializeComponent();
            _isUpdatingUi = false;
            Loaded += MainWindow_Loaded;
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(!_isDarkMode);
        }

        public void ApplyTheme(bool isDark)
        {
            _isDarkMode = isDark;
            var res = Application.Current.Resources;

            if (isDark)
            {
                // Dark Mode: Dark Navy base, Vivid Forge Orange / Silver accents, Crisp light text
                res["BgDarkBrush"] = new SolidColorBrush(Color.FromRgb(0x0C, 0x10, 0x1A));
                res["CardBgBrush"] = new SolidColorBrush(Color.FromRgb(0x13, 0x19, 0x26));
                res["CardHeaderBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x34));
                res["ControlBgBrush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x1F, 0x30));
                res["ControlHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x24, 0x31, 0x4A));
                res["GoldAccentBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00));
                res["GoldHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xA3, 0x33));
                res["GoldPressedBrush"] = new SolidColorBrush(Color.FromRgb(0xD9, 0x65, 0x00));
                res["TextBrush"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x26, 0x34, 0x4B));
                res["BorderLightBrush"] = new SolidColorBrush(Color.FromRgb(0x3E, 0x52, 0x75));
                res["PopupBgBrush"] = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x24));

                BtnToggleTheme.Content = "🌙 Dark Mode";
                TxtStatus.Text = "Theme switched to Dark Mode (Navy & Forge Orange).";
            }
            else
            {
                // Light Mode: Pure White / Light Slate base, Deep Forge Orange & Charcoal/Black text
                res["BgDarkBrush"] = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFA));
                res["CardBgBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                res["CardHeaderBrush"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xF0, 0xF6));
                res["ControlBgBrush"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                res["ControlHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                res["GoldAccentBrush"] = new SolidColorBrush(Color.FromRgb(0xD9, 0x65, 0x00));
                res["GoldHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
                res["GoldPressedBrush"] = new SolidColorBrush(Color.FromRgb(0xB3, 0x4B, 0x00));
                res["TextBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                res["BorderLightBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                res["PopupBgBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

                BtnToggleTheme.Content = "☀️ Light Mode";
                TxtStatus.Text = "Theme switched to Light Mode (White & Charcoal).";
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Initialize3DViewportAsync();

            _config = ConfigStoreService.Load();

            string? gamePath = _config.LastGamePath;
            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                gamePath = GameWorkspace.TryAutoDetect();
            }

            if (!string.IsNullOrEmpty(gamePath))
            {
                TxtGamePath.Text = gamePath;
                LoadGameDirectory(gamePath);
            }
        }

        private async Task Initialize3DViewportAsync()
        {
            try
            {
                string webDir = EmbeddedWebHost.GetWebAssetsDirectory();
                await Wv3DViewport.EnsureCoreWebView2Async();
                Wv3DViewport.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "sm2customizer.local",
                    webDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                Wv3DViewport.CoreWebView2.Navigate("https://sm2customizer.local/viewer.html");
                Wv3DViewport.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                Wv3DViewport.NavigationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        PushCurrentStateTo3D();
                    }
                };
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"3D Viewport status: {ex.Message}";
            }
        }

        private void PushCurrentStateTo3D()
        {
            if (Wv3DViewport?.CoreWebView2 == null) return;

            if (_selectedColor != null)
            {
                SendColorTo3D(_selectedColor);
            }
            else if (_scanner.Colors.Count > 0)
            {
                var defaultColor = _scanner.Colors.FirstOrDefault(c => c.Id.Contains("Macragge", StringComparison.OrdinalIgnoreCase))
                                   ?? _scanner.Colors[0];
                _selectedColor = defaultColor;
                SendColorTo3D(defaultColor);
            }

            string currentTeam = CmbTeam?.SelectedItem as string ?? "LOYALIST";
            string currentClass = CmbClass?.SelectedValue as string ?? "PVE_PVP_SOLDIER";
            string currentPreset = CmbPreset?.SelectedValue as string ?? CmbPreset?.SelectedItem as string ?? "PREDEFINED_PRESET_1";

            var activePreset = _scanner.Presets.FirstOrDefault(p =>
                p.Team == currentTeam && p.ClassKey == currentClass && p.PresetKey == currentPreset);

            if (activePreset != null)
            {
                foreach (var kvp in activePreset.SectionPieces)
                {
                    SendPieceTo3D(kvp.Key, kvp.Value);
                }
            }
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("action", out var act))
                {
                    string actionName = act.GetString() ?? "";
                    if (actionName == "viewerReady")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PushCurrentStateTo3D();
                        });
                    }
                    else if (actionName == "selectSlot")
                    {
                        if (root.TryGetProperty("slot", out var slotEl))
                        {
                            string slot = slotEl.GetString() ?? "";
                            Dispatcher.Invoke(() =>
                            {
                                MainTabs.SelectedIndex = 0; // Focus Armor Customizer
                                TxtStatus.Text = $"3D Viewport: Selected [{slot.Replace("_", " ")}]";
                            });
                        }
                    }
                    else if (actionName == "screenshotCaptured")
                    {
                        string? dataUrl = root.TryGetProperty("dataUrl", out var du) ? du.GetString() : null;
                        if (!string.IsNullOrEmpty(dataUrl) && dataUrl.Contains(","))
                        {
                            try
                            {
                                string base64 = dataUrl.Substring(dataUrl.IndexOf(",") + 1);
                                byte[] bytes = Convert.FromBase64String(base64);
                                string exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exports");
                                Directory.CreateDirectory(exportDir);
                                string filename = $"Astartes_Armour_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                                string filePath = Path.Combine(exportDir, filename);
                                File.WriteAllBytes(filePath, bytes);
                                File.WriteAllBytes(Path.Combine(exportDir, "Astartes_Armour_latest.png"), bytes);
                                Dispatcher.Invoke(() =>
                                {
                                    TxtStatus.Text = $"Snapshot saved to exports/{filename}";
                                    TxtStatus.Foreground = (Brush)FindResource("GoldAccentBrush");
                                });
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    TxtStatus.Text = $"Failed to save snapshot: {ex.Message}";
                                    TxtStatus.Foreground = Brushes.Red;
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void LoadGameDirectory(string path)
        {
            _workspace = new GameWorkspace(path);
            if (!_workspace.IsValid)
            {
                TxtStatus.Text = $"Selected directory is invalid. Could not find default_other.pak in: {path}";
                TxtStatus.Foreground = Brushes.Red;
                return;
            }

            _config.LastGamePath = _workspace.GameRootPath;
            ConfigStoreService.Save(_config);

            TxtStatus.Text = "Scanning game packages...";
            TxtStatus.Foreground = (Brush)FindResource("TextMutedBrush");

            if (!_scanner.Scan(_workspace, out string error))
            {
                TxtStatus.Text = $"Scan failed: {error}";
                TxtStatus.Foreground = Brushes.Red;
                MessageBox.Show(error, "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            TxtStatus.Text = $"Game data loaded successfully! Found {_scanner.Presets.Count} Presets, {_scanner.AllArmorPieces.Count} Armor Pieces, {_scanner.Colors.Count} Colors, {_scanner.Decals.Count} Decals.";
            TxtStatus.Foreground = (Brush)FindResource("GoldAccentBrush");

            PopulateArmorCustomizer();
            PopulateColorsTab();
            PopulateDecalsTab();
            PopulatePatternsTab();

            PushCurrentStateTo3D();
        }

        #region Armor Customizer

        private void PopulateArmorCustomizer()
        {
            _isUpdatingUi = true;
            CmbTeam.ItemsSource = new[] { "LOYALIST", "CHAOS" };
            CmbTeam.SelectedItem = "LOYALIST";

            UpdateClassList();
            _isUpdatingUi = false;

            RefreshArmorSlots();
        }

        private void UpdateClassList()
        {
            string currentTeam = CmbTeam.SelectedItem as string ?? "LOYALIST";
            var classes = _scanner.Presets
                .Where(p => p.Team == currentTeam)
                .Select(p => p.ClassKey)
                .Distinct()
                .Select(k => new { Key = k, Display = ClassPresetModel.GetClassDisplayName(k) })
                .ToList();

            CmbClass.ItemsSource = classes;
            CmbClass.DisplayMemberPath = "Display";
            CmbClass.SelectedValuePath = "Key";
            if (classes.Count > 0)
            {
                CmbClass.SelectedIndex = 0;
            }

            UpdatePresetList();
        }

        private void UpdatePresetList()
        {
            string currentTeam = CmbTeam.SelectedItem as string ?? "LOYALIST";
            string currentClass = CmbClass.SelectedValue as string ?? "PVE_PVP_SOLDIER";

            var presets = _scanner.Presets
                .Where(p => p.Team == currentTeam && p.ClassKey == currentClass)
                .Select(p => p.PresetKey)
                .Distinct()
                .Select(k => new { Key = k, DisplayName = FormatPresetDisplayName(k) })
                .ToList();

            CmbPreset.SelectedValuePath = "Key";
            CmbPreset.DisplayMemberPath = "DisplayName";
            CmbPreset.ItemsSource = presets;
            if (presets.Count > 0)
            {
                CmbPreset.SelectedIndex = 0;
            }
        }

        private static readonly HashSet<string> PlayableSections = new(StringComparer.OrdinalIgnoreCase)
        {
            "HELMET", "CHEST", "BACKPACK", "LEFT_PAULDRON", "RIGHT_PAULDRON",
            "LEFT_GAUNTLET", "RIGHT_GAUNTLET", "LEFT_GREAVE", "RIGHT_GREAVE",
            "CLOTH", "COAT", "HALO", "VANITY"
        };

        private static bool IsLegitArmorPiece(ArmorPieceItem piece)
        {
            if (string.IsNullOrWhiteSpace(piece.Uid)) return false;
            string uid = piece.Uid.ToLowerInvariant();

            // Bare Head is a legitimate full 3D head model
            if (piece.Section.Equals("HELMET", StringComparison.OrdinalIgnoreCase) && uid.Contains("no_helmet"))
                return true;

            // Filter out empty sentinels
            if (uid.Contains("_empty") || uid.Contains("_none") || uid == "none" || uid.EndsWith("_empty") || uid.EndsWith("_none"))
                return false;

            // Filter out developer / hidden / placeholder / mini / reference entries
            if (uid.Contains("_hidden") || uid.Contains("hidden_") || uid.Contains("refrt_") ||
                uid.Contains("test_") || uid.Contains("dummy") || uid.Contains("placeholder") ||
                uid.Contains("lasfusil_tubes"))
                return false;

            return true;
        }

        private void RefreshArmorSlots()
        {
            if (_isUpdatingUi || !IsLoaded || CmbTeam == null || CmbClass == null || CmbPreset == null || ItemsArmorSlots == null) return;

            string currentTeam = CmbTeam.SelectedItem as string ?? "LOYALIST";
            string currentClass = CmbClass.SelectedValue as string ?? "PVE_PVP_SOLDIER";
            string currentPreset = CmbPreset.SelectedValue as string ?? CmbPreset.SelectedItem as string ?? "PREDEFINED_PRESET_1";

            var activePreset = _scanner.Presets.FirstOrDefault(p =>
                p.Team == currentTeam && p.ClassKey == currentClass && p.PresetKey == currentPreset);

            bool showCrossClass = ChkCrossClassFilter.IsChecked == true;
            string currentClassName = ClassPresetModel.GetClassDisplayName(currentClass);
            string filterText = TxtArmorPieceSearch?.Text?.Trim().ToLowerInvariant() ?? "";

            var slots = new List<BodySectionSlot>();

            // Filter and sort body sections in anatomical order: Head -> Torso -> Shoulders -> Arms -> Legs -> Wargear
            var sortedSections = _scanner.BodySections
                .Where(s => PlayableSections.Contains(s))
                .OrderBy(s => GetSectionSortOrder(s))
                .ToList();

            foreach (var section in sortedSections)
            {
                var slot = new BodySectionSlot
                {
                    SectionKey = section,
                    DisplayName = FormatSectionName(section),
                    Category = GetSectionCategory(section),
                    CategoryOrder = GetSectionSortOrder(section)
                };

                var candidates = _scanner.AllArmorPieces
                    .Where(p => string.Equals(p.Section, section, StringComparison.OrdinalIgnoreCase))
                    .Where(IsLegitArmorPiece)
                    .Where(p => showCrossClass || p.ClassOrigin == currentClassName || string.IsNullOrEmpty(p.ClassOrigin))
                    .Where(p => string.IsNullOrEmpty(filterText) ||
                                p.DisplayName.ToLowerInvariant().Contains(filterText) ||
                                p.Uid.ToLowerInvariant().Contains(filterText) ||
                                p.TierBadge.ToLowerInvariant().Contains(filterText))
                    .OrderBy(p => p.DisplayName)
                    .ToList();

                string currentPieceUid = string.Empty;
                if (activePreset != null && activePreset.SectionPieces.TryGetValue(section, out var uid))
                {
                    currentPieceUid = uid;
                }

                // If active preset piece is empty or not in candidates, pick first legitimate piece
                if (candidates.Count > 0)
                {
                    if (string.IsNullOrEmpty(currentPieceUid) || !candidates.Exists(c => c.Uid == currentPieceUid))
                    {
                        var matchingLegit = candidates.FirstOrDefault(c => string.Equals(c.Uid, currentPieceUid, StringComparison.OrdinalIgnoreCase));
                        currentPieceUid = matchingLegit?.Uid ?? candidates[0].Uid;
                    }
                }

                slot.AvailablePieces = candidates;
                slot.SelectedPieceUid = currentPieceUid;
                slots.Add(slot);
            }

            var view = CollectionViewSource.GetDefaultView(slots);
            if (view != null)
            {
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BodySectionSlot.Category)));
            }

            ItemsArmorSlots.ItemsSource = view;

            if (activePreset != null)
            {
                foreach (var slot in slots)
                {
                    if (!string.IsNullOrEmpty(slot.SelectedPieceUid))
                    {
                        SendPieceTo3D(slot.SectionKey, slot.SelectedPieceUid);
                    }
                }
            }
        }

        private static int GetSectionSortOrder(string section)
        {
            return section.ToUpperInvariant() switch
            {
                "HELMET" => 10,
                "HELMET_LENSES" => 15,
                "ANTENNA" => 18,
                "CHEST" => 20,
                "BACKPACK" => 30,
                "LEFT_PAULDRON" => 40,
                "RIGHT_PAULDRON" => 50,
                "LEFT_GAUNTLET" => 60,
                "RIGHT_GAUNTLET" => 70,
                "LEFT_GREAVE" => 80,
                "RIGHT_GREAVE" => 90,
                "VANITY" => 100,
                "CLOTH" => 110,
                "COAT" => 120,
                "HALO" => 130,
                _ => 200
            };
        }

        private static string GetSectionCategory(string section)
        {
            return section.ToUpperInvariant() switch
            {
                "HELMET" or "HELMET_LENSES" or "ANTENNA" => "HEAD & SENSORS",
                "CHEST" or "BACKPACK" => "TORSO & POWER PACK",
                "LEFT_PAULDRON" or "RIGHT_PAULDRON" or "LEFT_GAUNTLET" or "RIGHT_GAUNTLET" => "SHOULDERS & ARMS",
                "LEFT_GREAVE" or "RIGHT_GREAVE" => "LEGS & GREAVES",
                "VANITY" or "CLOTH" or "COAT" or "HALO" => "WARGEAR & RELICS",
                _ => "CUSTOM ARMOUR"
            };
        }

        public static string FormatSectionName(string section)
        {
            return section.ToUpperInvariant() switch
            {
                "HELMET" => "Helmet & Visor",
                "HELMET_LENSES" => "Helmet Optics & Lenses",
                "CHEST" => "Torso & Cuirass",
                "BACKPACK" => "Power Pack",
                "LEFT_PAULDRON" => "Left Shoulder (Chapter)",
                "RIGHT_PAULDRON" => "Right Shoulder (Tactical)",
                "LEFT_GAUNTLET" => "Left Gauntlet & Arm",
                "RIGHT_GAUNTLET" => "Right Gauntlet & Arm",
                "LEFT_GREAVE" => "Left Greave & Sabaton",
                "RIGHT_GREAVE" => "Right Greave & Sabaton",
                "VANITY" => "Shield & Vanity Wargear",
                "CLOTH" => "Tabard & Loincloth",
                "COAT" => "Cape & Robes",
                "HALO" => "Iron Halo & Crest",
                "ANTENNA" => "Vox Antenna & Sensor",
                _ => char.ToUpperInvariant(section[0]) + section.Substring(1).ToLowerInvariant().Replace("_", " ")
            };
        }

        private static string FormatPresetDisplayName(string presetKey)
        {
            if (presetKey.StartsWith("PREDEFINED_PRESET_", StringComparison.OrdinalIgnoreCase))
            {
                string num = presetKey.Substring(18);
                return $"Armour Set {FormatRoman(num)}";
            }
            return presetKey.Replace("_", " ");
        }

        private static string FormatRoman(string s)
        {
            if (int.TryParse(s, out int n))
            {
                return n switch
                {
                    1 => "I",
                    2 => "II",
                    3 => "III",
                    4 => "IV",
                    5 => "V",
                    6 => "VI",
                    7 => "VII",
                    8 => "VIII",
                    _ => n.ToString()
                };
            }
            return s.ToUpperInvariant();
        }

        private void TxtArmorPieceSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            RefreshArmorSlots();
        }

        private void BtnFocusSlot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sectionKey)
            {
                _activeArmorSlotKey = sectionKey;
                Focus3DSlot(sectionKey);
            }
        }

        private void Focus3DSlot(string sectionKey)
        {
            if (Wv3DViewport?.CoreWebView2 == null) return;
            string target = sectionKey.ToLowerInvariant() switch
            {
                "helmet" or "helmet_lenses" or "antenna" => "helmet",
                "chest" => "chest",
                "backpack" => "backpack",
                "left_pauldron" => "left_pauldron",
                "right_pauldron" => "right_pauldron",
                "left_gauntlet" or "right_gauntlet" => "arms",
                "left_greave" or "right_greave" => "legs",
                _ => "full"
            };
            string script = $"window.focusCamera && window.focusCamera('{target}');";
            Wv3DViewport.CoreWebView2.ExecuteScriptAsync(script);
        }

        private void CmbTeam_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            UpdateClassList();
            RefreshArmorSlots();
        }

        private void CmbClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            UpdatePresetList();
            RefreshArmorSlots();
        }

        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            RefreshArmorSlots();
        }

        private void ChkCrossClassFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            RefreshArmorSlots();
        }

        private void SlotPiece_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            if (sender is ComboBox cb && cb.DataContext is BodySectionSlot slot)
            {
                _activeArmorSlotKey = slot.SectionKey;
                if (cb.SelectedItem is ArmorPieceItem piece)
                {
                    slot.SelectedPieceUid = piece.Uid;
                    SendPieceTo3D(slot.SectionKey, piece.Uid);
                }
            }
        }

        private void SendPieceTo3D(string slot, string pieceUid)
        {
            if (Wv3DViewport?.CoreWebView2 == null) return;

            try
            {
                var slotMsg = JsonSerializer.Serialize(new { action = "setSlotPiece", slot = slot, pieceUid = pieceUid });
                Wv3DViewport.CoreWebView2.PostWebMessageAsJson(slotMsg);
            }
            catch { }

            if (_workspace != null && _workspace.IsValid)
            {
                string currentClass = string.Empty;
                Dispatcher.Invoke(() =>
                {
                    currentClass = CmbClass?.SelectedValue as string ?? "PVE_PVP_SOLDIER";
                });

                var piece = _scanner.AllArmorPieces.FirstOrDefault(p => string.Equals(p.Uid, pieceUid, StringComparison.OrdinalIgnoreCase));
                var meshes = piece?.Meshes;

                Task.Run(() =>
                {
                    if (_gameModelService.TryGetSlotGameMesh(_workspace, slot, pieceUid, meshes, currentClass, out var objData))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (Wv3DViewport?.CoreWebView2 != null)
                                {
                                    var modelMsg = JsonSerializer.Serialize(new { action = "loadCustomObj", slot = slot, objData = objData });
                                    Wv3DViewport.CoreWebView2.PostWebMessageAsJson(modelMsg);
                                }
                            }
                            catch { }
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (Wv3DViewport?.CoreWebView2 != null)
                                {
                                    var clearMsg = JsonSerializer.Serialize(new { action = "clearSlotMesh", slot = slot });
                                    Wv3DViewport.CoreWebView2.PostWebMessageAsJson(clearMsg);
                                }
                            }
                            catch { }
                        });
                    }
                });
            }
        }

        private void BtnQuickReset_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            RefreshArmorSlots();
            string presetName = CmbPreset?.Text ?? "Selected Preset";
            TxtStatus.Text = $"Armor Customizer: Reset all armor slots to {presetName}.";
        }

        private void BtnQuickRandomize_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded || ItemsArmorSlots?.ItemsSource == null) return;

            var random = new Random();
            var view = ItemsArmorSlots.ItemsSource as System.ComponentModel.ICollectionView;
            var slots = view?.SourceCollection as IEnumerable<BodySectionSlot>;
            if (slots == null) return;

            int changedCount = 0;
            foreach (var slot in slots)
            {
                var legitCandidates = slot.AvailablePieces.Where(IsLegitArmorPiece).ToList();
                if (legitCandidates.Count > 0)
                {
                    var pick = legitCandidates[random.Next(legitCandidates.Count)];
                    slot.SelectedPieceUid = pick.Uid;
                    SendPieceTo3D(slot.SectionKey, pick.Uid);
                    changedCount++;
                }
            }

            view?.Refresh();
            TxtStatus.Text = $"Armor Customizer: Randomized {changedCount} armor slots.";
        }

        private void BtnQuickClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded || ItemsArmorSlots?.ItemsSource == null) return;

            var view = ItemsArmorSlots.ItemsSource as System.ComponentModel.ICollectionView;
            var slots = view?.SourceCollection as IEnumerable<BodySectionSlot>;
            if (slots == null) return;

            int clearedCount = 0;
            foreach (var slot in slots)
            {
                slot.SelectedPieceUid = string.Empty;
                SendClearSlotTo3D(slot.SectionKey);
                clearedCount++;
            }

            view?.Refresh();
            TxtStatus.Text = $"Armor Customizer: Cleared {clearedCount} armor slots.";
        }

        private void SendClearSlotTo3D(string slot)
        {
            if (Wv3DViewport?.CoreWebView2 == null) return;
            try
            {
                var clearSlotMsg = JsonSerializer.Serialize(new { action = "setSlotPiece", slot = slot, pieceUid = "" });
                Wv3DViewport.CoreWebView2.PostWebMessageAsJson(clearSlotMsg);

                var clearMeshMsg = JsonSerializer.Serialize(new { action = "clearSlotMesh", slot = slot });
                Wv3DViewport.CoreWebView2.PostWebMessageAsJson(clearMeshMsg);
            }
            catch { }
        }

        #endregion

        #region Paints & Shaders

        private void PopulateColorsTab()
        {
            FilterColors();
        }

        private void FilterColors(PaintColorModel? targetColor = null)
        {
            _isUpdatingUi = true;
            try
            {
                string filter = TxtColorSearch?.Text?.Trim().ToLowerInvariant() ?? "";
                var filtered = string.IsNullOrEmpty(filter)
                    ? _scanner.Colors.ToList()
                    : _scanner.Colors.Where(c => c.DisplayName.ToLowerInvariant().Contains(filter) || c.Id.ToLowerInvariant().Contains(filter)).ToList();

                var selected = targetColor ?? LstColors.SelectedItem as PaintColorModel;

                LstColors.ItemsSource = null;
                LstColors.ItemsSource = filtered;

                if (selected != null && filtered.Contains(selected))
                {
                    LstColors.SelectedItem = selected;
                }
                else if (filtered.Count > 0)
                {
                    var macragge = filtered.FirstOrDefault(c => c.Id.Contains("Macragge", StringComparison.OrdinalIgnoreCase));
                    LstColors.SelectedItem = macragge ?? filtered[0];
                }
            }
            finally
            {
                _isUpdatingUi = false;
            }

            if (LstColors.SelectedItem is PaintColorModel activeColor)
            {
                _selectedColor = activeColor;
                LoadColorIntoEditor(activeColor);
                SendColorTo3D(activeColor);
            }
        }

        private void TxtColorSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterColors();
        }

        private void LstColors_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;

            if (LstColors.SelectedItem is PaintColorModel color)
            {
                _selectedColor = color;
                LoadColorIntoEditor(color);
                SendColorTo3D(color);
            }
        }

        private void TxtColorId_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi || _selectedColor == null) return;
            _selectedColor.DisplayName = TxtColorId.Text;
        }

        private void LoadColorIntoEditor(PaintColorModel color)
        {
            _isUpdatingUi = true;
            PnlColorEditor.IsEnabled = true;

            TxtColorId.Text = color.DisplayName;
            ChkIsMetallic.IsChecked = color.IsMetallic;

            SldR.Value = color.R;
            SldG.Value = color.G;
            SldB.Value = color.B;

            SldSpecR.Value = color.SpecR;
            SldSpecG.Value = color.SpecG;
            SldSpecB.Value = color.SpecB;

            UpdateColorPreview();
            _isUpdatingUi = false;
        }

        private void ColorSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _selectedColor == null) return;

            _selectedColor.R = (byte)SldR.Value;
            _selectedColor.G = (byte)SldG.Value;
            _selectedColor.B = (byte)SldB.Value;

            _selectedColor.SpecR = (byte)SldSpecR.Value;
            _selectedColor.SpecG = (byte)SldSpecG.Value;
            _selectedColor.SpecB = (byte)SldSpecB.Value;

            _selectedColor.IsMetallic = ChkIsMetallic.IsChecked == true;

            UpdateColorPreview();
            SendColorTo3D(_selectedColor);
        }

        private void UpdateColorPreview()
        {
            if (_selectedColor == null) return;

            LblR.Text = ((int)SldR.Value).ToString();
            LblG.Text = ((int)SldG.Value).ToString();
            LblB.Text = ((int)SldB.Value).ToString();

            LblSpecR.Text = ((int)SldSpecR.Value).ToString();
            LblSpecG.Text = ((int)SldSpecG.Value).ToString();
            LblSpecB.Text = ((int)SldSpecB.Value).ToString();

            var color = Color.FromRgb(_selectedColor.R, _selectedColor.G, _selectedColor.B);
            BrdColorPreview.Background = new SolidColorBrush(color);
            if (TxtColorHexPreview != null)
            {
                TxtColorHexPreview.Text = _selectedColor.HexColor;
            }
        }

        private string GetSelectedColorTarget()
        {
            if (CmbColorTarget?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return tag.ToUpperInvariant();
            }
            return "ALL";
        }

        private static IEnumerable<string> ResolveTargetSlots(string target)
        {
            return target switch
            {
                "BOTH_PAULDRONS" => new[] { "LEFT_PAULDRON", "RIGHT_PAULDRON" },
                "BOTH_ARMS" => new[] { "LEFT_GAUNTLET", "RIGHT_GAUNTLET" },
                "BOTH_LEGS" => new[] { "LEFT_GREAVE", "RIGHT_GREAVE" },
                _ => new[] { target }
            };
        }

        private void CmbColorTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || !IsLoaded) return;
            string target = GetSelectedColorTarget();
            string label = (CmbColorTarget.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? target;
            TxtStatus.Text = $"Coloring Target set to: {label}";
        }

        private void BtnResetPartColor_Click(object sender, RoutedEventArgs e)
        {
            if (Wv3DViewport?.CoreWebView2 == null) return;
            string target = GetSelectedColorTarget();
            if (target == "ALL")
            {
                var clearAllMsg = JsonSerializer.Serialize(new { action = "clearAllSlotColors" });
                Wv3DViewport.CoreWebView2.PostWebMessageAsJson(clearAllMsg);
                TxtStatus.Text = "Reset all armor parts back to uniform global color.";
            }
            else
            {
                var slots = ResolveTargetSlots(target);
                foreach (var slot in slots)
                {
                    var clearSlotMsg = JsonSerializer.Serialize(new { action = "clearSlotColor", slot = slot });
                    Wv3DViewport.CoreWebView2.PostWebMessageAsJson(clearSlotMsg);
                }
                string label = (CmbColorTarget.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? target;
                TxtStatus.Text = $"Reset {label} back to global color.";
            }
        }

        private void SendColorTo3D(PaintColorModel color)
        {
            if (Wv3DViewport?.CoreWebView2 == null) return;
            try
            {
                string target = GetSelectedColorTarget();

                // Keep viewer's active brush paint swatch in sync
                string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                var paintSwatchMsg = JsonSerializer.Serialize(new { action = "setPaintColor", hex = hex });
                Wv3DViewport.CoreWebView2.PostWebMessageAsJson(paintSwatchMsg);

                if (target == "ALL")
                {
                    var colorMsg = JsonSerializer.Serialize(new
                    {
                        action = "setColor",
                        r = color.R,
                        g = color.G,
                        b = color.B,
                        isMetallic = color.IsMetallic,
                        specR = color.SpecR,
                        specG = color.SpecG,
                        specB = color.SpecB
                    });
                    Wv3DViewport.CoreWebView2.PostWebMessageAsJson(colorMsg);
                }
                else
                {
                    var slots = ResolveTargetSlots(target);
                    foreach (var slot in slots)
                    {
                        var slotColorMsg = JsonSerializer.Serialize(new
                        {
                            action = "setSlotColor",
                            slot = slot,
                            r = color.R,
                            g = color.G,
                            b = color.B,
                            isMetallic = color.IsMetallic,
                            specR = color.SpecR,
                            specG = color.SpecG,
                            specB = color.SpecB
                        });
                        Wv3DViewport.CoreWebView2.PostWebMessageAsJson(slotColorMsg);
                    }
                }
            }
            catch
            {
                string script = $"window.setArmorColor && window.setArmorColor({color.R}, {color.G}, {color.B}, {(color.IsMetallic ? "true" : "false")}, {color.SpecR}, {color.SpecG}, {color.SpecB});";
                Wv3DViewport.CoreWebView2.ExecuteScriptAsync(script);
            }
        }

        private void BtnAddCustomColor_Click(object sender, RoutedEventArgs e)
        {
            int index = _scanner.Colors.Count(c => c.Id.StartsWith("Custom_Color_")) + 1;
            var newCol = new PaintColorModel
            {
                Id = $"Custom_Color_{index}",
                DisplayName = $"Custom Color {index}",
                R = 180,
                G = 140,
                B = 40,
                A = 255,
                SpecR = 255,
                SpecG = 220,
                SpecB = 100,
                SpecA = 255,
                IsMetallic = true
            };

            _scanner.Colors.Insert(0, newCol);
            FilterColors(newCol);
            LstColors.ScrollIntoView(newCol);
        }

        #endregion

        #region Decals & Patterns

        private void PopulateDecalsTab()
        {
            GridDecals.ItemsSource = _scanner.Decals;
        }

        private void PopulatePatternsTab()
        {
            GridPatterns.ItemsSource = _scanner.Patterns;
        }

        private void GridDecals_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridDecals.SelectedItem is DecalModel decal)
            {
                SendDecalTo3D(decal.Section, decal.DisplayName, "#FFFFFF");
            }
        }

        private void SendDecalTo3D(string slot, string badgeName, string hex)
        {
            if (Wv3DViewport?.CoreWebView2 == null) return;
            string script = $"window.setDecalBadge && window.setDecalBadge('{slot}', '{badgeName}', '{hex}');";
            Wv3DViewport.CoreWebView2.ExecuteScriptAsync(script);
        }

        #endregion

        #region 3D Model Importer & Buttons

        private void BtnImportModel_Click(object sender, RoutedEventArgs e)
        {
            string targetSlot = string.IsNullOrWhiteSpace(_activeArmorSlotKey) ? "HELMET" : _activeArmorSlotKey;
            if (_importer.PromptAndLoadCustomModel(targetSlot, out var result) && result != null)
            {
                if (Wv3DViewport?.CoreWebView2 != null)
                {
                    try
                    {
                        var modelMsg = JsonSerializer.Serialize(new
                        {
                            action = "loadCustomModel",
                            slot = result.SlotName,
                            format = result.Format,
                            fileName = result.FileName,
                            base64 = result.Base64Content,
                            text = result.TextContent
                        });
                        Wv3DViewport.CoreWebView2.PostWebMessageAsJson(modelMsg);
                    }
                    catch
                    {
                        string escaped = JsonSerializer.Serialize(result.TextContent ?? result.Base64Content ?? string.Empty);
                        string script = $"window.loadCustomModel && window.loadCustomModel('{result.SlotName}', '{result.Format}', {escaped}, '{result.FileName}');";
                        Wv3DViewport.CoreWebView2.ExecuteScriptAsync(script);
                    }
                    MessageBox.Show($"Custom 3D Mesh '{result.FileName}' ({result.Format.ToUpperInvariant()}) successfully imported and attached to [{result.SlotName}] in the 3D studio!", "Model Imported", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnTogglePanel_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabs.Visibility == Visibility.Visible)
            {
                MainTabs.Visibility = Visibility.Collapsed;
                ColCustomizer.Width = new GridLength(0);
                ColCustomizer.MinWidth = 0;
                ColSplitter.Width = new GridLength(0);
                BtnTogglePanel.Content = "[► Show Customizer]";
            }
            else
            {
                MainTabs.Visibility = Visibility.Visible;
                ColCustomizer.Width = new GridLength(46, GridUnitType.Star);
                ColCustomizer.MinWidth = 420;
                ColSplitter.Width = new GridLength(6);
                BtnTogglePanel.Content = "[◄ Fullscreen 3D]";
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Space Marine 2 Installation Folder",
                InitialDirectory = Directory.Exists(TxtGamePath.Text) ? TxtGamePath.Text : @"C:\Program Files (x86)\Steam\steamapps\common"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtGamePath.Text = dialog.FolderName;
                LoadGameDirectory(dialog.FolderName);
            }
        }

        private void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
        {
            var detected = GameWorkspace.TryAutoDetect();
            if (!string.IsNullOrEmpty(detected))
            {
                TxtGamePath.Text = detected;
                LoadGameDirectory(detected);
            }
            else
            {
                MessageBox.Show("Could not automatically locate Space Marine 2. Please click Browse to select your installation folder.", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtGamePath.Text))
            {
                LoadGameDirectory(TxtGamePath.Text);
            }
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Space Marine 2 Preset (*.json)|*.json",
                FileName = "MyAstartesCustomization.json",
                Title = "Save Marine Customization Preset"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var proj = new CustomizerProject
                    {
                        Presets = _scanner.Presets,
                        CustomColors = _scanner.Colors.Where(c => c.Id.StartsWith("Custom_Color_")).ToList(),
                        CustomDecals = _scanner.Decals,
                        UnlockCrossClassSlots = ChkUnlockCrossClass.IsChecked == true,
                        UnlockAllColorsAndDecals = ChkUnlockAllColors.IsChecked == true,
                        UnlockAllPatterns = ChkUnlockAllPatterns.IsChecked == true
                    };

                    var json = JsonSerializer.Serialize(proj, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Customization preset successfully saved to file!", "Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Space Marine 2 Preset (*.json)|*.json",
                Title = "Load Marine Customization Preset"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var proj = JsonSerializer.Deserialize<CustomizerProject>(json);
                    if (proj != null)
                    {
                        ChkUnlockCrossClass.IsChecked = proj.UnlockCrossClassSlots;
                        ChkUnlockAllColors.IsChecked = proj.UnlockAllColorsAndDecals;
                        ChkUnlockAllPatterns.IsChecked = proj.UnlockAllPatterns;

                        foreach (var savedPreset in proj.Presets)
                        {
                            var existing = _scanner.Presets.FirstOrDefault(p =>
                                p.Team == savedPreset.Team && p.ClassKey == savedPreset.ClassKey && p.PresetKey == savedPreset.PresetKey);
                            if (existing != null)
                            {
                                foreach (var kvp in savedPreset.SectionPieces)
                                {
                                    existing.SectionPieces[kvp.Key] = kvp.Value;
                                }
                            }
                        }

                        foreach (var col in proj.CustomColors)
                        {
                            if (!_scanner.Colors.Exists(c => c.Id == col.Id))
                            {
                                _scanner.Colors.Insert(0, col);
                            }
                        }

                        RefreshArmorSlots();
                        FilterColors();
                        MessageBox.Show("Preset successfully loaded!", "Preset Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}
