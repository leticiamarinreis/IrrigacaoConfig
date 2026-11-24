"use strict";

const $ = (sel) => document.querySelector(sel);
const state = { me: null, projectId: null, settings: null, suggestion: null, suggestionSource: null };

// ---------- helpers ----------
async function api(path, { method = "GET", body, form } = {}) {
  const opts = { method, headers: {}, credentials: "same-origin" };
  if (form) opts.body = form;
  else if (body !== undefined) { opts.headers["Content-Type"] = "application/json"; opts.body = JSON.stringify(body); }
  const res = await fetch(path, opts);
  if (res.status === 204) return null;
  const data = await res.json().catch(() => null);
  if (!res.ok) {
    const err = new Error(problemText(data, res.status));
    err.status = res.status;
    throw err;
  }
  return data;
}

function problemText(p, status) {
  if (status === 429) return "Muitas tentativas. Aguarde um minuto.";
  if (!p) return `Erro ${status}.`;
  if (p.errors) return Object.values(p.errors).flat().join("\n");
  return p.detail || p.title || `Erro ${status}.`;
}

function toast(msg) {
  const t = $("#toast");
  t.textContent = msg; t.hidden = false;
  clearTimeout(toast._t); toast._t = setTimeout(() => (t.hidden = true), 4000);
}

function el(tag, props = {}, ...children) {
  const n = document.createElement(tag);
  Object.assign(n, props);
  for (const c of children) n.append(c);
  return n;
}

async function withBusy(button, fn) {
  button.disabled = true;
  try { return await fn(); } finally { button.disabled = false; }
}

// ---------- autenticação ----------
document.querySelectorAll(".tab").forEach((tab) =>
  tab.addEventListener("click", () => {
    document.querySelectorAll(".tab").forEach((t) => t.classList.toggle("active", t === tab));
    $("#loginForm").hidden = tab.dataset.tab !== "login";
    $("#registerForm").hidden = tab.dataset.tab !== "register";
    $("#authError").textContent = "";
  }));

$("#loginForm").addEventListener("submit", (e) => {
  e.preventDefault();
  const f = new FormData(e.target);
  withBusy(e.submitter, async () => {
    try { await enterApp(await api("/api/auth/login", { method: "POST", body: Object.fromEntries(f) })); }
    catch (err) { $("#authError").textContent = err.message; }
  });
});

$("#registerForm").addEventListener("submit", (e) => {
  e.preventDefault();
  const f = new FormData(e.target);
  withBusy(e.submitter, async () => {
    try { await enterApp(await api("/api/auth/register", { method: "POST", body: Object.fromEntries(f) })); }
    catch (err) { $("#authError").textContent = err.message; }
  });
});

$("#logoutBtn").addEventListener("click", async () => {
  await api("/api/auth/logout", { method: "POST" }).catch(() => {});
  location.reload();
});

async function enterApp(me) {
  state.me = me;
  $("#authView").hidden = true;
  $("#appView").hidden = false;
  $("#userBox").hidden = false;
  $("#userName").textContent = me.name;
  renderProjectSelect(me.projects[0]);
  if (me.projects.length) await loadProject(me.projects[0]);
  else { $("#claimForm").hidden = false; }
}

function renderProjectSelect(selected) {
  const sel = $("#projectSelect");
  sel.replaceChildren(...state.me.projects.map((p) => el("option", { value: p, textContent: p, selected: p === selected })));
}

$("#projectSelect").addEventListener("change", (e) => loadProject(e.target.value));
$("#claimToggle").addEventListener("click", () => ($("#claimForm").hidden = !$("#claimForm").hidden));
$("#claimForm").addEventListener("submit", (e) => {
  e.preventDefault();
  const f = new FormData(e.target);
  withBusy(e.submitter, async () => {
    try {
      const s = await api("/api/projects/claim", { method: "POST", body: Object.fromEntries(f) });
      state.me.projects.push(s.projectId);
      renderProjectSelect(s.projectId);
      e.target.reset(); e.target.hidden = true;
      await loadProject(s.projectId);
      toast(`Projeto ${s.projectId} adicionado.`);
    } catch (err) { toast(err.message); }
  });
});

// ---------- projeto / configurações ----------
async function loadProject(projectId) {
  state.projectId = projectId;
  state.settings = await api(`/api/projects/${encodeURIComponent(projectId)}/settings`);
  discardSuggestion();
  renderSettings();
}

const TH_FIELDS = [
  ["soilMoisture", "Umidade do solo", "%"],
  ["temperature", "Temperatura", "°C"],
  ["luminosity", "Luminosidade", null],
];
const cap = (s) => s[0].toUpperCase() + s.slice(1);

