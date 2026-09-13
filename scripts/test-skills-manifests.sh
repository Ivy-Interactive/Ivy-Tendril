#!/usr/bin/env bash
# Validates Agent Skills manifests, frontmatter, relative references, and symlinks.
# Exits 0 on success, 1 on any failure.
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "$script_dir/.." && pwd)

echo "==> Validating Tendril Agent Skills in: $repo_root"
failed=0

# 1. Validate JSON manifests
echo "--- Step 1: Validating JSON manifests ---"
manifests=(
    ".claude-plugin/marketplace.json"
    ".claude-plugin/plugin.json"
    ".agents/plugins/marketplace.json"
    ".codex-plugin/plugin.json"
)

for rel_path in "${manifests[@]}"; do
    full_path="$repo_root/$rel_path"
    if [ ! -f "$full_path" ]; then
        echo "FAIL: Missing manifest: $rel_path"
        failed=1
        continue
    fi
    if ! python3 -m json.tool "$full_path" >/dev/null 2>&1; then
        echo "FAIL: Invalid JSON in $rel_path"
        failed=1
        continue
    fi
    echo "PASS: Valid JSON: $rel_path"
done

# Check basic manifest schemas
python3 - <<EOF
import json, sys, os

root = "$repo_root"
errors = []

# Validate Claude Code marketplace.json
try:
    with open(os.path.join(root, ".claude-plugin/marketplace.json"), "r") as f:
        data = json.load(f)
        if "name" not in data or "plugins" not in data or not isinstance(data["plugins"], list):
            errors.append(".claude-plugin/marketplace.json missing name or plugins list")
        elif len(data["plugins"]) == 0 or "name" not in data["plugins"][0]:
            errors.append(".claude-plugin/marketplace.json plugins list is empty or malformed")
except Exception as e:
    errors.append(f".claude-plugin/marketplace.json error: {e}")

# Validate Claude Code plugin.json
try:
    with open(os.path.join(root, ".claude-plugin/plugin.json"), "r") as f:
        data = json.load(f)
        if not all(k in data for k in ("name", "version", "description", "skills")):
            errors.append(".claude-plugin/plugin.json missing required keys (name, version, description, skills)")
except Exception as e:
    errors.append(f".claude-plugin/plugin.json error: {e}")

# Validate Antigravity marketplace.json
try:
    with open(os.path.join(root, ".agents/plugins/marketplace.json"), "r") as f:
        data = json.load(f)
        if "name" not in data or "plugins" not in data or not isinstance(data["plugins"], list):
            errors.append(".agents/plugins/marketplace.json missing name or plugins list")
except Exception as e:
    errors.append(f".agents/plugins/marketplace.json error: {e}")

# Validate Codex plugin.json
try:
    with open(os.path.join(root, ".codex-plugin/plugin.json"), "r") as f:
        data = json.load(f)
        if not all(k in data for k in ("name", "version", "skills")):
            errors.append(".codex-plugin/plugin.json missing required keys (name, version, skills)")
except Exception as e:
    errors.append(f".codex-plugin/plugin.json error: {e}")

if errors:
    for err in errors:
        print(f"FAIL: {err}")
    sys.exit(1)
print("PASS: All manifest schemas verified")
EOF

if [ $? -ne 0 ]; then
    failed=1
fi

# 2. Validate skills/ subdirectories and SKILL.md frontmatter
echo "--- Step 2: Validating skills/ subdirectories and SKILL.md frontmatter ---"
skills_dir="$repo_root/skills"
if [ ! -d "$skills_dir" ]; then
    echo "FAIL: skills/ directory does not exist"
    failed=1
