# Updating a customized profile (#34)

Plugin updates and keypad profiles are separate. Installing a new plugin does not rearrange
an existing profile. Revision `2026-09-14.1` is recorded in `profiles/profile-revisions.json`,
including the reviewed page layout and hashes of the semantic layouts.

1. Export your current profile using Options+ and keep that `.lp5` file.
2. Prepare an update using the matching product, terminal and platform default:

   ```sh
   python3 tools/profile-update.py --current /path/to/export.lp5 \
     --updated profiles/VizhiCodex-Keypad.lp5 --output /path/to/new-review-folder
   ```

   On Windows use `python` and `VizhiCodex-Windows.lp5`.
3. Read `README.txt` and `differences.json`. The latter contains exact before/after action IDs
   and JSON locations, including customized macros, wheels and other settings. Page indices
   are zero-based. The tool keeps an exact `backup.lp5` and refuses to overwrite a review folder.
4. The default `keep-custom` mode creates no replacement. Apply the desired action changes
   manually in Options+, leaving your custom keys in place. The revision record describes the
   new default positions: Yes/No on page 1 and Up/Enter/Down beside the page 2 pickers.
5. To adopt the complete new default, repeat with a new output directory and
   `--mode adopt-defaults`. This creates `candidate.lp5` with a distinct profile GUID and an
   “Update 2026-09-14.1” display name. Import it through Options+. Confirm it appears as a
   separate selectable profile and verify the layout before removing any old profile.
6. Roll back by selecting the original profile. If necessary, import the preserved export.

The candidate intentionally contains the new defaults; your customizations remain in the backup
and original imported profile. This is not an automatic merge. Preparing the same pair and revision
uses the same candidate GUID; a different export, update, or revision gets a different GUID.
No application registration or Options+ database is edited by the tool.

Automated checks validate identity consistency, exact backup preservation, platform/product
mismatches, custom bindings/macros, repeat preparation and default layouts. Actual import,
side-by-side selection and rollback still need verification in Options+ on macOS and Windows.
Do not describe #34 as closed until that host validation passes.

Prompt settings are independent of profile layout: Vizhi uses `~/.codex/vizhi/prompts.json`,
copying a valid Claude prompt list once if present. Your existing destination always wins.
Malformed or inaccessible source/settings leave the source untouched and use defaults for that
load; correct the file or permissions and reload to retry. Future changes are product-specific.
The existing untouched-factory-seed upgrade remains active; custom IDs, order, icons, text and
`submit: false` are preserved. `[]` intentionally means no prompt keys.
