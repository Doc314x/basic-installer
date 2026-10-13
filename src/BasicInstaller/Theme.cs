using System.Drawing;

namespace BasicInstaller
{
    /// <summary>
    /// Zentrale Farbpalette, aus dem AS-Computertechnik-Logo extrahiert
    /// (dominanter Nicht-Weiß-Ton: #FF914D). Bewusst sparsam eingesetzt - ein
    /// Akzentton fürs Primäre (Installieren-Button, Tabellenkopf, Links),
    /// nicht die ganze Oberfläche einfärben.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0xFF, 0x91, 0x4D);
        public static readonly Color AccentDark = Color.FromArgb(0xE0, 0x72, 0x30); // Hover/Pressed
        public static readonly Color AccentLight = Color.FromArgb(0xFF, 0xEE, 0xE0); // dezente Flächen (Zeilenauswahl)
        public static readonly Color OnAccent = Color.White;
        public static readonly Color PageBackground = Color.FromArgb(0xFA, 0xFA, 0xFA);

        // Fuer Status-Badges ("X installiert" / "Y Updates verfuegbar") nach
        // dem Vorbild von Patch My PC - Gruen/Orange sind hier Status-
        // Semantik, kein Markenbezug wie der Accent-Ton.
        public static readonly Color Success = Color.FromArgb(0x2E, 0x7D, 0x32);
        public static readonly Color SuccessLight = Color.FromArgb(0xE8, 0xF5, 0xE9);
    }
}
