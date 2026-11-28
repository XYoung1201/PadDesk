"use strict";
// PadDesk configuration UI. Talks to the local service; closing this window never stops the mapping.

const $ = (s, el = document) => el.querySelector(s);
const STEP = new Set(["up", "down", "left", "right"]);
const TARGETS = [
  ["none", "未分配"], ["mouse_left", "鼠标左键"], ["mouse_right", "鼠标右键"], ["mouse_middle", "鼠标中键"],
  ["keyboard", "键盘按键"], ["media", "媒体键"], ["sequence", "按键序列 / HOG 热键"],
  ["cec", "投影仪 / 电视（HDMI-CEC）"],
  ["up", "鼠标步进 · 上"], ["down", "鼠标步进 · 下"], ["left", "鼠标步进 · 左"], ["right", "鼠标步进 · 右"],
];
const CEC = [["on", "开机"], ["standby", "待机"], ["toggle", "开关切换（先查询状态）"]];
const MODS = [[29, "Ctrl"], [56, "Alt"], [42, "Shift"], [125, "Super"]];
const AXIS_NAMES = { 0: "ABS_X 左摇杆 X", 1: "ABS_Y 左摇杆 Y", 2: "ABS_Z", 3: "ABS_RX 右摇杆 X", 4: "ABS_RY 右摇杆 Y", 5: "ABS_RZ", 16: "ABS_HAT0X", 17: "ABS_HAT0Y" };

// KeyboardEvent.code -> Linux key code
const CODEMAP = (() => {
  const m = { Escape: 1, Minus: 12, Equal: 13, Backspace: 14, Tab: 15, BracketLeft: 26, BracketRight: 27, Enter: 28,
    ControlLeft: 29, Semicolon: 39, Quote: 40, Backquote: 41, ShiftLeft: 42, Backslash: 43, Comma: 51, Period: 52, Slash: 53,
    ShiftRight: 54, NumpadMultiply: 55, AltLeft: 56, Space: 57, CapsLock: 58, NumLock: 69, ScrollLock: 70,
    Numpad7: 71, Numpad8: 72, Numpad9: 73, NumpadSubtract: 74, Numpad4: 75, Numpad5: 76, Numpad6: 77, NumpadAdd: 78,
    Numpad1: 79, Numpad2: 80, Numpad3: 81, Numpad0: 82, NumpadDecimal: 83, F11: 87, F12: 88, NumpadEnter: 96,
    ControlRight: 97, NumpadDivide: 98, PrintScreen: 99, AltRight: 100, Home: 102, ArrowUp: 103, PageUp: 104,
    ArrowLeft: 105, ArrowRight: 106, End: 107, ArrowDown: 108, PageDown: 109, Insert: 110, Delete: 111,
    Pause: 119, MetaLeft: 125, MetaRight: 126, OSLeft: 125, OSRight: 126, ContextMenu: 127 };
  "QWERTYUIOP".split("").forEach((c, i) => m["Key" + c] = 16 + i);
  "ASDFGHJKL".split("").forEach((c, i) => m["Key" + c] = 30 + i);
  "ZXCVBNM".split("").forEach((c, i) => m["Key" + c] = 44 + i);
  for (let i = 1; i <= 9; i++) m["Digit" + i] = 1 + i;
  m.Digit0 = 11;
  for (let i = 1; i <= 10; i++) m["F" + i] = 58 + i;
  for (let i = 13; i <= 24; i++) m["F" + i] = 170 + i;
  return m;
})();

// Controller drawing: each control lists the source ids it represents (decimal evdev codes).
const PAD = [
  { ids: ["key:312", "axis:2:+", "axis:10:+"], rect: [120, 18, 90, 24], label: "L2" },
  { ids: ["key:313", "axis:5:+", "axis:9:+"], rect: [390, 18, 90, 24], label: "R2" },
  { ids: ["key:310"], rect: [125, 48, 84, 16], label: "L1" },
  { ids: ["key:311"], rect: [391, 48, 84, 16], label: "R1" },
  { ids: ["key:544", "axis:17:-"], rect: [138, 116, 26, 32] },
  { ids: ["key:545", "axis:17:+"], rect: [138, 174, 26, 32] },
  { ids: ["key:546", "axis:16:-"], rect: [106, 148, 32, 26] },
  { ids: ["key:547", "axis:16:+"], rect: [164, 148, 32, 26] },
  { ids: ["key:307"], circle: [450, 120, 18], face: "n" },
  { ids: ["key:304"], circle: [450, 200, 18], face: "s" },
  { ids: ["key:308"], circle: [410, 160, 18], face: "w" },
  { ids: ["key:305"], circle: [490, 160, 18], face: "e" },
  { ids: ["key:314"], rect: [240, 128, 30, 14], small: "Select" },
  { ids: ["key:315"], rect: [330, 128, 30, 14], small: "Start" },
  { ids: ["key:316"], circle: [300, 178, 14], small: "Home" },
  { ids: ["key:317"], circle: [222, 238, 22], stick: [0, 1] },
  { ids: ["key:318"], circle: [378, 238, 22], stick: [3, 4] },
];
const FACE = { xbox: { n: "Y", s: "A", w: "X", e: "B" }, nintendo: { n: "X", s: "B", w: "Y", e: "A" } };

