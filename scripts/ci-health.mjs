#!/usr/bin/env node
/**
 * Repo health checks for AI Chronicle (runs on push/PR via GitHub Actions, also locally).
 *
 * What it verifies:
 *   1. tools.json / agent_tools.json are valid JSON with required fields.
 *   2. Every tool defined in JSON has a corresponding case "name": in ToolExecutor.cs
 *      (otherwise an agent can be handed a tool that does nothing).
 *   3. CefSharp code only appears inside #if NET472 regions (net6/Xbox must not load CEF).
 *   4. Source files stay near the ~1000-line guideline (warning) and below 1500 (error).
 *   5. Required WebUI assets and core prompt files exist; prompt JSON is parseable; prompts are non-empty.
 *   6. No secrets (API keys) committed in source/docs.
 *   7. No personal machine paths / usernames leaked in tracked source/docs.
 *
 * Exit code 0 = pass, 1 = fail. Run: `node scripts/ci-health.mjs`
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const errors = [];
const warnings = [];

function readJson(rel) {
  const full = path.join(root, rel);
  if (!fs.existsSync(full)) {
    errors.push(`Missing file: ${rel}`);
    return null;
  }
  try {
    return JSON.parse(fs.readFileSync(full, "utf8"));
  } catch (e) {
    errors.push(`Invalid JSON in ${rel}: ${e.message}`);
    return null;
  }
}

function toolSwitchNames() {
  const toolsDir = path.join(root, "Tools");
  let content = "";
  for (const f of fs.readdirSync(toolsDir)) {
    if (f.startsWith("ToolExecutor") && f.endsWith(".cs"))
      content += "\n" + fs.readFileSync(path.join(toolsDir, f), "utf8");
  }
  return [...content.matchAll(/case\s+"([^"]+)":/g)].map((m) => m[1]);
}

function walk(dir, exts, out) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "node_modules" || entry.name === "bin" || entry.name === "obj") continue;
    if (entry.name === ".idea" || entry.name === ".vscode" || entry.name === ".git") continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, exts, out);
    else if (exts.some((e) => entry.name.endsWith(e))) out.push(full);
  }
}

function relative(file) {
  return path.relative(root, file).replaceAll(path.sep, "/");
}

/* ---- 1. JSON validity + required fields ---- */
const jsonDefs = ["tools.json", "agent_tools.json"].map((f) => {
  const arr = readJson(`_Module/Prompts/${f}`);
  if (arr && Array.isArray(arr)) {
    for (const tool of arr) {
      for (const field of ["name", "description", "category"]) {
        if (typeof tool[field] !== "string" || !tool[field].trim())
          errors.push(`${f}: tool ${JSON.stringify(tool.name)} missing or empty '${field}'`);
      }
      if (tool.parameters && !Array.isArray(tool.parameters))
        errors.push(`${f}: tool ${tool.name} has non-array 'parameters'`);
    }
  }
  return arr;
});

const jsonTools = jsonDefs.filter(Boolean).flat();
const jsonNames = new Set(jsonTools.map((t) => t.name));

// Tools handled outside the ToolExecutor switch (system-internal), not a drift.
const NOT_IN_SWITCH_ALLOWLIST = new Set(["update_knowledge"]);

/* ---- 2. JSON <-> ToolExecutor switch sync ---- */
const switchNames = toolSwitchNames();
for (const name of jsonNames) {
  if (!switchNames.includes(name) && !NOT_IN_SWITCH_ALLOWLIST.has(name))
    errors.push(`Tool "${name}" is defined in JSON but has no 'case "${name}":' in ToolExecutor.cs`);
}

// Extra cases not exposed via JSON are allowed, but report them (e.g. browse_tools meta-tool).
for (const name of switchNames) {
  if (!jsonNames.has(name)) warnings.push(`ToolExecutor case "${name}" has no JSON definition (informational)`);
}

