using Htn.Domain.CheckIn;
using Htn.Domain.Common;
using Htn.Domain.Events;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;

namespace Htn.Domain.Tests;

public sealed class CheckInTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7();
    private static readonly Guid Staff = Guid.CreateVersion7();
    private static readonly PublicIdGenerator Ids = new();

    private static Registration NewRegistration(int groupSize = 1, bool paid = true)
    {
        var ticket = TicketType.Create(EventId, "Regular", 1500m, 100, groupSize, null, null, TicketVisibility.Public, 1, Now.AddDays(-10));
        var people = Enumerable.Range(0, groupSize)
            .Select(i => new AttendeeDetails($"P{i}", null, "Santos", null, $"p{i}@example.ph", null, null, null, null, null)).ToList();
        var reg = Registration.Create(EventId, ticket, people, people.Select(_ => Ids.NewParticipantId()).ToList(), null,
            RegistrationSource.Public, null, RegistrationForm.ConsentVersion, Now.AddHours(48), null, Now.AddDays(-5));
        if (paid) reg.MarkPaid(PaymentMethod.Cash, null, null, null, null, Now.AddDays(-4));
        return reg;
    }

    [Fact]
    public void First_check_in_welcomes_and_marks_attendee_and_registration()
    {
        var reg = NewRegistration();
        var a = reg.Attendees[0];
        Assert.Equal(CheckInResult.Welcome, reg.CheckIn(a.Id, "  Desk 1 ", Staff, Now));
        Assert.Equal(Now, a.CheckedInAt);
        Assert.Equal("Desk 1", a.CheckedInStation);
        Assert.Equal(Staff, a.CheckedInById);
        Assert.Equal(RegistrationStatus.CheckedIn, reg.Status);
        Assert.Equal(Now, reg.CheckedInAt);

        Assert.Equal(CheckInResult.AlreadyCheckedIn, reg.CheckIn(a.Id, "Desk 2", Staff, Now.AddMinutes(5)));
        Assert.Equal("Desk 1", a.CheckedInStation);
        Assert.Equal(Now, a.CheckedInAt);
    }

    [Fact]
    public void Group_keeps_each_attendee_flag_and_the_first_arrival_time()
    {
        var reg = NewRegistration(groupSize: 3);
        Assert.Equal(CheckInResult.Welcome, reg.CheckIn(reg.Attendees[2].Id, "Desk 1", Staff, Now));
        Assert.Equal(CheckInResult.Welcome, reg.CheckIn(reg.Attendees[0].Id, "Desk 2", Staff, Now.AddMinutes(3)));
        Assert.Equal(RegistrationStatus.CheckedIn, reg.Status);
        Assert.Equal(Now, reg.CheckedInAt);
        Assert.Null(reg.Attendees[1].CheckedInAt);
        Assert.Equal(CheckInResult.Welcome, reg.CheckInResultFor(reg.Attendees[1]));
    }

    [Fact]
    public void Unpaid_and_cancelled_change_nothing()
    {
        var pending = NewRegistration(paid: false);
        Assert.Equal(CheckInResult.NotPaid, pending.CheckIn(pending.Attendees[0].Id, "Desk 1", Staff, Now));
        Assert.Null(pending.Attendees[0].CheckedInAt);
        Assert.Equal(RegistrationStatus.PendingPayment, pending.Status);

        var cancelled = NewRegistration();
        cancelled.Cancel("Refunded", null, Now.AddDays(-1));
        Assert.Equal(CheckInResult.Cancelled, cancelled.CheckIn(cancelled.Attendees[0].Id, "Desk 1", Staff, Now));
        Assert.Null(cancelled.Attendees[0].CheckedInAt);
    }

    [Fact]
    public void Checked_in_registration_cannot_be_cancelled_or_reverted()
    {
        var reg = NewRegistration();
        reg.CheckIn(reg.Attendees[0].Id, "Desk 1", Staff, Now);
        Assert.Throws<DomainException>(() => reg.Cancel("x", null, Now));
    }

    [Fact]
    public void Another_registrations_attendee_is_refused()
    {
        var reg = NewRegistration();
        var other = NewRegistration();
        Assert.Throws<DomainException>(() => reg.CheckIn(other.Attendees[0].Id, "Desk 1", Staff, Now));
        Assert.Throws<DomainException>(() => reg.CheckInResultFor(other.Attendees[0]));
    }

    [Theory]
    [InlineData("HTN-7K3Q-9XWD", "HTN-7K3Q-9XWD")]
    [InlineData("htn-7k3q-9xwd", "HTN-7K3Q-9XWD")]
    [InlineData("HTN7K3Q9XWD", "HTN-7K3Q-9XWD")]
    [InlineData(" htn7k3q-9xwd \r", "HTN-7K3Q-9XWD")]
    public void Typed_participant_ids_are_recognized(string input, string expected)
    {
        Assert.True(ParticipantIdInput.TryNormalize(input, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HTN-7K3Q")]
    [InlineData("ABC-7K3Q-9XWD")]
    [InlineData("HTN-7K3Q-9XWD-1")]
    [InlineData("eyJ.abc")]
    [InlineData("HTN--7K3Q-9XWD")]
    public void Other_text_is_not_a_participant_id(string input) => Assert.False(ParticipantIdInput.TryNormalize(input, out _));

    [Theory]
    [InlineData("Desk 1", "Desk 1")]
    [InlineData("  Main entrance  ", "Main entrance")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Desk\n1", null)]
    public void Station_names_are_trimmed_and_bounded(string input, string? expected) => Assert.Equal(expected, CheckInStation.Normalize(input));

    [Fact]
    public void Station_name_is_at_most_40_characters()
    {
        Assert.NotNull(CheckInStation.Normalize(new string('a', 40)));
        Assert.Null(CheckInStation.Normalize(new string('a', 41)));
    }

    [Fact]
    public void Scan_log_rows_validate_their_fields()
    {
        var invalid = CheckInScan.Create(EventId, null, CheckInResult.Invalid, CheckInMethod.Qr, " Desk 1 ", Staff, Now);
        Assert.Equal("Desk 1", invalid.Station);
        Assert.Null(invalid.AttendeeId);
        Assert.Throws<DomainException>(() => CheckInScan.Create(EventId, null, CheckInResult.Welcome, CheckInMethod.Qr, "Desk", Staff, Now));
        Assert.Throws<DomainException>(() => CheckInScan.Create(EventId, null, CheckInResult.Invalid, CheckInMethod.Qr, "", Staff, Now));
        Assert.Throws<DomainException>(() => CheckInScan.Create(EventId, null, CheckInResult.Invalid, CheckInMethod.Qr, "Desk", Guid.Empty, Now));
    }

    [Fact]
    public void Member_rule_applies_to_the_member_level_only()
    {
        Assert.True(MemberRule.RequiresCheckIn(PersonLevel.Member));
        Assert.False(MemberRule.RequiresCheckIn(PersonLevel.Apprentice));
        Assert.False(MemberRule.RequiresCheckIn(PersonLevel.Goon));
        Assert.False(MemberRule.RequiresCheckIn(PersonLevel.Board));
    }

    [Fact]
    public void Created_history_can_carry_a_reason()
    {
        var p = Person.Create("Mara", null, "Cruz", "mara@example.ph", PersonLevel.Member, null, Now, null, MemberRule.ImportedReason);
        Assert.Equal("imported", Assert.Single(p.History).Reason);
        Assert.Null(Assert.Single(Person.Create("Abe", null, "Reyes", "abe@example.ph", PersonLevel.Goon, null, Now, null).History).Reason);
    }
}
