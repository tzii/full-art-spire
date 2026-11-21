// Renders _build/press.html into the press and social images with headless Chrome.
//
//   node _build/render_press.mjs [--chrome "<path to chrome.exe>"] [--only square|og|banner]
//
// Writes, relative to the site root:
//   press/workshop-preview.png   864x864 PNG  (Steam Workshop preview, must stay under 1,000,000 bytes)
//   assets/og-card.jpg           1200x630 JPG (Open Graph / Twitter card)
//   press/hero-1920x1080.jpg     1920x1080 JPG (Nexus header, video thumbnail)
//
// Needs Node 22+ (built-in fetch and WebSocket) and a local Chrome or Edge. Nothing is downloaded.
import { spawn } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync, existsSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const site = resolve(here, "..");
const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };

const chromeCandidates = [
  opt("--chrome"),
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
].filter(Boolean);
const chromePath = chromeCandidates.find((p) => existsSync(p));
if (!chromePath) throw new Error("No Chrome or Edge found; pass --chrome <path>.");

const jobs = [
  { layout: "square", width: 864, height: 864, out: "press/workshop-preview.png", format: "png" },
  { layout: "og", width: 1200, height: 630, out: "assets/og-card.jpg", format: "jpeg", quality: 88 },
  { layout: "banner", width: 1920, height: 1080, out: "press/hero-1920x1080.jpg", format: "jpeg", quality: 90 },
].filter((j) => !opt("--only") || j.layout === opt("--only"));

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const profile = mkdtempSync(join(tmpdir(), "fas-press-"));
const port = 9400 + Math.floor(Math.random() * 400);
const chrome = spawn(chromePath, [
  "--headless=new", `--remote-debugging-port=${port}`, "--hide-scrollbars",
  "--allow-file-access-from-files", `--user-data-dir=${profile}`, "about:blank",
], { stdio: "ignore" });

try {
  let targets = [];
  for (let i = 0; i < 60 && !targets.length; i++) {
    try { targets = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).filter((t) => t.type === "page"); } catch { /* not up yet */ }
    if (!targets.length) await sleep(200);
  }
  if (!targets.length) throw new Error("Chrome did not start its debugging endpoint.");

  const ws = new WebSocket(targets[0].webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.addEventListener("open", r); ws.addEventListener("error", j); });
  let seq = 0;
  const pending = new Map();
  ws.addEventListener("message", (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
  });
  const send = (method, params = {}) => new Promise((r) => { const id = ++seq; pending.set(id, r); ws.send(JSON.stringify({ id, method, params })); });
  const evaluate = async (expression) => (await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true })).result?.result?.value;

  await send("Page.enable");
  for (const job of jobs) {
    await send("Emulation.setDeviceMetricsOverride", { width: job.width, height: job.height, deviceScaleFactor: 1, mobile: false });
    const url = `${pathToFileURL(join(here, "press.html")).href}?layout=${job.layout}`;
    await send("Page.navigate", { url });
    await sleep(600);
    const ready = await evaluate(`(async () => {
      await document.fonts.ready;
      await Promise.all([...document.images].map((img) => img.decode().catch(() => null)));
      return document.fonts.check('900 40px "Mona Sans"') && [...document.images].every((img) => img.naturalWidth > 0);
    })()`);
    if (!ready) throw new Error(`${job.layout}: font or card images failed to load.`);
    const shot = await send("Page.captureScreenshot", { format: job.format, quality: job.quality, clip: { x: 0, y: 0, width: job.width, height: job.height, scale: 1 } });
    const outPath = join(site, job.out);
    writeFileSync(outPath, Buffer.from(shot.result.data, "base64"));
    const size = statSync(outPath).size;
    console.log(`wrote ${job.out} (${job.width}x${job.height}, ${(size / 1024).toFixed(0)} KB)`);
    if (job.layout === "square" && size >= 1_000_000) {
      console.warn("  warning: the Workshop preview is over 1,000,000 bytes; re-encode it before uploading.");
      process.exitCode = 1;
    }
  }
  ws.close();
} finally {
  chrome.kill();
  await sleep(300);
  try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome may still hold files */ }
}
