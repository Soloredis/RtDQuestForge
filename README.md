# RtDQuestForge

A server-synced quest system for Valheim. Drop quest packs into a config
folder as json files, and players get a vanilla-styled quest journal with
accept/abandon, live objective tracking, prerequisites, and rewards.

Built for dedicated servers first: the server's quest list is the single
source of truth, and kill credit works correctly in multiplayer regardless
of which machine owns the creature.

## Features

- **Quest Journal UI** - Built from Valheim's own woodpanel sprites and
  Norse font. Opens on `L` (rebindable). Active and Completed columns,
  rarity-colored quest cards, mouse wheel scrolling.
- **Accept / Abandon** - Quests only progress once accepted. Abandoning a
  quest wipes its progress, so re-accepting starts fresh.
- **Kill and gather objectives** - Any creature prefab, any item prefab,
  any amounts, freely mixed in a single quest.
- **Rewards** - Item rewards (dropped at your feet if your inventory is
  full), vanilla skill XP rewards, and optional EpicMMOSystem XP.
- **Prerequisite chains** - Lock quests behind other quests. Locked quests
  show greyed out with the name of the quest that unlocks them.
- **Multi-file quest packs** - Every `*.json` in the config folder loads as
  its own journal page. Ship quest packs per biome, per author, per
  whatever - no merging into one giant file.
- **Server sync** - Clients receive the server's quest list on spawn.
  Server admins edit quests in one place; local client files don't matter.
- **Multiplayer kill credit** - Kills are credited to the player who landed
  the killing blow, routed over the network when the creature was owned by
  another machine. No more lost credit when hunting in a group.
- **JSON localization** - All UI strings load from
  `config/RtDQuestForge/translations/`. Copy `English.json`, rename it to
  your language, translate the values. Quest titles and goals also support
  `$tokens` for fully localized quest packs.

## Installation

1. Install [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/),
   [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/), and
   [Json.NET](https://thunderstore.io/c/valheim/p/ValheimModding/JsonDotNET/).
2. Drop `RtDQuestForge.dll` into `BepInEx/plugins/`.
3. Launch once. Starter quest files and the translation template generate
   in `BepInEx/config/RtDQuestForge/`.

On dedicated servers, install the same way server-side. All players must
have the mod (enforced via Jotunn network compatibility).

## Creating quests

Add any `myquests.json` to `BepInEx/config/RtDQuestForge/` (server-side on
dedicated servers). Each file becomes a journal page. Example quest showing
every field:

```json
{
  "Quests": [
    {
      "ID": "example_quest",
      "Title": "Seasoned Hunter",
      "Goal": "Prove yourself against the boars.",
      "Rarity": "Uncommon",
      "PreReqID": "some_other_quest_id",
      "KillReqs": [ { "Prefab": "Boar", "Amount": 5 } ],
      "GatherReqs": [ { "Prefab": "LeatherScraps", "Amount": 5 } ],
      "RewardItems": [ { "Prefab": "Coins", "Amount": 40 } ],
      "SkillRewards": [ { "Skill": "Bows", "Amount": 10.0 } ],
      "ExpReward": 150
    }
  ]
}
```

- `ID` must be unique across all files. Duplicates are skipped and logged.
- `Rarity` colors the card border: Common, Uncommon, Rare, Epic, Legendary.
- `PreReqID` is optional - omit it for quests available from the start.
- `Skill` names match Valheim's skill enum: Swords, Bows, Run, Sneak, etc.
- `ExpReward` grants EpicMMOSystem XP and is ignored (and hidden in the
  journal) when EpicMMOSystem is not installed.
- Progress is saved per character in `progress_<name>.json` - don't edit
  those, and they are ignored by the quest loader.

## Configuration

`BepInEx/config/soloredis.rtdquestforge.cfg`:

- `General / JournalHotkey` - Key to open the journal (default `L`).
- `Logging / Enable` - Verbose diagnostic logging for kill credit routing.
  Leave off unless you are debugging.

# Support & Feedback

* If you enjoy the hundreds of hours put into this overhaul and want to show some love, you can support my work via Patreon.
* Many authors are moving to  which supports Gale mod manager.
* [Patreon](https://www.patreon.com/c/Soloredis/about)
* [Hexium](https://mods.valtools.org/)
* [RtD Discord](https://discord.gg/mx4WK39z54)
* [Hexium Discord](https://discord.gg/nfCQBKYPvH)

# Licensing

- To review the terms of this mod please read the LICENSE.txt included. 
- Modpack developers may list this mod as a dependency within a manifest file.
- Reuploading the .dll is strictly prohibited. 