else
    skill_count=0
    for skill_path in "$skills_dir"/*; do
        if [ -d "$skill_path" ]; then
            skill_name=$(basename "$skill_path")
            skill_md="$skill_path/SKILL.md"
            skill_count=$((skill_count + 1))
            if [ ! -f "$skill_md" ]; then
                echo "FAIL: Missing SKILL.md in skills/$skill_name"
                failed=1
                continue
            fi

            # Validate YAML frontmatter has name: and description:
            has_frontmatter=$(head -n 1 "$skill_md" | grep -c "^---" || true)
            has_name=$(grep -E "^name:[[:space:]]+" "$skill_md" | head -n 1 || true)
            has_desc=$(grep -E "^description:[[:space:]]+" "$skill_md" | head -n 1 || true)

            if [ "$has_frontmatter" -eq 0 ] || [ -z "$has_name" ] || [ -z "$has_desc" ]; then
                echo "FAIL: skills/$skill_name/SKILL.md missing valid YAML frontmatter (name, description)"
                failed=1
            else
                echo "PASS: skills/$skill_name (valid SKILL.md with frontmatter)"
            fi
        fi
    done
    if [ "$skill_count" -eq 0 ]; then
        echo "FAIL: No skills found in skills/ directory"
        failed=1
    fi
fi

# 3. Validate relative paths in manifests, setup guides, and scripts
echo "--- Step 3: Validating referenced relative paths ---"
setup_guides=(
    "docs/vscode-setup.md"
    "docs/antigravity-setup.md"
    "docs/cursor-setup.md"
    "docs/claude-setup.md"
    "src/Ivy.Tendril.Docs/Docs/06_CodingAgents/00_Skills.md"
)

for guide in "${setup_guides[@]}"; do
    if [ -f "$repo_root/$guide" ]; then
        echo "PASS: Setup guide exists: $guide"
    else
        echo "FAIL: Missing setup guide: $guide"
        failed=1
    fi
done

# Validate referenced helper scripts
scripts_to_check=(
    "skills/tendril-extension/scripts/install-antigravity.sh"
    "skills/tendril-extension/scripts/package-vsix.sh"
    "skills/tendril-extension/scripts/uninstall-antigravity.sh"
)

for script in "${scripts_to_check[@]}"; do
    if [ -f "$repo_root/$script" ]; then
        echo "PASS: Referenced script exists: $script"
    else
        echo "FAIL: Missing referenced script: $script"
        failed=1
    fi
done

# 4. Validate symlink resolution
echo "--- Step 4: Validating symlink resolution ---"
symlink_roots=(
    ".claude/skills"
    ".agents/skills"
)

for root_rel in "${symlink_roots[@]}"; do
    full_root="$repo_root/$root_rel"
    if [ ! -d "$full_root" ]; then
        echo "FAIL: Symlink container directory missing: $root_rel"
        failed=1
        continue
    fi
    for link in "$full_root"/*; do
        if [ -L "$link" ]; then
            link_name=$(basename "$link")
            if [ -e "$link" ]; then
                echo "PASS: Symlink resolves: $root_rel/$link_name -> $(readlink "$link")"
            else
                echo "FAIL: Broken symlink: $root_rel/$link_name -> $(readlink "$link")"
                failed=1
            fi
        fi
    done
done

# Check VS Code extension skill symlink
vscode_skill="$repo_root/extensions/vscode/.agents/skills/tendril-extension"
if [ -L "$vscode_skill" ]; then
    if [ -e "$vscode_skill" ]; then
        echo "PASS: Symlink resolves: extensions/vscode/.agents/skills/tendril-extension -> $(readlink "$vscode_skill")"
    else
        echo "FAIL: Broken symlink: extensions/vscode/.agents/skills/tendril-extension -> $(readlink "$vscode_skill")"
        failed=1
    fi
else
    echo "FAIL: extensions/vscode/.agents/skills/tendril-extension is not a symlink"
    failed=1
fi

if [ "$failed" -eq 0 ]; then
    echo "==> ALL AGENT SKILLS MANIFEST TESTS PASSED"
    exit 0
else
    echo "==> AGENT SKILLS MANIFEST TESTS FAILED"
    exit 1
fi
