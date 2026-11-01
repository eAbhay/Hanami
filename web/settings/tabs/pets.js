// Pets tab — import Shimeji image sets, and choose which pets are out on the desktop.
import { h, svg, formatBytes, dialog, debounce, slider, segmented, toggle } from "../util.js";
import { pageHeader, card, row } from "../components/index.js";

const MAX_PETS = 8;

export function createPetsTab(ctx) {
  const importZip = h("button.btn.primary", { type: "button", onclick: () => doImport("zip") }, svg("upload"), h("span", "Import .zip"));
  const importFolder = h("button.btn", { type: "button", onclick: () => doImport("folder") }, svg("folder"), h("span", "Import folder"));
  const actions = h("div.slot-switch", importFolder, importZip);

  // ---- behaviour
  const savePets = (patch) => ctx.mutate("updateSettings", { pets: patch }).catch(() => {});
  const show = toggle(true, (v) => savePets({ enabled: v }), "Show pets");
  const climb = toggle(true, (v) => savePets({ climbWindows: v }), "Climb windows");
  const activity = segmented([{ value: "calm", label: "Calm" }, { value: "normal", label: "Normal" }, { value: "lively", label: "Lively" }],
    "normal", (v) => savePets({ activity: v }), { ariaLabel: "Activity" });
  const sendScale = debounce(() => savePets({ scale: pendingScale }), 150);
  let pendingScale = 1;
  const size = slider({ label: "Size", min: 0.5, max: 2, step: 0.05, value: 1, format: (v) => `${Math.round(v * 100)}%`,
    onInput: (v) => { pendingScale = v; sendScale(); } });

  const outList = h("div.pet-list");
  const outEmpty = h("p.field-hint", "No pets out yet. Add one from your library below.");
  const packList = h("div.pet-list");
  const empty = h("div.empty",
    h("div.empty-art", svg("sparkles")),
    h("div.empty-title", "No pet packs yet"),
    h("div.empty-text", "Pets use Shimeji image sets (shime1.png … shime46.png). Search for “<character> shimeji”, download a pack and import its folder or .zip here. Each pack's licence belongs to its author, so check its terms."));

  const el = h("section",
    pageHeader("Pets", "Little companions that live on your desktop.", actions),
    card("Behaviour", null,
      row("sparkles", "Show pets", "Turn all pets off without removing them. The tray menu can also hide them for this session.", show),
      h("div.divider"),
      size,
      h("div.field", h("label.field-label", "Activity"), activity,
        h("p.field-hint", "Calm pets sit and nap more; lively ones run around and rarely sleep.")),
      h("div.divider"),
      row(null, "Climb windows", "Stand on the tops of your windows and climb their sides. Pets never move, resize or click your windows.", climb)),
    card("Your pets", h("span.card-meta", `Up to ${MAX_PETS}`), outEmpty, outList),
    card("Pet library", h("span.card-meta", "Imported Shimeji packs"), empty, packList));

  async function doImport(source) {
    actions.querySelectorAll("button").forEach((b) => { b.disabled = true; });
    try {
      const r = await ctx.call("importPet", { source });
      if (r?.cancelled) return;
      await ctx.mutate("getState");
      const n = r.imported?.length ?? 0;
      ctx.toast(`Imported ${n} pet pack${n === 1 ? "" : "s"}${r.rejected?.length ? ` · ${r.rejected.length} skipped` : ""}`, "success");
    } catch (err) {
      ctx.toastError(err);
    } finally {
      actions.querySelectorAll("button").forEach((b) => { b.disabled = false; });
    }
  }

  const safe = (p) => p.catch(() => {});

  async function rename(pack) {
    const name = await dialog({ title: "Rename pet pack", input: { value: pack.name }, confirmLabel: "Rename" });
    if (name && name !== pack.name) safe(ctx.mutate("renamePet", { id: pack.id, name }).then(() => ctx.mutate("getState")));
  }

  async function remove(pack) {
    const ok = await dialog({ title: `Delete “${pack.name}”?`, body: "Removes the pack and every pet using it. This can't be undone.", confirmLabel: "Delete", danger: true });
    if (ok) safe(ctx.mutate("deletePet", { id: pack.id }));
  }

  const preview = (pack) => h("img.pet-thumb", { src: pack.preview, alt: "", draggable: false });
  const chipList = (c) => [c.climb && "Climbs", c.ceiling && "Ceiling", c.sit && "Sits"].filter(Boolean);
  const chips = (c) => h("span.pet-chips", ...(chipList(c).length ? chipList(c) : ["Walks"]).map((t) => h("span.chip", t)));

  function update(s) {
    const packs = s.petPacks ?? [];
    const byId = new Map(packs.map((p) => [p.id, p]));
    const cfg = s.settings.pets ?? {};
    show.set(cfg.enabled ?? true);
    climb.set(cfg.climbWindows ?? true);
    activity.set(cfg.activity ?? "normal");
    pendingScale = cfg.scale ?? 1;
    if (!sendScale.pending()) size.set(pendingScale);
    const items = cfg.items ?? [];
    outEmpty.hidden = items.length > 0;
    outList.replaceChildren(...items.map((it) => {
      const pack = byId.get(it.packId);
      return h("div.row.rule", preview(pack ?? { preview: "" }),
        h("div.row-text", h("div.row-title", pack?.name ?? "Missing pack"), pack ? chips(pack.capabilities) : h("div.row-sub", "")),
        h("div.row-actions",
          h("button.btn.subtle.small", { type: "button", title: "Drop it from above the screen", onclick: () => safe(ctx.mutate("callPet", { id: it.id })) }, svg("pointer"), h("span", "Call here")),
          h("button.btn.subtle.small", { type: "button", onclick: () => safe(ctx.mutate("removePet", { id: it.id })) }, svg("trash"), h("span", "Remove"))));
    }));

    empty.hidden = packs.length > 0;
    const full = items.length >= MAX_PETS;
    packList.replaceChildren(...packs.map((p) => h("div.row.rule", preview(p),
      h("div.row-text", h("div.row-title", p.name), h("div.row-sub", chips(p.capabilities), ` ${p.frameWidth}×${p.frameHeight} · ${formatBytes(p.sizeBytes)}`)),
      h("div.row-actions",
        h("button.btn.small", { type: "button", disabled: full, title: full ? `At most ${MAX_PETS} pets` : "Put another one on the desktop",
          onclick: () => safe(ctx.mutate("addPet", { packId: p.id })) }, svg("plus"), h("span", "Add")),
        h("button.icon-btn", { type: "button", title: "Rename", "aria-label": `Rename ${p.name}`, onclick: () => rename(p) }, svg("pencil")),
        h("button.icon-btn", { type: "button", title: "Delete", "aria-label": `Delete ${p.name}`, onclick: () => remove(p) }, svg("trash"))))));
  }

  return { el, update };
}
