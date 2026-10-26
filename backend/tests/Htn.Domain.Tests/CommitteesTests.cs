using Htn.Domain.Committees;
using Htn.Domain.Common;

namespace Htn.Domain.Tests;

public class CommitteeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7();

    private static Committee New(string name = "Logistics", int order = 1) => Committee.Create(EventId, name, order, Now);

    [Fact]
    public void Create_trims_the_name_and_starts_active_without_a_lead()
    {
        var committee = New("  Logistics  ");
        Assert.Equal("Logistics", committee.Name);
        Assert.True(committee.IsActive);
        Assert.Null(committee.LeadPersonId);
        Assert.Null(committee.DeactivatedAt);
        Assert.Equal(EventId, committee.EventId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string name) => Assert.Throws<DomainException>(() => New(name));

    [Fact]
    public void A_name_is_at_most_60_characters() =>
        Assert.Throws<DomainException>(() => New(new string('a', Committee.NameMaxLength + 1)));

    [Theory]
    [InlineData(-1)]
    [InlineData(Committee.MaxSortOrder + 1)]
    public void The_sort_order_has_bounds(int order) => Assert.Throws<DomainException>(() => New("Logistics", order));

    [Fact]
    public void An_event_is_required() => Assert.Throws<DomainException>(() => Committee.Create(Guid.Empty, "Logistics", 1, Now));

    [Fact]
    public void Update_reports_whether_anything_changed()
    {
        var committee = New();
        Assert.False(committee.Update("Logistics", 1));
        Assert.True(committee.Update("Logistics & Venue", 2));
        Assert.Equal("Logistics & Venue", committee.Name);
        Assert.Equal(2, committee.SortOrder);
    }

    [Fact]
    public void Deactivation_is_final_and_clears_the_lead()
    {
        var committee = New();
        var lead = Guid.CreateVersion7();
        committee.SetLead(lead);
        Assert.Equal(lead, committee.LeadPersonId);

        committee.Deactivate(Now);
        Assert.False(committee.IsActive);
        Assert.Equal(Now, committee.DeactivatedAt);
        Assert.Null(committee.LeadPersonId);

        Assert.Throws<DomainException>(() => committee.Deactivate(Now));
        Assert.Throws<DomainException>(() => committee.Update("Anything", 1));
        Assert.Throws<DomainException>(() => committee.SetLead(lead));
    }

    [Fact]
    public void SetLead_refuses_the_empty_guid() => Assert.Throws<DomainException>(() => New().SetLead(Guid.Empty));
}

public class CommitteeMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static Committee NewCommittee() => Committee.Create(Guid.CreateVersion7(), "Logistics", 1, Now);

    [Fact]
    public void Join_copies_the_event_from_the_committee_and_starts_active()
    {
        var committee = NewCommittee();
        var person = Guid.CreateVersion7();
        var member = CommitteeMember.Join(committee, person, CommitteeRole.Member, Now);

        Assert.Equal(committee.Id, member.CommitteeId);
        Assert.Equal(committee.EventId, member.EventId);
        Assert.Equal(person, member.PersonId);
        Assert.Equal(CommitteeRole.Member, member.Role);
        Assert.True(member.IsActive);
        Assert.Null(member.LeftAt);
    }

    [Fact]
    public void Nobody_joins_a_deactivated_committee()
    {
        var committee = NewCommittee();
        committee.Deactivate(Now);
        Assert.Throws<DomainException>(() => CommitteeMember.Join(committee, Guid.CreateVersion7(), CommitteeRole.Member, Now));
    }

    [Fact]
    public void Join_requires_a_person_and_a_known_role()
    {
        var committee = NewCommittee();
        Assert.Throws<DomainException>(() => CommitteeMember.Join(committee, Guid.Empty, CommitteeRole.Member, Now));
        Assert.Throws<DomainException>(() => CommitteeMember.Join(committee, Guid.CreateVersion7(), (CommitteeRole)42, Now));
    }

    [Fact]
    public void Leaving_is_a_timestamp_and_happens_once()
    {
        var member = CommitteeMember.Join(NewCommittee(), Guid.CreateVersion7(), CommitteeRole.Lead, Now);
        Assert.Throws<DomainException>(() => member.Leave(Now.AddDays(-1)));

        member.Leave(Now.AddDays(1));
        Assert.False(member.IsActive);
        Assert.Equal(Now.AddDays(1), member.LeftAt);

        Assert.Throws<DomainException>(() => member.Leave(Now.AddDays(2)));
        Assert.Throws<DomainException>(() => member.ChangeRole(CommitteeRole.Member));
    }

    [Fact]
    public void ChangeRole_reports_whether_anything_changed()
    {
        var member = CommitteeMember.Join(NewCommittee(), Guid.CreateVersion7(), CommitteeRole.Member, Now);
        Assert.False(member.ChangeRole(CommitteeRole.Member));
        Assert.True(member.ChangeRole(CommitteeRole.Lead));
        Assert.Equal(CommitteeRole.Lead, member.Role);
    }
}

