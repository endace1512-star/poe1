import { Data, type Fragments, type RouteData } from "./engine";
import { useToast } from "../components/Toast";

const icon = (name: string) => <img className="inline-icon" src={`/leveling-icons/${name}.png`} alt="" />;

const DIRECTIONS = ["↑", "↗", "→", "↘", "↓", "↙", "←", "↖"];

const LAB_LINKS: Record<string, string> = {
  normal: "https://www.poelab.com/gtgax",
  cruel: "https://www.poelab.com/r8aws",
  merciless: "https://www.poelab.com/riikv",
  eternal: "https://www.poelab.com/wfbra",
};

export function AreaName({ id }: { id: string }) {
  const area = Data.Areas[id];
  if (!area) return <span className="f-area">{id}</span>;
  return (
    <span>
      <span className="f-area">{area.name}</span>
      {area.is_town_area ? <> {icon("town")}</> : <span className="f-level">lv{area.level}</span>}
    </span>
  );
}

function Copy({ text }: { text: string }) {
  const toast = useToast();
  return (
    <span
      className="f-copy"
      title="Bấm để copy chuỗi tìm kiếm"
      onClick={(e) => {
        e.stopPropagation();
        navigator.clipboard?.writeText(text).then(() => toast(`Đã copy ${text}`));
      }}
    >
      {text}
    </span>
  );
}

export function FragmentView({ part }: { part: Fragments.AnyFragment }) {
  if (typeof part === "string") return <span className="f-generic">{part}</span>;
  switch (part.type) {
    case "kill":
      return <span className="f-enemy">{part.value}</span>;
    case "arena":
      return <span className="f-arena">{part.value}</span>;
    case "area":
    case "enter":
      return <AreaName id={part.areaId} />;
    case "logout":
      return (
        <span>
          <span className="f-generic">Logout</span> ➞ <AreaName id={part.areaId} />
        </span>
      );
    case "waypoint":
      return <span className="f-waypoint">{icon("waypoint")}Waypoint</span>;
    case "waypoint_use":
      return (
        <span>
          <span className="f-waypoint">{icon("waypoint")}Waypoint</span> ➞ <AreaName id={part.dstAreaId} />
        </span>
      );
    case "waypoint_get":
      return <span className="f-waypoint">{icon("waypoint")}Waypoint</span>;
    case "portal_set":
      return <span className="f-portal">{icon("portal")}Portal</span>;
    case "portal_use":
      return (
        <span>
          <span className="f-portal">{icon("portal")}Portal</span> ➞ <AreaName id={part.dstAreaId} />
        </span>
      );
    case "quest": {
      const quest = Data.Quests[part.questId];
      const npcs = Array.from(new Set(part.rewardOffers.map((o) => quest?.reward_offers[o]?.quest_npc).filter(Boolean)));
      return (
        <span>
          {icon("quest")}
          <span className="f-quest">{quest?.name ?? part.questId}</span>
          {npcs.length > 0 && <span className="muted"> – {npcs.join(", ")}</span>}
        </span>
      );
    }
    case "quest_text":
      return <span className="f-questtext">{part.value}</span>;
    case "generic":
      return <span className="f-generic">{part.value}</span>;
    case "reward_quest":
      return <span className="f-questtext">{part.item}</span>;
    case "reward_vendor":
      return (
        <span className="f-questtext">
          {part.item}
          {part.cost ? ` (${part.cost})` : ""}
        </span>
      );
    case "trial":
      return <span className="f-trial">{icon("trial")}Trial of Ascendancy</span>;
    case "ascend":
      return (
        <span className="f-trial">
          {icon("trial")}Ascend ({part.version}){" "}
          <a href={LAB_LINKS[part.version]} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>
            poelab
          </a>
        </span>
      );
    case "crafting":
      return (
        <span className="f-crafting">
          {icon("crafting")}Crafting: {part.crafting_recipes.join(", ")}
        </span>
      );
    case "dir":
      return <span className="f-dir">{DIRECTIONS[part.dirIndex] ?? "?"}</span>;
    case "copy":
      return <Copy text={part.text} />;
    default:
      return null;
  }
}

const ATTR_CLASS: Record<string, string> = { strength: "gem-str", dexterity: "gem-dex", intelligence: "gem-int" };

export function GemStepView({ step }: { step: RouteData.GemStep }) {
  const gem = Data.Gems[step.requiredGem.id];
  const name = gem?.name ?? step.requiredGem.id;
  const cls = ATTR_CLASS[gem?.primary_attribute ?? ""] ?? "gem-none";
  return (
    <div className="lv-line lv-gem-step">
      {step.rewardType === "quest" ? <span className="muted">Nhận thưởng:</span> : <span className="muted">Mua:</span>}
      <span className={cls}>
        {step.count > 1 ? `${step.count}× ` : ""}
        {name}
      </span>
      {step.requiredGem.note && <span className="faint small">({step.requiredGem.note})</span>}
    </div>
  );
}
