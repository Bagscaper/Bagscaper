from dataclasses import dataclass
from math import ceil
from typing import Mapping

from app.schemas.game import AgeGroup, DisasterType, Gender
from app.schemas.survival import SurvivalTypeDefinition


@dataclass(frozen=True)
class Profile:
    reference_weight_kg: float
    bmr_kcal_day: int
    water_coefficient: int
    max_carry_weight_kg: float


PROFILES: dict[tuple[AgeGroup, Gender], Profile] = {
    (AgeGroup.CHILD, Gender.MALE): Profile(30, 1400, 55, 4.5),
    (AgeGroup.CHILD, Gender.FEMALE): Profile(28, 1300, 55, 4.2),
    (AgeGroup.CHILD, Gender.OTHER): Profile(29, 1350, 55, 4.2),
    (AgeGroup.TEEN, Gender.MALE): Profile(55, 1900, 40, 9.9),
    (AgeGroup.TEEN, Gender.FEMALE): Profile(50, 1650, 40, 9.0),
    (AgeGroup.TEEN, Gender.OTHER): Profile(52.5, 1775, 40, 9.0),
    (AgeGroup.AGE_20_30, Gender.MALE): Profile(70, 1650, 35, 14.0),
    (AgeGroup.AGE_20_30, Gender.FEMALE): Profile(55, 1350, 35, 11.0),
    (AgeGroup.AGE_20_30, Gender.OTHER): Profile(62.5, 1500, 35, 11.0),
    (AgeGroup.AGE_40_50, Gender.MALE): Profile(72, 1550, 30, 12.96),
    (AgeGroup.AGE_40_50, Gender.FEMALE): Profile(58, 1300, 30, 10.44),
    (AgeGroup.AGE_40_50, Gender.OTHER): Profile(65, 1425, 30, 10.44),
    (AgeGroup.AGE_60_PLUS, Gender.MALE): Profile(68, 1400, 30, 10.2),
    (AgeGroup.AGE_60_PLUS, Gender.FEMALE): Profile(55, 1200, 30, 8.25),
    (AgeGroup.AGE_60_PLUS, Gender.OTHER): Profile(61.5, 1300, 30, 8.25),
}

WATER_MULTIPLIERS = {
    DisasterType.HEATWAVE: 1.30,
    DisasterType.WILDFIRE: 1.20,
    DisasterType.FIRE: 1.15,
    DisasterType.FLOOD: 1.05,
    DisasterType.TYPHOON: 1.05,
    DisasterType.COLDWAVE: 1.05,
    DisasterType.EARTHQUAKE: 1.00,
}

DISASTER_TAGS: dict[DisasterType, tuple[set[str], set[str]]] = {
    DisasterType.FIRE: ({"burn_dressing", "eye_wash"}, {"respirator", "flashlight", "escape_tool"}),
    DisasterType.FLOOD: ({"antiseptic", "wound_dressing"}, {"water_filter", "waterproof_bag", "signal_tool"}),
    DisasterType.TYPHOON: ({"wound_dressing", "antiseptic"}, {"rain_protection", "flashlight", "radio"}),
    DisasterType.WILDFIRE: ({"burn_dressing", "eye_wash"}, {"respirator", "goggles", "escape_tool"}),
    DisasterType.EARTHQUAKE: ({"trauma_kit", "wound_dressing", "splint"}, {"flashlight", "whistle", "helmet"}),
    DisasterType.HEATWAVE: ({"oral_rehydration_salts", "first_aid_basic"}, {"cooling_pack", "shade", "ventilation"}),
    DisasterType.COLDWAVE: ({"first_aid_basic"}, {"thermal_blanket", "warming_pack", "windproof_shelter"}),
}

DISASTER_DISPLAY_NAMES: dict[DisasterType, str] = {
    DisasterType.FIRE: "화재",
    DisasterType.FLOOD: "홍수",
    DisasterType.TYPHOON: "태풍",
    DisasterType.WILDFIRE: "산불",
    DisasterType.EARTHQUAKE: "지진",
    DisasterType.HEATWAVE: "폭염",
    DisasterType.COLDWAVE: "한파",
}

SURVIVAL_TYPE_RULE_CODES = frozenset({"CRDP", "OWLP", "DRSP", "SPSP", "PRPS"})


def validate_survival_type_rules(catalog: Mapping[str, SurvivalTypeDefinition]) -> None:
    missing = SURVIVAL_TYPE_RULE_CODES - catalog.keys()
    if missing:
        raise ValueError(f"survival type rules reference missing type_code values: {', '.join(sorted(missing))}")


def profile_for(age_group: AgeGroup, gender: Gender) -> Profile:
    return PROFILES[(age_group, gender)]


def required_water_72h_ml(profile: Profile, disaster: DisasterType) -> int:
    raw = profile.reference_weight_kg * profile.water_coefficient * 3 * WATER_MULTIPLIERS[disaster]
    return ceil(raw / 10) * 10