/* ---- 3. C# source checks ---- */
const csFiles = [];
walk(root, [".cs"], csFiles);
let checkedSourceFiles = 0;
for (const file of csFiles) {
  const rel = relative(file);
  if (rel.startsWith("BLSource/")) continue;

  checkedSourceFiles++;
  const content = fs.readFileSync(file, "utf8");
  const lineCount = content.split(/\r?\n/).length;
  if (lineCount > 1500)
    errors.push(`Source file too large: ${rel} (${lineCount} lines, limit 1500)`);
  else if (lineCount > 1000)
    warnings.push(`Source file over ~1000-line guideline: ${rel} (${lineCount} lines)`);

  if (!content.includes("CefSharp")) continue;

  const stack = [];
  const lines = content.split(/\r?\n/);
  const isNet472Condition = (directive) => {
    const expr = directive.replace(/^#(if|elif)\s*/, "");
    return /\bNET472\b/.test(expr) && !/!\s*NET472/.test(expr);
  };
  for (let i = 0; i < lines.length; i++) {
    const trimmed = lines[i].trim();
    if (trimmed.startsWith("#if")) {
      stack.push(isNet472Condition(trimmed));
    } else if (trimmed.startsWith("#elif")) {
      if (stack.length) stack[stack.length - 1] = isNet472Condition(trimmed);
    } else if (trimmed.startsWith("#else")) {
      if (stack.length) stack[stack.length - 1] = !stack[stack.length - 1];
    } else if (trimmed.startsWith("#endif")) {
      stack.pop();
    } else if (lines[i].includes("CefSharp") &&
               !trimmed.startsWith("//") &&
               !trimmed.startsWith("*") &&
               !stack.some(Boolean)) {
      errors.push(`CefSharp outside #if NET472: ${rel}:${i + 1}`);
    }
  }
}

/* ---- 4. WebUI assets + prompt files ---- */
const requiredWebAssets = [
  "_Module/WebUI/history.html",
  "_Module/WebUI/history.css",
  "_Module/WebUI/history.js",
  "_Module/WebUI/theme.css",
  "_Module/WebUI/app.js",
];
for (const rel of requiredWebAssets) {
  if (!fs.existsSync(path.join(root, rel)))
    errors.push(`Missing WebUI asset: ${rel}`);
}

const requiredPrompts = [
  "world_info.txt",
  "game_rules.txt",
  "conversation_rules.txt",
  "letter_rules.txt",
  "historian_rules.txt",
  "yearly_chronicle_prompt.txt",
  "biography_prompt.txt",
  "special_chronicle_prompt.txt",
  "self_review_rules.txt",
];
for (const name of requiredPrompts) {
  if (!fs.existsSync(path.join(root, "_Module", "Prompts", name)))
    errors.push(`Missing prompt file: _Module/Prompts/${name}`);
}

const promptsDir = path.join(root, "_Module", "Prompts");
if (fs.existsSync(promptsDir)) {
  for (const entry of fs.readdirSync(promptsDir, { withFileTypes: true })) {
    if (!entry.isFile()) continue;
    const full = path.join(promptsDir, entry.name);
    if (entry.name.endsWith(".txt") && fs.statSync(full).size === 0)
      errors.push(`Empty prompt file: _Module/Prompts/${entry.name}`);
    if (entry.name.endsWith(".json")) {
      try {
        JSON.parse(fs.readFileSync(full, "utf8"));
      } catch (e) {
        errors.push(`Invalid JSON in _Module/Prompts/${entry.name}: ${e.message}`);
      }
    }
  }
}

/* ---- 5. Secret scan ---- */
const secretPatterns = [
  /sk-[A-Za-z0-9]{20,}/g,
  /api[_-]?key\s*[:=]\s*["'][^"']{12,}["']/gi,
  /Bearer\s+[A-Za-z0-9._-]{24,}/g,
  /password\s*[:=]\s*["'][^"']{4,}["']/gi,
];
const scanFiles = [];
walk(root, [".cs", ".json", ".md", ".txt", ".xml", ".yaml", ".yml"], scanFiles);
for (const file of scanFiles) {
  const rel = relative(file);
  if (rel.startsWith("BLSource") || rel.startsWith(".git")) continue;
  const content = fs.readFileSync(file, "utf8");
  for (const pattern of secretPatterns) {
    pattern.lastIndex = 0;
    const m = pattern.exec(content);
    if (m) {
      errors.push(`Possible secret in ${rel}: ${m[0].slice(0, 24)}...`);
      break;
    }
  }
}

/* ---- 6. Personal path / username scan ---- */
for (const file of scanFiles) {
  const rel = relative(file);
  if (rel.startsWith("BLSource") || rel.startsWith(".git")) continue;
  const content = fs.readFileSync(file, "utf8");
  if (/C:\\Users\\[A-Za-z0-9_]+/.test(content))
    errors.push(`Personal machine path in ${rel}: contains C:\\Users\\<name>`);
  if (/yangui/i.test(content) && !rel.includes("AGENTS") && !rel.includes("CLAUDE"))
    errors.push(`Personal username in ${rel}`);
}

/* ---- Report ---- */
console.log("=== AI Chronicle repo health check ===\n");
console.log(`tools.json tools:       ${jsonDefs[0]?.length ?? "?"}`);
console.log(`agent_tools.json tools: ${jsonDefs[1]?.length ?? "?"}`);
console.log(`ToolExecutor cases:     ${switchNames.length}`);
console.log(`source files checked:   ${checkedSourceFiles}`);
console.log(`scanned files:          ${scanFiles.length}`);
console.log(`warnings:               ${warnings.length}`);
console.log("");

if (warnings.length) {
  console.log("Warnings:");
  for (const w of warnings) console.log(`  ! ${w}`);
  console.log("");
}
if (errors.length) {
  console.log("FAILED:");
  for (const e of errors) console.log(`  ✗ ${e}`);
  console.log("");
  process.exit(1);
}
console.log("All checks passed.");
