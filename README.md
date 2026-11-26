# That's You! - PC/Mac port with a web phone controller

Scripts that rebuild the PS4 PlayLink game "That's You!" (CUSA08389) from your own backup as a native Windows x64 and macOS arm64 game, and replace the discontinued phone app with a web page that runs in any phone browser, live camera effects included.

This repository contains no game or app files. You need your own PS4 dump (base game and patch) and the Android APK of the companion app.

## Before publishing anything

- `local-patches/` holds changes made against decompiled game code. It is a separate local git repository, ignored by this one, and must be converted to build-time compiled-assembly edits (Plan 7) before anything from this repository is pushed to a public remote. The pre-push hook enforces this.
- Never commit or publish the address of your deployed phone page. Each person deploys and protects their own page.

## Setup

1. Copy `config/local.example.json` to `config/local.json` and fill in your paths.
2. Run `python3 -m pipeline.install_hooks` and `git config iwyk.privateUrl <your page host>`.
3. Run the tests: `python3 -m unittest discover -s tests -v`

Further steps are added as the pipeline grows.

## Phone page

The phone controller is a web page (source in `web/`). Phones open it by scanning the QR code on the game's join screen and then talk to the game directly over WebRTC on your LAN. Host the page anywhere that serves static files over HTTPS and put its address into `phone_page_url` in `config/local.json`. Keep that address to yourself: it is never written to this repository.

Build. `python3 -m pipeline.build_web` copies `web/` into `work/web-dist` and adds the app's art, fonts and texts from your own app export (`work/export/app`, made by the extract step). The art only ever lands in `work/`. Without the export, or with `--no-art`, you get the neutral page, which works the same with plain styling. `--sounds` also copies the app's UI sounds.

Serve locally. `python3 -m pipeline.web_serve` serves `web/` (neutral) on port 8088; `--art` serves `work/web-dist` instead. `http://127.0.0.1` counts as a secure context, so the camera works in a desktop browser.

Deploy. Set `web_deploy` in `config/local.json`, then run `python3 -m pipeline.deploy_web` (`--build` builds first, `--dry-run` only prints the plan):

- `{"target": "cloudflare", "worker_name": "<name>", "compatibility_date": "2026-11-01"}` deploys the folder as Cloudflare static assets with wrangler (wrangler on PATH, or Node.js for npx). The full tool output goes to `work/web-deploy/deploy.log`; addresses are redacted from what is printed.
- `{"target": "folder", "path": "/absolute/folder"}` copies the page into a folder served by your own web server. Files from an earlier deploy that are gone are removed, anything else in the folder is left alone.
- `{"target": "rsync", "dest": "<user@host:/folder/>", "delete": false}` runs `rsync -a`, with `--delete` only when `delete` is true.

End-to-end test. `python3 -m pipeline.web_e2e --all` runs every scenario of the mock console (`tools/mockconsole/scenarios`) in headless Chrome with a fake camera: one browser context per phone, the page served locally, the console reached over WebRTC Direct. It takes a screenshot of every step and reports pass or fail per step in `work/reports/web-e2e.json`; screenshots and logs go to `work/e2e/<time>/`. Options: `--scenario NAME` (repeatable), `--art` (test the build with art), `--headful`, `--loopback` (advertise 127.0.0.1 instead of the LAN address), `--chrome PATH`, `--keep`, `--out DIR`.
