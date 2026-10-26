using System.Text.RegularExpressions;
using Htn.Domain.Common;

namespace Htn.Domain.Tests;

public sealed partial class PublicIdGeneratorTests
{
    private readonly PublicIdGenerator _ids = new();

    [GeneratedRegex("^HTN-[23456789ABCDEFGHJKMNPQRSTVWXYZ]{4}-[23456789ABCDEFGHJKMNPQRSTVWXYZ]{4}$")]
    private static partial Regex ParticipantFormat();

    [Fact]
    public void Participant_id_has_HTN_XXXX_XXXX_format()
    {
        for (var i = 0; i < 1_000; i++)
            Assert.Matches(ParticipantFormat(), _ids.NewParticipantId());
    }

    [Fact]
    public void Alphabet_is_crockford_without_look_alikes()
    {
        Assert.Equal(30, PublicIdGenerator.Alphabet.Length);
        Assert.Equal(PublicIdGenerator.Alphabet.Length, PublicIdGenerator.Alphabet.Distinct().Count());
        foreach (var banned in "01ILOU")
            Assert.DoesNotContain(banned, PublicIdGenerator.Alphabet);
    }

    [Fact]
    public void Generated_ids_use_only_the_alphabet_and_cover_all_of_it()
    {
        var seen = new HashSet<char>();
        for (var i = 0; i < 2_000; i++)
            foreach (var c in _ids.NewParticipantId()[4..].Replace("-", ""))
            {
                Assert.Contains(c, PublicIdGenerator.Alphabet);
                seen.Add(c);
            }
        Assert.Equal(PublicIdGenerator.Alphabet.Length, seen.Count);
    }

    [Fact]
    public void No_collisions_in_100k()
    {
        var set = new HashSet<string>(100_000);
        for (var i = 0; i < 100_000; i++)
            Assert.True(set.Add(_ids.NewParticipantId()), "collision");
    }

    [Fact]
    public void Codes_have_requested_length()
    {
        var code = _ids.NewCode(4);
        Assert.Equal(4, code.Length);
        Assert.All(code, c => Assert.Contains(c, PublicIdGenerator.Alphabet));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ids.NewCode(0));
    }

    [Fact]
    public void Tokens_are_url_safe_and_unique()
    {
        var a = _ids.NewToken();
        var b = _ids.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length); // 32 bytes, base64url without padding
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ids.NewToken(8));
    }
}
