namespace Htn.Domain.Organization;

public enum PromotionError
{
    None,
    Inactive,
    UnknownLevel,
    SameLevel,
    SkipsAStep,
    ReasonRequired,
}

public static class PromotionErrors
{
    public static string Message(PromotionError error, PersonLevel from, PersonLevel to) => error switch
    {
        PromotionError.Inactive => "This person is deactivated.",
        PromotionError.UnknownLevel => "Unknown level.",
        PromotionError.SameLevel => $"This person is already {to}.",
        PromotionError.SkipsAStep =>
            $"{from} can't go straight to {to}. Promotions go one step at a time: Member → Apprentice → Goon.",
        PromotionError.ReasonRequired => "Give a reason for moving this person down a level.",
        _ => "",
    };
}
