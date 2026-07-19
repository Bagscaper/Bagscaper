using NUnit.Framework;

public class PlayerProfileSelectionTests
{
    [SetUp]
    public void SetUp()
    {
        PlayerProfileSelection.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        PlayerProfileSelection.Clear();
    }

    [TestCase(PlayerProfileSelection.Gender.Male, "male")]
    [TestCase(PlayerProfileSelection.Gender.Female, "female")]
    public void GenderMapsToBackendValue(PlayerProfileSelection.Gender gender, string expected)
    {
        Assert.That(PlayerProfileSelection.ToApiValue(gender), Is.EqualTo(expected));
    }

    [TestCase(PlayerProfileSelection.AgeGroup.Child, "child")]
    [TestCase(PlayerProfileSelection.AgeGroup.Teen, "teen")]
    [TestCase(PlayerProfileSelection.AgeGroup.Age20To30, "age_20_30")]
    [TestCase(PlayerProfileSelection.AgeGroup.Age40To50, "age_40_50")]
    [TestCase(PlayerProfileSelection.AgeGroup.Age60Plus, "age_60_plus")]
    public void AgeMapsToBackendValue(PlayerProfileSelection.AgeGroup age, string expected)
    {
        Assert.That(PlayerProfileSelection.ToApiValue(age), Is.EqualTo(expected));
    }

    [Test]
    public void SetAndClearControlRuntimeSelectionLifetime()
    {
        Assert.That(PlayerProfileSelection.TryGet(out _), Is.False);

        PlayerProfileSelection.Set(
            PlayerProfileSelection.Gender.Female,
            PlayerProfileSelection.AgeGroup.Age40To50);

        Assert.That(PlayerProfileSelection.TryGet(out PlayerProfileSelection.Profile profile), Is.True);
        Assert.That(profile.GenderApiValue, Is.EqualTo("female"));
        Assert.That(profile.AgeGroupApiValue, Is.EqualTo("age_40_50"));

        PlayerProfileSelection.Clear();
        Assert.That(PlayerProfileSelection.HasSelection, Is.False);
    }
}
