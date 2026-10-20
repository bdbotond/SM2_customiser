using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SM2Customizer.Models
{
    public class PaintColorModel : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _displayName = string.Empty;
        private byte _r = 255;
        private byte _g = 255;
        private byte _b = 255;
        private byte _a = 255;
        private byte _patternR = 255;
        private byte _patternG = 255;
        private byte _patternB = 255;
        private byte _patternA = 255;
        private byte _specR = 255;
        private byte _specG = 255;
        private byte _specB = 255;
        private byte _specA = 255;
        private bool _isMetallic = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string DisplayName
        {
            get => _displayName;
            set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
        }

        // Primary tint
        public byte R
        {
            get => _r;
            set { if (_r != value) { _r = value; OnPropertyChanged(); OnPropertyChanged(nameof(HexColor)); } }
        }

        public byte G
        {
            get => _g;
            set { if (_g != value) { _g = value; OnPropertyChanged(); OnPropertyChanged(nameof(HexColor)); } }
        }

        public byte B
        {
            get => _b;
            set { if (_b != value) { _b = value; OnPropertyChanged(); OnPropertyChanged(nameof(HexColor)); } }
        }

        public byte A
        {
            get => _a;
            set { if (_a != value) { _a = value; OnPropertyChanged(); } }
        }

        // Pattern tint
        public byte PatternR
        {
            get => _patternR;
            set { if (_patternR != value) { _patternR = value; } }
        }

        public byte PatternG
        {
            get => _patternG;
            set { if (_patternG != value) { _patternG = value; } }
        }

        public byte PatternB
        {
            get => _patternB;
            set { if (_patternB != value) { _patternB = value; } }
        }

        public byte PatternA
        {
            get => _patternA;
            set { if (_patternA != value) { _patternA = value; } }
        }

        // Specular / Metalness tint
        public byte SpecR
        {
            get => _specR;
            set { if (_specR != value) { _specR = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpecHexColor)); } }
        }

        public byte SpecG
        {
            get => _specG;
            set { if (_specG != value) { _specG = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpecHexColor)); } }
        }

        public byte SpecB
        {
            get => _specB;
            set { if (_specB != value) { _specB = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpecHexColor)); } }
        }

        public byte SpecA
        {
            get => _specA;
            set { if (_specA != value) { _specA = value; } }
        }

        // Metallic flag
        public bool IsMetallic
        {
            get => _isMetallic;
            set { if (_isMetallic != value) { _isMetallic = value; OnPropertyChanged(); } }
        }

        public string HexColor => $"#{R:X2}{G:X2}{B:X2}";
        public string SpecHexColor => $"#{SpecR:X2}{SpecG:X2}{SpecB:X2}";

        public PaintColorModel Clone()
        {
            return new PaintColorModel
            {
                Id = this.Id,
                DisplayName = this.DisplayName,
                R = this.R,
                G = this.G,
                B = this.B,
                A = this.A,
                PatternR = this.PatternR,
                PatternG = this.PatternG,
                PatternB = this.PatternB,
                PatternA = this.PatternA,
                SpecR = this.SpecR,
                SpecG = this.SpecG,
                SpecB = this.SpecB,
                SpecA = this.SpecA,
                IsMetallic = this.IsMetallic
            };
        }

        public override string ToString() => DisplayName;
    }
}
