using Htn.Domain.Common;

namespace Htn.Domain.Tests;

public sealed class EmailMaskTests
{
    [Theory]
    [InlineData("mika@hackthenorth.ph", "m•••@hackthenorth.ph")]
    [InlineData(" Mika@HackTheNorth.PH ", "M•••@HackTheNorth.PH")]
    [InlineData("a@b.ph", "a•••@b.ph")]
    [InlineData("@b.ph", "•••@b.ph")]
    [InlineData("no-at-sign", "n•••")]
    [InlineData("", "•••")]
    [InlineData(null, "•••")]
    public void Masks_all_but_first_character_and_domain(string? email, string expected) =>
        Assert.Equal(expected, EmailMask.Mask(email));
}
