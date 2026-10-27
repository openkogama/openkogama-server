using System.Globalization;

namespace OpenKogama.Api;

public readonly record struct ThemeColor(float R, float G, float B, float A = 1f)
{
    public static ThemeColor FromHex(string hex)
    {
        string digits = hex.TrimStart('#');
        if (digits.Length is not (6 or 8) || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            throw new FormatException($"{hex} is not a #rrggbb or #rrggbbaa color");
        if (digits.Length == 6) value = value << 8 | 0xff;
        return new ThemeColor((value >> 24) / 255f, (value >> 16 & 0xff) / 255f, (value >> 8 & 0xff) / 255f, (value & 0xff) / 255f);
    }

    public string ToHex() => $"#{Byte(R):x2}{Byte(G):x2}{Byte(B):x2}{Byte(A):x2}";

    static int Byte(float channel) => (int)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);
}
