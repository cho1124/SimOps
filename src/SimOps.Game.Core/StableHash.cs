using System.Security.Cryptography;
using System.Text;

namespace SimOps.Game.Core;

public static class StableHash
{
    public static string Sha256Hex(string canonicalValue)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(canonicalValue);
        var hash = sha256.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);

        for (var index = 0; index < hash.Length; index++)
        {
            builder.Append(hash[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
