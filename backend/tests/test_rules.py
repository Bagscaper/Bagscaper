import pytest

from app.domain.rules import (
    PROFILES,
    WATER_MULTIPLIERS,
    profile_for,
    required_water_72h_ml,
    validate_survival_type_rules,
)
from app.repositories.catalogs import load_survival_type_catalog
from app.schemas.game import AgeGroup, DisasterType, Gender


def test_profile_and_disaster_water_calculation() -> None:
    profile = profile_for(AgeGroup.AGE_20_30, Gender.FEMALE)
    assert profile.bmr_kcal_day == 1350
    assert profile.max_carry_weight_kg == 11.0
    assert required_water_72h_ml(profile, DisasterType.WILDFIRE) == 6930


def test_other_uses_lower_carry_limit() -> None:
    profile = profile_for(AgeGroup.CHILD, Gender.OTHER)
    assert profile.reference_weight_kg == 29
    assert profile.max_carry_weight_kg == 4.2


@pytest.mark.parametrize(
    ("age_group", "gender", "expected"),
    [(age_group, gender, profile) for (age_group, gender), profile in PROFILES.items()],
)
def test_all_fifteen_profiles_are_addressable(age_group, gender, expected) -> None:
    actual = profile_for(age_group, gender)
    assert actual == expected
    assert actual.reference_weight_kg > 0
    assert actual.bmr_kcal_day > 0
    assert actual.water_coefficient > 0
    assert actual.max_carry_weight_kg > 0


@pytest.mark.parametrize(("disaster", "multiplier"), WATER_MULTIPLIERS.items())
def test_all_seven_disaster_multipliers_round_up_to_ten_ml(disaster, multiplier) -> None:
    profile = profile_for(AgeGroup.AGE_20_30, Gender.MALE)
    raw = profile.reference_weight_kg * profile.water_coefficient * 3 * multiplier
    required = required_water_72h_ml(profile, disaster)
    assert required % 10 == 0
    assert raw <= required < raw + 10


def test_survival_type_rule_catalog_integrity() -> None:
    catalog = load_survival_type_catalog()
    validate_survival_type_rules(catalog)
    with pytest.raises(ValueError, match="missing type_code"):
        validate_survival_type_rules({code: entry for code, entry in catalog.items() if code != "OWLP"})
