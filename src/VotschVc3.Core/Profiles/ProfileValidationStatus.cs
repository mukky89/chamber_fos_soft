namespace VotschVc3.Core.Profiles;

/// <summary>Laboratory readiness state of a saved profile.</summary>
public enum ProfileValidationStatus
{
    /// <summary>To be tested – profile must be verified before normal use.</summary>
    TBT,

    /// <summary>Work in progress – profile is still being prepared.</summary>
    WIP,

    /// <summary>Validated and ready for use.</summary>
    OK,

    /// <summary>Not OK – profile must not be used.</summary>
    NOK,
}

public static class ProfileValidationStatusExtensions
{
    public static string Description(this ProfileValidationStatus status) => status switch
    {
        ProfileValidationStatus.OK => "OK – všetko je v poriadku, profil sa môže používať.",
        ProfileValidationStatus.NOK => "NOK – NOT OK, profil nepoužívať.",
        ProfileValidationStatus.WIP => "WIP – WORK IN PROGRESS, profil je rozpracovaný.",
        _ => "TBT – TO BE TESTED, profil sa musí otestovať.",
    };
}
