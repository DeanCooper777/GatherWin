using System.Security.Cryptography;
using System.Text;

namespace GatherWin.Services;

/// <summary>
/// Solves SHA-256 hashcash proof-of-work challenges for Gather API operations.
/// </summary>
public static class ProofOfWorkSolver
{
    /// <summary>
    /// Finds a nonce such that SHA256("challenge:nonce") has at least <paramref name="difficulty"/>
    /// leading zero bits.
    /// </summary>
    public static (string challenge, string nonce) Solve(string challenge, int difficulty)
    {
        for (long n = 0; n < long.MaxValue; n++)
        {
            var input = $"{challenge}:{n}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

            if (HasLeadingZeroBits(hash, difficulty))
                return (challenge, n.ToString());
        }

        throw new InvalidOperationException("Failed to solve proof-of-work challenge");
    }

    private static bool HasLeadingZeroBits(byte[] hash, int requiredBits)
    {
        var fullBytes = requiredBits / 8;
        var remainingBits = requiredBits % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (hash[i] != 0) return false;
        }

        if (remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((hash[fullBytes] & mask) != 0) return false;
        }

        return true;
    }
}
