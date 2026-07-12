---
title: Getting Started with Brainwares
references:
  - path: "README.md"
    hash: "fe9c1fbbc4181b2643ad43a4e12677ba4f01baf65c4678d4c30c11b2d7a6095c"
tags: [tutorial, setup]
---

# Getting Started with Brainwares

Brainwares merges the concepts of **Obsidian** (connected local Markdown notes) and **Promptware** (self-improving, context-aware prompt modules).

## 1. Hashing Code References

We have linked this note to your `README.md` file! If you make any modifications to `README.md`, your brainwares memory will detect that it is out-of-sync.

Try this workflow:
1. Run `bw status` (it should say `Outdated memories: 0`).
2. Add a space or comment to `README.md`.
3. Run `bw status` again. It will flag this memory page as `[OUTDATED CODE]`.
4. Run `bw update getting-started` to re-hash the file and mark it clean again!

## 2. Linking Notes (Wiki-Links)

You can link memory notes using Obsidian double-bracket syntax: [[index]].
To check references and backlinks for this note:
```bash
bw read getting-started
```