/** Monta as linhas mín/máx e devolve uma função que lê os valores editados. */
function thresholdEditor(container, t, { editableUnit }) {
  container.replaceChildren(el("div", { className: "th-row th-head" },
    el("span", { textContent: "" }), el("span", { textContent: "Mínimo" }), el("span", { textContent: "Máximo" })));
  const inputs = {};
  let unitSelect = null;
  for (const [key, label, unit] of TH_FIELDS) {
    const name = el("span", { textContent: `${label} (${unit ?? t.luminosityUnit})` });
    const mk = (suffix) => (inputs[key + suffix] = el("input", {
      type: "number", step: "any", value: t[key + suffix], required: true, ariaLabel: `${label} ${suffix}`,
    }));
    container.append(el("div", { className: "th-row" }, name, mk("Min"), mk("Max")));
  }
  if (editableUnit) {
    unitSelect = el("select", {},
      el("option", { value: "%", textContent: "%", selected: t.luminosityUnit === "%" }),
      el("option", { value: "lux", textContent: "lux", selected: t.luminosityUnit === "lux" }));
    container.append(el("label", { className: "inline" }, "Unidade de luminosidade que o ESP32 envia:", unitSelect));
  }
  return () => {
    const out = { luminosityUnit: unitSelect ? unitSelect.value : t.luminosityUnit };
    for (const [key] of TH_FIELDS) for (const s of ["Min", "Max"]) out[key + s] = Number(inputs[key + s].value);
    return out;
  };
}

let readSettingsThresholds = () => ({});

function renderSettings() {
  const s = state.settings;
  $("#versionInfo").textContent = `versão ${s.version} · atualizado ${new Date(s.updatedAt).toLocaleString("pt-BR")}`;
  const f = $("#settingsForm");
  f.alertEmail.value = s.alertEmail;
  f.reservoirAlertDistanceCm.value = s.reservoirAlertDistanceCm;
  readSettingsThresholds = thresholdEditor($("#stThresholds"), s.thresholds, { editableUnit: true });

  $("#varsList").replaceChildren();
  for (const [k, v] of Object.entries(s.customVariables || {})) addVarRow(k, v.value, v.exposeToDashboard);

  const cp = $("#currentPlant");
  if (s.plant) {
    const conf = s.plant.source === "manual" ? "cadastro manual" : `IA · ${Math.round(s.plant.confidence * 100)}% de confiança`;
    cp.replaceChildren(
      el("strong", { textContent: s.plant.commonName }),
      el("em", { textContent: s.plant.scientificName || "" }),
      el("div", { className: "muted", textContent: conf }));
  } else {
    cp.replaceChildren(el("span", { className: "muted", textContent: "Nenhuma planta definida. Tire uma foto para começar." }));
  }
}

function addVarRow(key = "", value = "", expose = false) {
  const keyIn = el("input", { placeholder: "nome (ex.: telefone)", value: key, maxLength: 40 });
  const valIn = el("input", { placeholder: "valor", value, maxLength: 500 });
  const pub = el("input", { type: "checkbox", checked: expose });
  const rm = el("button", { type: "button", className: "remove", textContent: "✕", title: "Remover" });
  const row = el("div", { className: "var-row" }, keyIn, valIn, el("label", {}, pub, "pública"), rm);
  row._read = () => [keyIn.value.trim(), { value: valIn.value, exposeToDashboard: pub.checked }];
  rm.addEventListener("click", () => row.remove());
  $("#varsList").append(row);
}
$("#addVarBtn").addEventListener("click", () => addVarRow());

$("#settingsForm").addEventListener("submit", (e) => {
  e.preventDefault();
  const f = e.target;
  const customVariables = {};
  for (const row of $("#varsList").children) {
    const [k, v] = row._read();
    if (k) customVariables[k] = v;
  }
  const body = {
    alertEmail: f.alertEmail.value.trim(),
    reservoirAlertDistanceCm: Number(f.reservoirAlertDistanceCm.value),
    thresholds: readSettingsThresholds(),
    customVariables,
  };
  withBusy(e.submitter, async () => {
    try {
      const r = await api(`/api/projects/${encodeURIComponent(state.projectId)}/settings`, { method: "PUT", body });
      state.settings = r.settings; renderSettings();
      toast(`Salvo. Orion: ${r.orionSync}`);
    } catch (err) { toast(err.message); }
  });
});

// ---------- planta: foto → IA ----------
/** Reduz a foto para ~1280px JPEG antes de enviar (menos dados no celular e menos tokens no Gemini). */
async function shrinkImage(file, maxSide = 1280) {
  try {
    const bmp = await createImageBitmap(file);
    const scale = Math.min(1, maxSide / Math.max(bmp.width, bmp.height));
    const canvas = el("canvas", { width: Math.round(bmp.width * scale), height: Math.round(bmp.height * scale) });
    canvas.getContext("2d").drawImage(bmp, 0, 0, canvas.width, canvas.height);
    const blob = await new Promise((r) => canvas.toBlob(r, "image/jpeg", 0.85));
    return blob ?? file;
  } catch { return file; } // navegador não decodifica (ex.: HEIC) → envia o original
}

