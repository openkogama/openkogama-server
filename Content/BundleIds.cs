using System.Security.Cryptography;
using System.Text;

namespace OpenKogama.Content;

public static class BundleIds
{
    public static string Token(string seed, int length)
    {
        string hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
        return string.Concat(Enumerable.Repeat(hex, length / hex.Length + 1))[..length];
    }

    public static byte[] Replace(byte[] data, string old, string replacement)
    {
        byte[] from = Encoding.ASCII.GetBytes(old);
        byte[] to = Encoding.ASCII.GetBytes(replacement);
        for (int at = data.AsSpan().IndexOf(from); at >= 0;)
        {
            to.CopyTo(data, at);
            int next = data.AsSpan(at + to.Length).IndexOf(from);
            at = next < 0 ? -1 : at + to.Length + next;
        }
        return data;
    }
}
