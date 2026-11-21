// Path of Building import, adapted from exile-leveling (MIT, HeartofPhos): web/src/state/pob/process.ts
import { inflate } from "pako";
import { Data, type RouteData } from "./engine";

const GEM_ID_REMAP: Record<string, string> = {
  "Metadata/Items/Gems/Smite": "Metadata/Items/Gems/SkillGemSmite",
  "Metadata/Items/Gems/ConsecratedPath": "Metadata/Items/Gems/SkillGemConsecratedPath",
  "Metadata/Items/Gems/VaalAncestralWarchief": "Metadata/Items/Gems/SkillGemVaalAncestralWarchief",
  "Metadata/Items/Gems/HeraldOfAgony": "Metadata/Items/Gems/SkillGemHeraldOfAgony",
  "Metadata/Items/Gems/HeraldOfPurity": "Metadata/Items/Gems/SkillGemHeraldOfPurity",
  "Metadata/Items/Gems/ScourgeArrow": "Metadata/Items/Gems/SkillGemScourgeArrow",
  "Metadata/Items/Gems/RainOfSpores": "Metadata/Items/Gems/SkillGemToxicRain",
  "Metadata/Items/Gems/SummonRelic": "Metadata/Items/Gems/SkillGemSummonRelic",
  "Metadata/Items/Gems/SkillGemNewPhaseRun": "Metadata/Items/Gems/SkillGemPhaseRun",
  "Metadata/Items/Gems/SkillGemNewArcticArmour": "Metadata/Items/Gems/SkillGemArcticArmour",
};

function mapGemId(gemId: string) {
  gemId = GEM_ID_REMAP[gemId] ?? gemId;
  gemId = Data.VaalGemLookup[gemId] ?? gemId;
  gemId = Data.AwakenedGemLookup[gemId] ?? gemId;
  return gemId;
}

const POB_COLOUR = /\^(x[a-zA-Z0-9]{6}|[0-9])/g;
const clean = (s: string) => s.replace(POB_COLOUR, "");

function decode(code: string): Document {
  let trimmed = code.trim();
  // pobb.in / pastebin links are not fetched (no network from here); only raw codes.
  trimmed = trimmed.replace(/_/g, "/").replace(/-/g, "+").replace(/\s/g, "");
  const bytes = Uint8Array.from(window.atob(trimmed), (c) => c.charCodeAt(0));
  const xml = new TextDecoder().decode(inflate(bytes));
  return new DOMParser().parseFromString(xml, "application/xml");
}

export interface GemGroup {
  title: string;
  gems: string[];
}

export interface PobBuild {
  build: RouteData.BuildData;
  requiredGems: RouteData.RequiredGem[];
  groups: GemGroup[];
  ascendancy: string | null;
  level: number | null;
}

/** Decodes a PoB export code. Throws when the code is not a valid PoB build. */
export function importPob(code: string): PobBuild {
  const doc = decode(code);
  const buildEl = doc.getElementsByTagName("Build")[0];
  if (!buildEl) throw new Error("Không đọc được build PoB.");
  const characterClass = buildEl.getAttribute("className") ?? "Witch";
  if (!Data.Characters[characterClass]) throw new Error(`Class không hỗ trợ: ${characterClass}`);
  const bandit = (buildEl.getAttribute("bandit") ?? "None") as RouteData.BuildData["bandit"];

  const requiredGems: RouteData.RequiredGem[] = [];
  const groups: GemGroup[] = [];
  const sets = Array.from(doc.getElementsByTagName("SkillSet"));
  const containers: { el: Element; title?: string }[] = sets.length
    ? sets.map((el) => ({ el, title: el.getAttribute("title") ?? undefined }))
    : [{ el: doc.documentElement }];

  for (const { el, title } of containers) {
    let recentLabel: string | undefined;
    for (const skill of Array.from(el.getElementsByTagName("Skill"))) {
      if (skill.getAttribute("enabled") === "false") continue;
      const label = skill.getAttribute("label") ?? undefined;
      const gems = Array.from(skill.getElementsByTagName("Gem"));
      if (gems.length === 0) recentLabel = label;
      const names: string[] = [];
      for (const gem of gems) {
        const raw = gem.getAttribute("gemId");
        if (!raw) continue;
        const id = mapGemId(raw);
        const note = clean(recentLabel || title || label || "");
        if (!requiredGems.some((g) => g.id === id)) requiredGems.push({ id, note, count: 1 });
        if (Data.Gems[id]) names.push(Data.Gems[id].name);
      }
      if (names.length) groups.push({ title: clean(title || recentLabel || label || "Default"), gems: names });
    }
  }

  const level = Number(buildEl.getAttribute("level")) || null;
  return {
    build: { characterClass, bandit: ["None", "Oak", "Kraityn", "Alira"].includes(bandit) ? bandit : "None", leagueStart: true, library: true },
    requiredGems,
    groups,
    ascendancy: buildEl.getAttribute("ascendClassName"),
    level,
  };
}
