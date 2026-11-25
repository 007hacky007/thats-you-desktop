# Unity version

Pinned: **Unity 6000.3.29f1** (Unity 6.3 LTS, changeset 96d2fe17529c, released 2026-11-19).

Why 6.3 LTS and not 2022.3 LTS:

- 2022.3 LTS is out of its support window. 6.3 LTS is supported until December 2027.
- The game uses the built-in render pipeline. Unity starts deprecating it in 6.5, and it stays supported (bug fixes and maintenance) through the whole 6.7 LTS lifecycle, at least until the end of 2028. In 6.3 it is fully supported with no deprecation warnings.
- Native Windows x64 and macOS arm64 standalone players.
- Unity Personal lets you turn off the "Made with Unity" splash screen in Unity 6.

Install: Unity Hub, Personal license, editor 6000.3.29f1 with "Windows Build Support (Mono)" and "Mac Build Support (Mono)".
Hub deep link: `unityhub://6000.3.29f1/96d2fe17529c`

Sources:
- https://unity.com/topics/render-pipelines-strategy-for-2026
- https://docs.unity3d.com/6000.5/Documentation/Manual/urp/upgrading-from-birp.html
- https://unity.com/releases/unity-6/support
- https://unity.com/blog/unity-6-3-lts-is-now-available
- Unity release API: https://services.api.unity.com/unity/editor/release/v1/releases?stream=LTS&version=6000.3