async function handlePhoto(file) {
  if (!file) return;
  $("#plantError").textContent = "";
  const img = await shrinkImage(file);
  const preview = $("#photoPreview");
  if (preview.src) URL.revokeObjectURL(preview.src);
  preview.src = URL.createObjectURL(img); preview.hidden = false;

  const form = new FormData();
  form.append("photo", img, "planta.jpg");
  await runAi(() => api(`/api/projects/${encodeURIComponent(state.projectId)}/plant/identify`, { method: "POST", form }), "ia-foto");
}

$("#cameraInput").addEventListener("change", (e) => { handlePhoto(e.target.files[0]); e.target.value = ""; });
$("#galleryInput").addEventListener("change", (e) => { handlePhoto(e.target.files[0]); e.target.value = ""; });

$("#nameForm").addEventListener("submit", (e) => {
  e.preventDefault();
  $("#photoPreview").hidden = true;
  suggestByName(e.target.plantName.value.trim());
});

function suggestByName(plantName) {
  return runAi(() => api(`/api/projects/${encodeURIComponent(state.projectId)}/plant/suggest`, { method: "POST", body: { plantName } }), "ia-nome");
}

async function runAi(call, source) {
  $("#aiBusy").hidden = false; $("#plantError").textContent = "";
  document.querySelectorAll(".photo-actions input, #nameForm button").forEach((b) => (b.disabled = true));
  try {
    state.suggestion = await call();
    state.suggestionSource = source;
    renderSuggestion();
  } catch (err) {
    $("#plantError").textContent = err.message;
  } finally {
    $("#aiBusy").hidden = true;
    document.querySelectorAll(".photo-actions input, #nameForm button").forEach((b) => (b.disabled = false));
  }
}

let readSuggestionThresholds = () => ({});

function renderSuggestion() {
  const s = state.suggestion;
  $("#suggestion").hidden = false;
  $("#sgName").value = s.commonName;
  $("#sgSci").value = s.scientificName;
  const pct = Math.round(s.confidence * 100);
  const bar = $("#sgConfBar");
  bar.style.width = `${pct}%`;
  bar.classList.toggle("low", s.confidence < 0.6);
  $("#sgConfText").textContent = `${pct}% de confiança`;

  $("#sgWarnings").replaceChildren(...s.warnings.map((w) => el("li", { textContent: w })));
  const alts = $("#sgAlternatives");
  alts.replaceChildren();
  if (s.alternatives.length) {
    alts.append(el("span", { textContent: "Talvez seja:" }));
    for (const a of s.alternatives) {
      const b = el("button", { type: "button", className: "chip", textContent: a });
      b.addEventListener("click", () => suggestByName(a));
      alts.append(b);
    }
  }
  $("#sgCare").textContent = s.careSummary;
  $("#sgWater").textContent = s.wateringNotes ? `💧 ${s.wateringNotes}` : "";
  readSuggestionThresholds = thresholdEditor($("#sgThresholds"), {
    soilMoistureMin: s.soilMoistureMin, soilMoistureMax: s.soilMoistureMax,
    temperatureMin: s.temperatureMin, temperatureMax: s.temperatureMax,
    luminosityMin: s.luminosityMin, luminosityMax: s.luminosityMax,
    luminosityUnit: s.luminosityUnit,
  }, { editableUnit: false });
  $("#suggestion").scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function discardSuggestion() {
  state.suggestion = null;
  $("#suggestion").hidden = true;
  $("#plantError").textContent = "";
}
$("#discardBtn").addEventListener("click", discardSuggestion);

$("#applyBtn").addEventListener("click", (e) => {
  const s = state.suggestion;
  const name = $("#sgName").value.trim();
  // Se o dono corrigiu o nome, a origem passa a ser manual (a confiança da IA não vale mais).
  const edited = name !== s.commonName;
  const body = {
    commonName: name,
    scientificName: $("#sgSci").value.trim(),
    confidence: edited ? 1 : s.confidence,
    careSummary: edited ? "" : s.careSummary,
    wateringNotes: edited ? "" : s.wateringNotes,
    source: edited ? "manual" : state.suggestionSource,
    thresholds: readSuggestionThresholds(),
  };
  withBusy(e.currentTarget, async () => {
    try {
      const r = await api(`/api/projects/${encodeURIComponent(state.projectId)}/plant/apply`, { method: "POST", body });
      state.settings = r.settings;
      discardSuggestion(); renderSettings();
      toast(`Planta aplicada. Orion: ${r.orionSync}`);
    } catch (err) { $("#plantError").textContent = err.message; }
  });
});

// ---------- início ----------
(async () => {
  try { await enterApp(await api("/api/auth/me")); }
  catch { $("#authView").hidden = false; }
})();
