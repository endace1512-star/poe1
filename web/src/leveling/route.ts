import {
  Data,
  buildGemSteps,
  findCharacterGems,
  getRouteFiles,
  initializeRouteState,
  parseRoute,
  type RouteData,
} from "./engine";

const ROUTE_SOURCES = import.meta.glob("./data/routes/*.txt", { query: "?raw", import: "default", eager: true }) as Record<string, string>;

/** act-1.txt … act-10.txt in act order. */
function routeSources(): string[] {
  const actNumber = (path: string) => Number(/act-(\d+)/.exec(path)?.[1] ?? 0);
  return Object.keys(ROUTE_SOURCES)
    .sort((a, b) => actNumber(a) - actNumber(b))
    .map((k) => ROUTE_SOURCES[k]);
}

export const CLASSES = Object.keys(Data.Characters);
export const BANDITS: RouteData.BuildData["bandit"][] = ["None", "Oak", "Kraityn", "Alira"];

export function defaultBuild(): RouteData.BuildData {
  return { characterClass: "Witch", bandit: "None", leagueStart: true, library: true };
}

/** A step with the route edge (area transition) it belongs to. */
export interface PlacedStep {
  step: RouteData.Step;
  edge: number;
  key: string;
}

export interface PlacedSection {
  name: string;
  steps: PlacedStep[];
}

export interface BuiltRoute {
  sections: PlacedSection[];
  /** edges[i] = area id you are in after the i-th zone change; edges[0] is the start area. */
  edges: string[];
}

/** Parses the bundled route for a build and adds quest/vendor gem steps for the build's gems. */
export function buildRoute(build: RouteData.BuildData, requiredGems: RouteData.RequiredGem[], gemsOnly = false): BuiltRoute {
  const state = initializeRouteState();
  if (build.leagueStart) state.preprocessorDefinitions.add("LEAGUE_START");
  if (build.library) state.preprocessorDefinitions.add("LIBRARY");
  state.preprocessorDefinitions.add(
    { None: "BANDIT_KILL", Oak: "BANDIT_OAK", Kraityn: "BANDIT_KRAITYN", Alira: "BANDIT_ALIRA" }[build.bandit],
  );

  const quiet = console.warn;
  console.warn = () => {}; // the engine logs route lint warnings we don't need in the browser console
  let base: RouteData.Route;
  try {
    base = parseRoute(getRouteFiles(routeSources()), state);
  } finally {
    console.warn = quiet;
  }

  const questGems = new Set<number>();
  const vendorGems = new Set<number>();
  if (requiredGems.length > 0) findCharacterGems(build, requiredGems, questGems);

  const sections: PlacedSection[] = [];
  let edge = 0;
  base.sections.forEach((section, si) => {
    const placed: PlacedSection = { name: section.name, steps: [] };
    section.steps.forEach((step, i) => {
      if (step.type !== "fragment_step") return;
      if (step.edgeIndex !== null) edge = step.edgeIndex;

      const gemSteps: RouteData.GemStep[] = [];
      if (requiredGems.length > 0) {
        for (const part of step.parts) {
          if (typeof part !== "string" && part.type === "quest") {
            gemSteps.push(...buildGemSteps(part, build, requiredGems, questGems, vendorGems));
          }
        }
      }
      if (gemsOnly && gemSteps.length === 0 && step.edgeIndex === null) return;

      let shown: RouteData.FragmentStep = step;
      const search = gemSteps.map((g) => Data.Gems[g.requiredGem.id]?.name).filter(Boolean).join("|");
      if (search) shown = { ...step, parts: [...step.parts, { type: "copy", text: `"${search}"`, side: "tail" }] };

      placed.steps.push({ step: shown, edge, key: `${si}-${i}` });
      gemSteps.forEach((g, gi) => placed.steps.push({ step: g, edge, key: `${si}-${i}-g${gi}` }));
    });
    if (placed.steps.length > 0) sections.push(placed);
  });

  return { sections, edges: base.edges };
}

/**
 * Where to move when the game reports a new area. Normally the next edge; if a zone change was missed
 * (e.g. the app started late) we also accept a non-town area a few edges ahead. Never moves backwards.
 */
export function nextEdgeFor(edges: string[], active: number, areaId: string): number | null {
  if (edges[active + 1] === areaId) return active + 1;
  if (Data.Areas[areaId]?.is_town_area) return null;
  for (let k = 2; k <= 4; k++) if (edges[active + k] === areaId) return active + k;
  return null;
}
