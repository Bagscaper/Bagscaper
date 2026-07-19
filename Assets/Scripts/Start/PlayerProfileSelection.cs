using System;

public static class PlayerProfileSelection
{
    public enum Gender
    {
        Male,
        Female
    }

    public enum AgeGroup
    {
        Child,
        Teen,
        Age20To30,
        Age40To50,
        Age60Plus
    }

    public readonly struct Profile
    {
        public Profile(Gender gender, AgeGroup ageGroup)
        {
            Gender = gender;
            AgeGroup = ageGroup;
        }

        public Gender Gender { get; }
        public AgeGroup AgeGroup { get; }
        public string GenderApiValue => ToApiValue(Gender);
        public string AgeGroupApiValue => ToApiValue(AgeGroup);
    }

    private static bool hasSelection;
    private static Profile selection;

    public static bool HasSelection => hasSelection;

    public static void Set(Gender gender, AgeGroup ageGroup)
    {
        selection = new Profile(gender, ageGroup);
        hasSelection = true;
    }

    public static bool TryGet(out Profile profile)
    {
        profile = selection;
        return hasSelection;
    }

    public static void Clear()
    {
        selection = default;
        hasSelection = false;
    }

    public static string ToApiValue(Gender gender)
    {
        return gender switch
        {
            Gender.Male => "male",
            Gender.Female => "female",
            _ => throw new ArgumentOutOfRangeException(nameof(gender), gender, null)
        };
    }

    public static string ToApiValue(AgeGroup ageGroup)
    {
        return ageGroup switch
        {
            AgeGroup.Child => "child",
            AgeGroup.Teen => "teen",
            AgeGroup.Age20To30 => "age_20_30",
            AgeGroup.Age40To50 => "age_40_50",
            AgeGroup.Age60Plus => "age_60_plus",
            _ => throw new ArgumentOutOfRangeException(nameof(ageGroup), ageGroup, null)
        };
    }
}
