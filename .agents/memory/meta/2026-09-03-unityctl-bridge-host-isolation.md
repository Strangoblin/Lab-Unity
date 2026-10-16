---
name: unityctl-bridge-host-isolation
description: UnityCtl bridge host and network namespace diagnosis and recovery.
metadata:
  type: environment
---

# UnityCtl Bridge Host Isolation — 2026-09-03

## Root cause

The Unity Editor and a normal sandbox command can run in different process/network namespaces. A bridge started from the normal sandbox may fail to bind with `Socket permission denied`, become unreachable from later commands, or leave a stale `.unityctl/bridge.json` even though Unity is running.

Confirmed invariant (2026-09-04): a sandbox bridge and a real-host bridge/Editor are not interoperable. The bridge lifecycle and every `unityctl` verification command must stay in the same execution environment as the Editor; process discovery across the boundary is not proof of transport connectivity.

This is an environment and bridge lifecycle problem, not a UnityCtl package version mismatch.

## Recovery

Run the bridge lifecycle from the same host context as the Unity Editor and keep the daemon alive:

```bash
unityctl bridge stop --project /Users/xiaokangji/Unity/Lab
unityctl bridge start --project /Users/xiaokangji/Unity/Lab
unityctl bridge status --project /Users/xiaokangji/Unity/Lab
unityctl status --project /Users/xiaokangji/Unity/Lab
```

If the bridge reports healthy but Unity is not connected, trigger a harmless reimport of an existing Editor C# file to invoke UnityCtl's `[DidReloadScripts]` reconnect path. Verify with `unityctl wait --timeout 60`.

## Verified result

On 2026-09-03 the project connected successfully on bridge port `63328`, with matching UnityCtl `0.10.1` versions. `unityctl asset refresh` then completed with compilation succeeded.
