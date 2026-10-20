using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SM2Customizer.Models
{
    public class ArmorPieceItem
    {
        public string Uid { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string ClassOrigin { get; set; } = string.Empty;
        public string TierBadge { get; set; } = string.Empty;
        public string TierColor { get; set; } = "#8E9AA8";
        public List<string> Meshes { get; set; } = new List<string>();

        public override string ToString() => DisplayName;
    }

    public class BodySectionSlot : INotifyPropertyChanged
    {
        private string _sectionKey = string.Empty;
        private string _displayName = string.Empty;
        private string _category = "Armor";
        private int _categoryOrder = 0;
        private string _selectedPieceUid = string.Empty;
        private List<ArmorPieceItem> _availablePieces = new List<ArmorPieceItem>();

        public string SectionKey
        {
            get => _sectionKey;
            set { if (_sectionKey != value) { _sectionKey = value; OnPropertyChanged(nameof(SectionKey)); } }
        }

        public string DisplayName
        {
            get => _displayName;
            set { if (_displayName != value) { _displayName = value; OnPropertyChanged(nameof(DisplayName)); } }
        }

        public string Category
        {
            get => _category;
            set { if (_category != value) { _category = value; OnPropertyChanged(nameof(Category)); } }
        }

        public int CategoryOrder
        {
            get => _categoryOrder;
            set { if (_categoryOrder != value) { _categoryOrder = value; OnPropertyChanged(nameof(CategoryOrder)); } }
        }

        public string SelectedPieceUid
        {
            get => _selectedPieceUid;
            set
            {
                if (_selectedPieceUid != value)
                {
                    _selectedPieceUid = value;
                    OnPropertyChanged(nameof(SelectedPieceUid));
                    OnPropertyChanged(nameof(SelectedPiece));
                }
            }
        }

        public List<ArmorPieceItem> AvailablePieces
        {
            get => _availablePieces;
            set
            {
                _availablePieces = value;
                OnPropertyChanged(nameof(AvailablePieces));
                OnPropertyChanged(nameof(SelectedPiece));
            }
        }

        public ArmorPieceItem? SelectedPiece
        {
            get => AvailablePieces.Find(p => p.Uid == SelectedPieceUid);
            set
            {
                if (value != null && SelectedPieceUid != value.Uid)
                {
                    SelectedPieceUid = value.Uid;
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public class ClassPresetModel
    {
        public string Team { get; set; } = "LOYALIST";
        public string ClassKey { get; set; } = "PVE_PVP_SOLDIER";
        public string DisplayName { get; set; } = "Tactical";
        public string PresetKey { get; set; } = "PREDEFINED_PRESET_1";

        public Dictionary<string, string> SectionPieces { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string GetClassDisplayName(string classKey)
        {
            return classKey switch
            {
                "PVE_PVP_SOLDIER" => "Tactical",
                "PVE_PVP_SUPPORT" => "Bulwark",
                "PVE_PVP_TANK" => "Heavy",
                "PVE_PVP_SNIPER" => "Sniper",
                "PVE_PVP_RAIDER" => "Vanguard",
                "PVE_PVP_BERSERK" => "Assault",
                "PVE_PVP_ENGINEER" => "Techmarine (Hidden)",
                "PVE_PVP_MEDIC" => "Apothecary (Hidden)",
                "PVE_PVP_CHARACTER_MOD_1" => "Mod Character 1",
                "PVE_PVP_CHARACTER_MOD_2" => "Mod Character 2",
                "PVE_PVP_CHARACTER_MOD_3" => "Mod Character 3",
                _ => classKey.Replace("PVE_PVP_", "")
            };
        }
    }
}