public class EventVolunteerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7();

    private static EventVolunteer New(string name = "Carla Diaz", string? email = null, string? mobile = null) =>
        EventVolunteer.Create(EventId, name, email, mobile, Now);

    [Fact]
    public void Create_normalizes_the_contact_details()
    {
        var volunteer = New("  Carla Diaz ", "Carla.Diaz@Example.PH", "0917 123 4567");
        Assert.Equal("Carla Diaz", volunteer.Name);
        Assert.Equal("carla.diaz@example.ph", volunteer.Email);
        Assert.Equal("+639171234567", volunteer.Mobile);
        Assert.True(volunteer.IsActive);
        Assert.Null(volunteer.CommitteeId);
    }

    [Theory]
    [InlineData("Ana Bautista")]
    [InlineData("Ma. Niña Dela Cruz-Ñañez")]
    [InlineData("O'Brien")]
    [InlineData("Santos, Jr.")]
    public void Names_follow_the_attendee_rules(string name) => Assert.Equal(name, New(name).Name);

    [Theory]
    [InlineData("Carla 2")]
    [InlineData("<b>Carla</b>")]
    [InlineData("https://evil.example/x")]
    [InlineData("carla@example.ph")]
    [InlineData("Ma.Niña")]
    [InlineData("")]
    public void Odd_names_are_refused(string name) => Assert.Throws<DomainException>(() => New(name));

    [Fact]
    public void A_name_is_at_most_120_characters() =>
        Assert.Throws<DomainException>(() => New(new string('a', EventVolunteer.NameMaxLength + 1)));

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@example.ph")]
    public void Odd_emails_are_refused(string email) => Assert.Throws<DomainException>(() => New("Carla Diaz", email));

    [Theory]
    [InlineData("12345")]
    [InlineData("+1 415 555 0100")]
    public void Odd_mobiles_are_refused(string mobile) => Assert.Throws<DomainException>(() => New("Carla Diaz", null, mobile));

    [Fact]
    public void Blank_contact_details_become_null()
    {
        var volunteer = New("Carla Diaz", "   ", "  ");
        Assert.Null(volunteer.Email);
        Assert.Null(volunteer.Mobile);
    }

    [Fact]
    public void Update_reports_whether_anything_changed()
    {
        var volunteer = New("Carla Diaz", "carla@example.ph");
        Assert.False(volunteer.Update("Carla Diaz", "carla@example.ph", null));
        Assert.True(volunteer.Update("Carla D. Diaz", null, "09171234567"));
        Assert.Equal("Carla D. Diaz", volunteer.Name);
        Assert.Null(volunteer.Email);
        Assert.Equal("+639171234567", volunteer.Mobile);
    }

    [Fact]
    public void A_volunteer_only_joins_a_committee_of_their_own_event()
    {
        var volunteer = New();
        var ours = Committee.Create(EventId, "Logistics", 1, Now);
        var theirs = Committee.Create(Guid.CreateVersion7(), "Logistics", 1, Now);

        Assert.True(volunteer.AttachTo(ours));
        Assert.Equal(ours.Id, volunteer.CommitteeId);
        Assert.False(volunteer.AttachTo(ours));
        Assert.Throws<DomainException>(() => volunteer.AttachTo(theirs));
        Assert.True(volunteer.AttachTo(null));
        Assert.Null(volunteer.CommitteeId);
    }

    [Fact]
    public void A_deactivated_committee_takes_no_volunteers()
    {
        var committee = Committee.Create(EventId, "Logistics", 1, Now);
        committee.Deactivate(Now);
        Assert.Throws<DomainException>(() => New().AttachTo(committee));
    }

    [Fact]
    public void Deactivation_is_final_and_detaches_them()
    {
        var volunteer = New();
        volunteer.AttachTo(Committee.Create(EventId, "Logistics", 1, Now));
        volunteer.Deactivate(Now);

        Assert.False(volunteer.IsActive);
        Assert.Equal(Now, volunteer.DeactivatedAt);
        Assert.Null(volunteer.CommitteeId);
        Assert.Throws<DomainException>(() => volunteer.Deactivate(Now));
        Assert.Throws<DomainException>(() => volunteer.Update("Carla Diaz", null, null));
        Assert.Throws<DomainException>(() => volunteer.AttachTo(null));
    }
}

public class PersonNameRulesTests
{
    [Theory]
    [InlineData("Ana Bautista", true)]
    [InlineData("Ma. Niña Dela Cruz-Ñañez", true)]
    [InlineData("Santos, Jr.", true)]
    [InlineData("O’Brien", true)]
    [InlineData("Carla 2", false)]
    [InlineData("<script>", false)]
    [InlineData("a@b.ph", false)]
    [InlineData("http://x.example", false)]
    [InlineData(".Leading dot", false)]
    public void The_shared_name_rules_match_the_registration_form(string value, bool valid)
    {
        Assert.Equal(valid, PersonNameRules.IsValidName(value));
        // RegistrationForm keeps its public API and now delegates to the same rules.
        Assert.Equal(valid, Htn.Domain.Registrations.RegistrationForm.IsValidName(value));
    }
}