const S = {
  state: null, prof: { pad: null, remote: null }, rev: { pad: -1, remote: -1 }, padId: null,
  sel: { pad: null, remote: null }, tab: "pad", keys: null, hog: null, settings: null,
  learnSeen: null, recording: null, els: [],
};

async function api(method, path, body) {
  const r = await fetch(path, { method, headers: { "X-PadDesk": "1", "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body) });
  const d = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(d.error || ("HTTP " + r.status));
  return d;
}
function footer(text) { $("#footer").textContent = text || ""; }
function h(tag, attrs = {}, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === "class") el.className = v; else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
    else if (v === true) el.setAttribute(k, ""); else if (v !== false && v != null) el.setAttribute(k, v);
  }
  for (const k of kids.flat()) if (k != null) el.append(k.nodeType ? k : document.createTextNode(k));
  return el;
}
const physical = m => m.display || m.name || m.caption;
const clone = o => JSON.parse(JSON.stringify(o));

// ---- profiles ------------------------------------------------------------------------------

async function loadProfile(scope) {
  try {
    const d = await api("GET", "/api/profile?scope=" + scope);
    S.prof[scope] = d.profile; S.rev[scope] = d.rev;
    if (scope === "pad") S.padId = d.profile.device_id;
  } catch (e) {
    S.prof[scope] = null; S.rev[scope] = -1; if (scope === "pad") S.padId = null;
  }
  const list = S.prof[scope] ? S.prof[scope].mappings : [];
  if (!list.some(m => m.id === S.sel[scope])) S.sel[scope] = list.length ? list[0].id : null;
  renderList(scope); renderEditor(scope);
  if (scope === "pad") { renderPad(); renderMotion(); }
}

async function saveProfile(scope, mutate, okText) {
  const p = clone(S.prof[scope]);
  mutate(p);
  try {
    const d = await api("PUT", "/api/profile", { scope, rev: S.rev[scope], profile: p });
    S.prof[scope] = d.profile; S.rev[scope] = d.rev;
    renderList(scope); renderEditor(scope);
    if (scope === "pad") { renderPad(); renderMotion(); }
    footer(okText || "已保存");
    return true;
  } catch (e) {
    footer("保存失败：" + e.message);
    await loadProfile(scope);
    return false;
  }
}

// ---- mapping list and editor -------------------------------------------------------------

function renderList(scope) {
  const box = $("#list-" + scope), keep = box.scrollTop;
  // re-rendering after a save must not jump back to the top: hold the height while rebuilding
  const hold = h("div", { style: "height:" + box.scrollHeight + "px" });
  box.textContent = ""; box.append(hold);
  const p = S.prof[scope];
  const count = $(scope === "pad" ? "#padCount" : "#remoteCount");
  if (!p) { hold.remove(); box.append(h("p", { class: "muted" }, "没有连接手柄。连接后会自动出现，蓝牙手柄请先在系统蓝牙设置中配对。")); count.textContent = ""; return; }
  if (!p.mappings.length) box.append(h("p", { class: "muted" }, scope === "remote"
    ? "点击“学习按键”，再按遥控器、蓝牙遥控器、翻页笔或电视遥控器（HDMI-CEC）上的键，把它加入列表后设置输出。"
    : "这个手柄没有报告任何按键。"));
  for (const m of p.mappings) {
    const st = scope === "remote" ? (m.target === "none" ? "未设置" : "已设置") : m.locked ? "固定" : m.confirmed ? "已对准" : "待确认";
    box.append(h("div", { class: "item" + (m.id === S.sel[scope] ? " sel" : ""), "data-id": m.id, onclick: () => select(scope, m.id),
      ondblclick: () => { select(scope, m.id); quickMap(scope, m.id); } },
      h("div", {}, h("div", { class: "nm" }, physical(m)), h("div", { class: "raw" }, m.caption)),
      h("div", {}, h("div", { class: "out" }, m.output_label), h("div", { class: "st" + (st === "已对准" || st === "已设置" ? " ok" : "") }, st))));
  }
  if (scope === "pad") count.textContent = p.mappings.filter(m => m.confirmed && !m.locked).length + " / " + p.mappings.length + " 已对准";
  else count.textContent = p.mappings.filter(m => m.target !== "none").length + " / " + p.mappings.length + " 已设置";
  $("#removeBtn").disabled = !(scope === "remote" && S.sel.remote);
  hold.remove(); box.scrollTop = keep;
}

function select(scope, id) {
  S.sel[scope] = id;
  for (const el of document.querySelectorAll("#list-" + scope + " .item")) el.classList.toggle("sel", el.dataset.id === id);
  renderEditor(scope);
  if (scope === "pad") renderPad();
  if (scope === "remote") $("#removeBtn").disabled = !id;
}

function renderEditor(scope) {
  const box = $("#editor-" + scope); box.textContent = "";
  const p = S.prof[scope];
  const m = p && p.mappings.find(x => x.id === S.sel[scope]);
  if (!m) { box.hidden = true; return; }
  box.hidden = false;
  const name = h("input", { value: m.name ? physical(m) : "", placeholder: m.caption, maxlength: 40 });
  const target = h("select", {}, TARGETS.filter(([v]) => scope === "pad" || !STEP.has(v)).map(([v, t]) => h("option", { value: v, selected: v === m.target }, t)));
  const extra = h("div", { class: "editor-extra" });
  const fields = {};
  const drawExtra = () => {
    extra.textContent = "";
    const t = target.value;
    if (t === "keyboard") {
      const held = new Set(m.target === "keyboard" ? m.keys : []);
      fields.mods = MODS.map(([c, n]) => { const cb = h("input", { type: "checkbox", value: c, checked: held.has(c) }); return [c, cb, h("label", {}, cb, n)]; });
      const main = (m.target === "keyboard" ? m.keys : []).find(k => !MODS.some(([c]) => c === k));
      fields.key = h("select", {}, h("option", { value: "" }, "（只按修饰键）"),
        (S.keys ? S.keys.keys : []).filter(k => !MODS.some(([c]) => c === k.code)).map(k => h("option", { value: k.code, selected: k.code === main }, k.name)));
      const rec = h("button", { class: "ghost", type: "button", onclick: () => record(rec, fields) }, "录入按键");
      extra.append(h("div", { class: "chips" }, fields.mods.map(x => x[2])), h("div", { class: "row" }, fields.key, rec),
        h("p", { class: "muted" }, "按住修饰键再点按另一个映射键可组成 Alt+Tab 等组合；Super+Tab 等被桌面拦截的组合请用勾选方式设置。"));
    } else if (t === "media") {
      fields.media = h("select", {}, (S.keys ? S.keys.media : []).map(k => h("option", { value: k.code, selected: m.keys[0] === k.code }, k.name)));
      extra.append(fields.media, h("p", { class: "muted" }, "音量键长按时按“移动设置”中的延迟和间隔连续调节。"));
    } else if (t === "sequence") {
      fields.mode = h("select", {}, h("option", { value: "hog", selected: m.sequence_mode === "hog" }, "HOG 热键（字母序列）"),
        h("option", { value: "keys", selected: m.sequence_mode === "keys" }, "通用按键序列"));
      fields.text = h("input", { value: m.target === "sequence" ? m.sequence_text : "", placeholder: "JFQR 或 *JFYX" });
      const help = h("p", { class: "muted" });
      const hogPick = h("select", { onchange: () => { if (hogPick.value) { fields.mode.value = "hog"; fields.text.value = hogPick.value; sync(); } } });
      const sync = () => {
        const hog = fields.mode.value === "hog";
        fields.text.placeholder = hog ? "JFQR 或 *JFYX" : "Super+Tab 或 Ctrl+C Ctrl+V";
        help.textContent = hog ? "按下手柄键时依次点按这些字母（每步约 16 ms）；* 和 + 分别点按左、右 Ctrl，与 HOG 的前缀一致。"
          : "用空格分隔步骤，用 + 连接同时按下的键，例如 Super+Ctrl+Right、Ctrl+C Ctrl+V。";
        hogPick.hidden = !hog || !S.hog || !S.hog.entries.length;
      };
      fields.mode.onchange = sync;
      hogPick.append(h("option", { value: "" }, "从 HOG 配置选择…"), ...(S.hog ? S.hog.entries : []).map(e => h("option", { value: e.hotkey }, e.hotkey + "   ·   " + e.action)));
      extra.append(h("div", { class: "row" }, fields.mode, fields.text), hogPick, help);
      sync();
    } else if (t === "cec") {
      fields.cec = h("select", {}, CEC.map(([v, n]) => h("option", { value: v, selected: m.cec_action === v }, n)));
      extra.append(fields.cec, h("p", { class: "muted" }, "按下时通过 HDMI-CEC 让投影仪开机或待机。设备在“设置”里选择，默认 /dev/cec1（HDMI1）。"));
    } else if (STEP.has(t)) {
      extra.append(h("p", { class: "muted" }, "按住时按“移动设置”里的步长移动鼠标，长按连续移动。"));
    }
  };
  target.onchange = drawExtra;
  drawExtra();
  const apply = h("button", { onclick: () => applyEdit(scope, m.id, name.value.trim(), target.value, fields) }, "应用");
  const quick = h("button", { class: "ghost", title: "5 秒内按下键盘按键或鼠标按键，自动设为这个键的输出（也可双击列表中的键）",
    onclick: () => quickMap(scope, m.id) }, "快速映射");
  box.append(h("div", { class: "title" }, h("b", {}, physical(m)), h("span", {}, m.caption)),
    h("div", { class: "row" }, name, target), extra, h("div", { class: "row" }, h("span", { class: "muted" }, m.locked ? "方向键默认用于步进移动，也可以改成其他输出。" : ""), quick, apply));
}

function applyEdit(scope, id, name, target, f) {
  let keysOut = [], seq = null, cec = "";
  if (target === "keyboard") {
    keysOut = f.mods.filter(x => x[1].checked).map(x => x[0]);
    if (f.key.value) keysOut.push(+f.key.value);
    if (!keysOut.length) { footer("请选择按键或至少一个修饰键。"); return; }
  } else if (target === "media") keysOut = [+f.media.value];
  else if (target === "sequence") {
    seq = { mode: f.mode.value, text: f.text.value.trim() };
    if (!seq.text) { footer("请输入按键序列。"); return; }
  } else if (target === "cec") cec = f.cec.value;
  saveProfile(scope, p => {
    const m = p.mappings.find(x => x.id === id);
    m.name = name; m.target = target; m.keys = keysOut; m.confirmed = true;
    m.locked = STEP.has(target) && m.locked;
    if (seq) { m.sequence_mode = seq.mode; m.sequence_text = seq.text; }
    m.cec_action = cec;
  }, "已应用到 " + (name || id));
}

function record(btn, f) {
  if (S.recording) return stopRecording();
  api("POST", "/api/suspend", { on: true }).catch(() => {});
  btn.textContent = "请按键…"; btn.classList.add("recording");
  const onKey = e => {
    e.preventDefault(); e.stopPropagation();
    const code = CODEMAP[e.code];
    const modsHeld = { 29: e.ctrlKey, 56: e.altKey, 42: e.shiftKey, 125: e.metaKey };
    if (!code) return;
    if (MODS.some(([c]) => c === code) || code === 97 || code === 100 || code === 54 || code === 126) {
      for (const [c, cb] of f.mods) cb.checked = !!modsHeld[c];
      return;
    }
    for (const [c, cb] of f.mods) cb.checked = !!modsHeld[c];
    f.key.value = String(code);
    stopRecording();
  };
  const timer = setTimeout(stopRecording, 10000);
  window.addEventListener("keydown", onKey, true);
  S.recording = () => { clearTimeout(timer); window.removeEventListener("keydown", onKey, true); btn.textContent = "录入按键"; btn.classList.remove("recording"); };
}
function stopRecording() {
  if (!S.recording) return;
  S.recording(); S.recording = null;
  api("POST", "/api/suspend", { on: false }).catch(() => {});
}

// ---- quick map: the next keyboard key or mouse button within 5 seconds becomes the output ---------

// KeyboardEvent.code of media / browser keys -> Linux key code (all offered by the "媒体键" output)
const MEDIAMAP = { AudioVolumeUp: 115, AudioVolumeDown: 114, AudioVolumeMute: 113, MediaPlayPause: 164,
  MediaTrackNext: 163, MediaTrackPrevious: 165, MediaStop: 166, BrowserBack: 158, BrowserForward: 159,
  BrowserRefresh: 173, BrowserHome: 172 };
const MODKEYS = new Set([29, 97, 42, 54, 56, 100, 125, 126]);
const QUICK_SECONDS = 5;

function quickMap(scope, id) {
  const p = S.prof[scope], m = p && p.mappings.find(x => x.id === id);
  if (!m || S.quick) return;
  stopRecording();
  api("POST", "/api/suspend", { on: true }).catch(() => {});
  const ov = $("#quickOverlay"), cancel = $("#quickCancel");
  $("#quickTitle").textContent = "为“" + physical(m) + "”快速映射";
  let left = QUICK_SECONDS, pendingMods = null;
  const tick = () => { $("#quickText").textContent = "请按下一个键盘按键（可带 Ctrl / Alt / Shift / Super）或鼠标按键 · 剩余 " + left + " 秒"; };
  tick(); ov.hidden = false;
  const heldMods = e => [[29, e.ctrlKey], [56, e.altKey], [42, e.shiftKey], [125, e.metaKey]].filter(x => x[1]).map(x => x[0]);
  const done = (result) => {
    finish();
    if (!result) { footer("未检测到按键，映射没有改变。"); return; }
    saveProfile(scope, q => {
      const x = q.mappings.find(y => y.id === id); if (!x) return;
      x.target = result.target; x.keys = result.keys || []; x.confirmed = true; x.locked = false;
    }, "已映射：" + physical(m) + " → " + result.label);
  };
  const onKeyDown = e => {
    e.preventDefault(); e.stopPropagation();
    if (e.repeat) return;
    if (MEDIAMAP[e.code]) {
      const c = MEDIAMAP[e.code], k = S.keys && S.keys.media.find(x => x.code === c);
      return done({ target: "media", keys: [c], label: k ? k.name : e.code });
    }
    const code = CODEMAP[e.code];
    if (!code) return;
    if (MODKEYS.has(code)) { pendingMods = [...new Set([...heldMods(e), code])]; return; }
    const keys = [...heldMods(e).filter(c => c !== code), code];
    done({ target: "keyboard", keys, label: keyLabel(keys) });
  };
  const onKeyUp = e => {
    e.preventDefault(); e.stopPropagation();
    // a modifier pressed and released on its own maps to that modifier (or the modifiers held together)
    if (pendingMods && MODKEYS.has(CODEMAP[e.code])) done({ target: "keyboard", keys: pendingMods, label: keyLabel(pendingMods) });
  };
  const onMouse = e => {
    if (e.target === cancel) return;
    e.preventDefault(); e.stopPropagation();
    const r = { 0: ["mouse_left", "鼠标左键"], 1: ["mouse_middle", "鼠标中键"], 2: ["mouse_right", "鼠标右键"] }[e.button];
    if (r) return done({ target: r[0], label: r[1] });
    if (e.button === 3) return done({ target: "media", keys: [158], label: "浏览器后退" });
    if (e.button === 4) return done({ target: "media", keys: [159], label: "浏览器前进" });
  };
  const block = e => { if (e.target !== cancel) { e.preventDefault(); e.stopPropagation(); } };
  const timer = setInterval(() => { left -= 1; if (left <= 0) done(null); else tick(); }, 1000);
  const listeners = [["keydown", onKeyDown], ["keyup", onKeyUp], ["mousedown", onMouse], ["contextmenu", block],
    ["auxclick", block], ["click", block], ["mouseup", block]];
  for (const [t, f] of listeners) window.addEventListener(t, f, true);
  const finish = () => {
    clearInterval(timer);
    for (const [t, f] of listeners) window.removeEventListener(t, f, true);
    ov.hidden = true; S.quick = null;
    api("POST", "/api/suspend", { on: false }).catch(() => {});
  };
  S.quick = finish;
  cancel.onclick = () => { finish(); footer("已取消快速映射。"); };
}

function keyLabel(codes) {
  const name = c => { const k = S.keys && S.keys.keys.find(x => x.code === c); return k ? k.name : "KEY " + c; };
  return codes.map(name).join(" + ");
}

// ---- controller drawing ------------------------------------------------------------------

function buildPad() {
  const g = $("#dynamic"), NS = "http://www.w3.org/2000/svg";
  const mk = (tag, attrs) => { const e = document.createElementNS(NS, tag); for (const k in attrs) e.setAttribute(k, attrs[k]); return e; };
  for (const c of PAD) {
    if (c.stick) g.append(mk("circle", { class: "well", cx: c.circle[0], cy: c.circle[1], r: 36 }));
    const el = c.rect ? mk("rect", { class: "ctl", x: c.rect[0], y: c.rect[1], width: c.rect[2], height: c.rect[3], rx: 7 })
      : mk("circle", { class: "ctl", cx: c.circle[0], cy: c.circle[1], r: c.circle[2] });
    el.addEventListener("click", () => {
      const p = S.prof.pad; if (!p) return;
      const id = c.ids.find(i => p.mappings.some(m => m.id === i));
      if (id) { showTab("pad"); select("pad", id); const row = $(`#list-pad .item[data-id="${id}"]`); if (row) row.scrollIntoView({ block: "center" }); }
      else footer("这个手柄没有报告对应的按键，可用“按键定位”找到它。");
    });
    g.append(el);
    let text = null;
    if (c.label || c.face) {
      const [x, y] = c.rect ? [c.rect[0] + c.rect[2] / 2, c.rect[1] + c.rect[3] / 2] : c.circle;
      text = mk("text", { x, y }); text.textContent = c.label || ""; g.append(text);
    } else if (c.small) {
      const [x, y] = c.rect ? [c.rect[0] + c.rect[2] / 2, c.rect[1] + c.rect[3] + 12] : [c.circle[0], c.circle[1] + 26];
      text = mk("text", { x, y, class: "small" }); text.textContent = c.small; g.append(text);
    }
    S.els.push({ c, el, text, home: c.circle ? [c.circle[0], c.circle[1]] : null });
  }
}

function renderPad() {
  const p = S.prof.pad, layout = p ? p.visual_layout : "xbox";
  for (const b of document.querySelectorAll("#layoutSeg button")) b.classList.toggle("on", b.dataset.layout === layout);
  const ids = new Set(p ? p.mappings.map(m => m.id) : []);
  for (const { c, el, text } of S.els) {
    if (c.face && text) text.textContent = FACE[layout][c.face];
    el.classList.toggle("mapped", c.ids.some(i => ids.has(i)));
    el.classList.toggle("sel", c.ids.includes(S.sel.pad));
  }
  $("#padName").textContent = p ? p.device_name : "等待手柄";
}

function livePad(st) {
  const active = new Set(st.active);
  for (const { c, el, text, home } of S.els) {
    el.classList.toggle("on", c.ids.some(i => active.has(i)));
    if (c.stick) {
      const x = +(st.axes[c.stick[0]] || 0), y = +(st.axes[c.stick[1]] || 0);
      el.setAttribute("cx", home[0] + x * 14); el.setAttribute("cy", home[1] + y * 14);
    }
  }
  for (const scope of ["pad", "remote"]) for (const row of document.querySelectorAll("#list-" + scope + " .item")) row.classList.toggle("on", active.has(row.dataset.id));
  $("#stickVal").textContent = st.stick.map(v => v.toFixed(2)).join(", ");
  const names = [];
  for (const scope of ["pad", "remote"]) if (S.prof[scope]) for (const m of S.prof[scope].mappings) if (active.has(m.id)) names.push(physical(m));
  $("#pressed").textContent = names.length ? names.join("、") : "—";
}

// ---- motion settings ---------------------------------------------------------------------

function renderMotion() {
  const f = $("#motionForm"), p = S.prof.pad;
  for (const el of f.elements) el.disabled = !p;
  if (!p) return;
  const axes = Object.keys(S.state ? S.state.axes : {}).map(Number);
  for (const n of ["stick_x", "stick_y"]) {
    const sel = f.elements[n]; sel.textContent = "";
    const opts = [...new Set([...axes, p[n]])].sort((a, b) => a - b);
    for (const a of opts) sel.append(h("option", { value: a, selected: a === p[n] }, AXIS_NAMES[a] || "ABS " + a));
  }
  f.elements.stick_enabled.checked = p.stick_enabled;
  f.elements.repeat_enabled.checked = p.repeat_enabled;
  f.elements.invert_x.checked = p.sign_x < 0;
  f.elements.invert_y.checked = p.sign_y < 0;
  for (const n of ["speed", "deadzone", "curve", "h_step", "v_step", "repeat_delay_ms", "repeat_interval_ms"]) f.elements[n].value = p[n];
  for (const r of f.querySelectorAll("input[type=range]")) r.nextElementSibling.textContent = r.value;
}

// ---- state polling -----------------------------------------------------------------------

async function poll() {
  if (S.polling) return;
  S.polling = true;
  try { await pollOnce(); } finally { S.polling = false; }
}

async function pollOnce() {
  let st;
  try { st = await api("GET", "/api/state"); }
  catch (e) {
    $("#status").textContent = "服务未运行"; $("#status").className = "pill warn";
    footer("无法连接后台服务。请执行 sudo systemctl start paddesk"); return;
  }
  const first = !S.state;
  S.state = st;
  const pill = $("#status");
  pill.textContent = st.status;
  pill.className = "pill " + (st.status === "正在控制" || st.status === "遥控待命" ? "ok" : st.status === "已暂停" || st.status === "校准 / 编辑中" ? "warn" : "");
  $("#pauseBtn").textContent = st.paused ? "恢复" : "暂停";
  const banner = $("#banner"); banner.hidden = !st.output_error; banner.textContent = st.output_error || "";
  if (st.message && st.message !== S.lastMessage) { footer(st.message); S.lastMessage = st.message; }
  const sel = $("#padSelect");
  const sig = st.pads.map(p => p.id).join("|") + "#" + (st.pad ? st.pad.id : "");
  if (sel.dataset.sig !== sig) {
    sel.dataset.sig = sig; sel.textContent = "";
    if (!st.pads.length) sel.append(h("option", {}, "未连接手柄"));
    for (const p of st.pads) sel.append(h("option", { value: p.id, selected: st.pad && p.id === st.pad.id }, p.name));
    sel.disabled = st.pads.length < 2;
  }
  const padId = st.pad ? st.pad.id : null;
  if (first || padId !== S.padId || (padId && st.revs.pad !== S.rev.pad)) await loadProfile("pad");
  if (first || st.revs.remote !== S.rev.remote) await loadProfile("remote");
  if (first) renderDevices();
  // learning overlay and result
  const ov = $("#learnOverlay");
  ov.hidden = !st.learning;
  if (st.learning) {
    $("#learnTitle").textContent = st.learning.scope === "pad" ? "请按下要定位的手柄键" : "请按下遥控器或外设上的键";
    $("#learnText").textContent = (st.learning.scope === "pad" ? "先完全松开所有按键、扳机和摇杆。" : "支持红外接收器、蓝牙遥控器、翻页笔、键盘、电视遥控器（HDMI-CEC）。") + " 剩余 " + st.learning.remaining + " 秒";
  }
  const lr = JSON.stringify(st.learn_result);
  if (st.learn_result && lr !== S.learnSeen) {
    S.learnSeen = lr;
    const scope = st.learn_result.scope;
    await loadProfile(scope);
    showTab(scope); select(scope, st.learn_result.id);
    const row = $(`#list-${scope} .item[data-id="${st.learn_result.id}"]`); if (row) row.scrollIntoView({ block: "center" });
  } else if (first) S.learnSeen = lr;
  if (S.tab === "settings") renderDevices();
  livePad(st);
}

function renderDevices() {
  const box = $("#deviceList"), st = S.state;
  if (!st || !S.settings) return;
  const sig = JSON.stringify(st.key_devices) + JSON.stringify(S.settings.grab);
  if (box.dataset.sig === sig) return;
  box.dataset.sig = sig; box.textContent = "";
  if (!st.key_devices.length) box.append(h("p", { class: "muted" }, "没有发现按键外设。"));
  for (const d of st.key_devices) {
    const cb = h("input", { type: "checkbox", checked: !!S.settings.grab[d.id], onchange: async () => {
      const grab = Object.assign({}, S.settings.grab); if (cb.checked) grab[d.id] = true; else delete grab[d.id];
      try { S.settings = await api("POST", "/api/settings", { grab }); footer(cb.checked ? "已独占 " + d.name : "已取消独占 " + d.name); }
      catch (e) { footer(e.message); }
    } });
    box.append(h("div", { class: "devrow" }, h("div", {}, d.name, h("small", {}, d.id)), h("label", { class: "check" }, cb, " 独占")));
  }
}

function showTab(tab) {
  S.tab = tab;
  for (const b of document.querySelectorAll("#tabs button")) b.classList.toggle("on", b.dataset.tab === tab);
  for (const t of ["pad", "remote", "motion", "settings"]) $("#tab-" + t).hidden = t !== tab;
  if (tab === "settings") renderDevices();
  if (tab === "motion") renderMotion();
}

// ---- wiring ------------------------------------------------------------------------------

async function init() {
  buildPad();
  for (const b of document.querySelectorAll("#tabs button")) b.onclick = () => showTab(b.dataset.tab);
  $("#pauseBtn").onclick = () => api("POST", "/api/pause", { paused: !(S.state && S.state.paused) }).catch(e => footer(e.message));
  $("#padSelect").onchange = e => api("POST", "/api/select", { device_id: e.target.value }).catch(err => footer(err.message));
  $("#locateBtn").onclick = () => api("POST", "/api/learn", { scope: "pad" }).catch(e => footer(e.message));
  $("#learnBtn").onclick = () => api("POST", "/api/learn", { scope: "remote" }).catch(e => footer(e.message));
  $("#learnCancel").onclick = () => api("POST", "/api/learn/cancel", {}).catch(() => {});
  $("#resetBtn").onclick = async () => {
    if (!confirm("恢复这个手柄的默认配置？已设置的输出会被清除。")) return;
    try { await api("POST", "/api/reset", {}); footer("已恢复默认"); } catch (e) { footer(e.message); }
  };
  $("#removeBtn").onclick = () => {
    const id = S.sel.remote; if (!id) return;
    saveProfile("remote", p => { p.mappings = p.mappings.filter(m => m.id !== id); }, "已删除");
  };
  for (const b of document.querySelectorAll("#layoutSeg button")) b.onclick = () => {
    if (S.prof.pad) saveProfile("pad", p => { p.visual_layout = b.dataset.layout; }, "布局已切换");
  };
  const f = $("#motionForm");
  for (const r of f.querySelectorAll("input[type=range]")) r.oninput = () => { r.nextElementSibling.textContent = r.value; };
  f.onsubmit = e => {
    e.preventDefault();
    const v = n => f.elements[n].value;
    if (v("stick_x") === v("stick_y")) { footer("水平轴和垂直轴不能相同。"); return; }
    saveProfile("pad", p => {
      p.stick_enabled = f.elements.stick_enabled.checked; p.repeat_enabled = f.elements.repeat_enabled.checked;
      p.sign_x = f.elements.invert_x.checked ? -1 : 1; p.sign_y = f.elements.invert_y.checked ? -1 : 1;
      p.stick_x = +v("stick_x"); p.stick_y = +v("stick_y");
      for (const n of ["speed", "deadzone", "curve"]) p[n] = +v(n);
      for (const n of ["h_step", "v_step", "repeat_delay_ms", "repeat_interval_ms"]) p[n] = Math.round(+v(n));
    }, "移动设置已保存");
  };
  $("#centerBtn").onclick = () => api("POST", "/api/center", {}).then(() => footer("已记录摇杆中心")).catch(e => footer(e.message));
  $("#hogSave").onclick = async () => {
    try { S.settings = await api("POST", "/api/settings", { hog_config: $("#hogPath").value }); await loadHog(); footer("HOG 配置路径已保存"); }
    catch (e) { footer(e.message); }
  };
  const cecDo = async (action, label) => {
    $("#cecInfo").textContent = label + "…";
    try {
      const r = action === "status" ? await api("GET", "/api/cec") : await api("POST", "/api/cec", { action });
      $("#cecInfo").textContent = r.message;
    } catch (e) { $("#cecInfo").textContent = e.message; }
  };
  $("#cecSave").onclick = async () => {
    try { S.settings = await api("POST", "/api/settings", { cec_device: $("#cecDevice").value }); $("#cecDevice").value = S.settings.cec_device; footer("CEC 设备已设为 " + S.settings.cec_device); }
    catch (e) { footer(e.message); }
  };
  $("#cecOn").onclick = () => cecDo("on", "正在开机");
  $("#cecStandby").onclick = () => cecDo("standby", "正在待机");
  $("#cecQuery").onclick = () => cecDo("status", "正在查询");
  try { S.keys = await api("GET", "/api/keys"); S.settings = await api("GET", "/api/settings"); } catch (e) { /* service down: poll reports it */ }
  if (S.settings) { $("#hogPath").value = S.settings.hog_config; $("#cecDevice").value = S.settings.cec_device || "/dev/cec1"; }
  await loadHog();
  await poll();
  if (S.state) $("#version").textContent = "PadDesk " + S.state.version;
  setInterval(poll, 120);
}

async function loadHog() {
  try {
    S.hog = await api("GET", "/api/hog");
    $("#hogInfo").textContent = S.hog.path ? "已读取 " + S.hog.path + "，共 " + S.hog.entries.length + " 个热键。" : "未找到 HOG 配置文件。";
  } catch (e) { S.hog = null; }
}

init();
