# That's You! - PC/Mac port with a web phone controller

Scripts that rebuild the PS4 PlayLink game "That's You!" (CUSA08389) from your own backup as a native Windows x64 and macOS arm64 game, and replace the discontinued phone app with a web page that runs in any phone browser, live camera effects included.

This repository contains no game or app files. You need your own PS4 dump (base game and patch) and the Android APK of the companion app.

## Before publishing anything

- `local-patches/` holds changes made against decompiled game code. It must be converted to build-time compiled-assembly edits (Plan 7) before anything from this repository is pushed to a public remote. The pre-push hook enforces this.
- Never commit or publish the address of your deployed phone page. Each person deploys and protects their own page.

## Setup

1. Copy `config/local.example.json` to `config/local.json` and fill in your paths.
2. Run the tests: `python3 -m unittest discover -s tests -v`

Further steps are added as the pipeline grows.
