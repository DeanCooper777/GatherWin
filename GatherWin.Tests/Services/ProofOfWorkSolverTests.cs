using System.Security.Cryptography;
using System.Text;
using GatherWin.Services;

namespace GatherWin.Tests.Services;

public class ProofOfWorkSolverTests
{
    [Fact]
    public void Solve_ReturnsValidNonce()
    {
        var challenge = "test-challenge-123";
        var difficulty = 8; // 1 leading zero byte — fast to solve

        var (returnedChallenge, nonce) = ProofOfWorkSolver.Solve(challenge, difficulty);

        Assert.Equal(challenge, returnedChallenge);

        // Verify the hash actually has the required leading zero bits
        var input = $"{challenge}:{nonce}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        Assert.True(ProofOfWorkSolver.HasLeadingZeroBits(hash, difficulty));
    }

    [Fact]
    public void Solve_DifficultyZero_ReturnsImmediately()
    {
        var (challenge, nonce) = ProofOfWorkSolver.Solve("any-challenge", 0);

        Assert.Equal("any-challenge", challenge);
        Assert.Equal("0", nonce); // First nonce should work with difficulty 0
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0xFF }, 16, true)]
    [InlineData(new byte[] { 0x00, 0x00, 0xFF }, 17, false)]
    [InlineData(new byte[] { 0x00, 0x0F, 0xFF }, 12, true)]
    [InlineData(new byte[] { 0x00, 0x0F, 0xFF }, 13, false)]
    [InlineData(new byte[] { 0xFF, 0x00, 0x00 }, 1, false)]
    [InlineData(new byte[] { 0x00 }, 8, true)]
    [InlineData(new byte[] { 0x01 }, 7, true)]
    [InlineData(new byte[] { 0x01 }, 8, false)]
    public void HasLeadingZeroBits_VariousDifficulties(byte[] hash, int difficulty, bool expected)
    {
        Assert.Equal(expected, ProofOfWorkSolver.HasLeadingZeroBits(hash, difficulty));
    }
}
