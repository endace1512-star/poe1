import type { GameData } from "./types.js";
import AREAS_JSON from "../data/areas.json";
import AWAKENED_GEM_LOOKUP_JSON from "../data/awakened-gem-lookup.json";
import CHARACTERS_JSON from "../data/characters.json";
import GEM_COLOURS_JSON from "../data/gem-colours.json";
import GEMS_JSON from "../data/gems.json";
import KILL_WAYPOINTS_JSON from "../data/kill-waypoints.json";
import QUESTS_JSON from "../data/quests.json";
import VAAL_GEM_LOOKUP_JSON from "../data/vaal-gem-lookup.json";

export const Data = {
  Areas: AREAS_JSON as GameData.Areas,
  AwakenedGemLookup: AWAKENED_GEM_LOOKUP_JSON as GameData.VariantGemLookup,
  Characters: CHARACTERS_JSON as GameData.Characters,
  GemColours: GEM_COLOURS_JSON as GameData.GemColours,
  Gems: GEMS_JSON as GameData.Gems,
  KillWaypoints: KILL_WAYPOINTS_JSON as GameData.KillWaypoints,
  Quests: QUESTS_JSON as unknown as GameData.Quests,
  VaalGemLookup: VAAL_GEM_LOOKUP_JSON as GameData.VariantGemLookup,
};
