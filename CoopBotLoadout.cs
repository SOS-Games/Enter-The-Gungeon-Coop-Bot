using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using Dungeonator;
using HarmonyLib;
using UnityEngine;

namespace CoopBot
{
    // Mod-owned snapshot of the bot's gear, taken at the exact moment the game writes its
    // mid-game save, keyed by that save's GUID. On continue we reconcile player 2 to it.
    public class BotSnapshot
    {
        public string SaveGuid = "";
        public List<int> Guns = new List<int>();
        public List<int> GunAmmo = new List<int>();
        public int CurrentGunId = -1;
        public List<int> Passives = new List<int>();
        public List<int> Actives = new List<int>();
        public float Health = -1f, Armor = -1f;
        public int Blanks = -1;
        public string CharacterPrefab = "";   // e.g. PlayerMarine; empty/PlayerCoopCultist = no override
        public int Costume;                    // 1 = alternate costume (the game also restores this itself)
    }

    public static class BotLoadout
    {
        private static string FilePath { get { return Path.Combine(Paths.ConfigPath, "CoopBot.loadout.txt"); } }
        private static string s_reconciledGuid;

        // ---------- capture / file ----------

        public static BotSnapshot Capture(PlayerController bot)
        {
            var s = new BotSnapshot();
            foreach (var g in bot.inventory.AllGuns)
            {
                if (g == null || CoopBotBrain.IsStartingGun(bot, g)) continue;
                s.Guns.Add(g.PickupObjectId);
                s.GunAmmo.Add(g.CurrentAmmo);
            }
            if (bot.CurrentGun != null) s.CurrentGunId = bot.CurrentGun.PickupObjectId;
            foreach (var p in bot.passiveItems) if (p != null && !IsStartingPassive(bot, p.PickupObjectId)) s.Passives.Add(p.PickupObjectId);
            foreach (var a in bot.activeItems) if (a != null && !IsStartingActive(bot, a.PickupObjectId)) s.Actives.Add(a.PickupObjectId);
            s.Health = bot.healthHaver.GetCurrentHealth();
            s.Armor = bot.healthHaver.Armor;
            s.Blanks = bot.Blanks;
            try { s.CharacterPrefab = CharacterSelectController.GetCharacterPathFromIdentity(bot.characterIdentity); } catch (Exception) { }
            s.Costume = bot.IsUsingAlternateCostume ? 1 : 0;
            return s;
        }

        private static bool IsStartingPassive(PlayerController p, int id) { return p.startingPassiveItemIds != null && p.startingPassiveItemIds.Contains(id); }
        private static bool IsStartingActive(PlayerController p, int id) { return p.startingActiveItemIds != null && p.startingActiveItemIds.Contains(id); }

        private static string Join(List<int> l) { var sb = new StringBuilder(); for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); sb.Append(l[i]); } return sb.ToString(); }
        private static List<int> Split(string v)
        {
            var l = new List<int>();
            foreach (var part in v.Split(',')) { int n; if (int.TryParse(part.Trim(), out n)) l.Add(n); }
            return l;
        }

        public static void Write(BotSnapshot s)
        {
            try
            {
                var c = CultureInfo.InvariantCulture;
                var lines = new[]
                {
                    "# CoopBot bot loadout snapshot (written when the game makes its mid-game save)",
                    "version=" + CoopBotPlugin.Version,
                    "saveGuid=" + s.SaveGuid,
                    "guns=" + Join(s.Guns),
                    "gunAmmo=" + Join(s.GunAmmo),
                    "currentGun=" + s.CurrentGunId,
                    "passives=" + Join(s.Passives),
                    "actives=" + Join(s.Actives),
                    "health=" + s.Health.ToString(c),
                    "armor=" + s.Armor.ToString(c),
                    "blanks=" + s.Blanks,
                    "character=" + s.CharacterPrefab,
                    "costume=" + s.Costume,
                };
                File.WriteAllText(FilePath, string.Join("\n", lines));
            }
            catch (Exception e) { Debug.LogWarning("CoopBot: could not write loadout: " + e.Message); }
        }

        public static BotSnapshot Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = new BotSnapshot();
                var c = CultureInfo.InvariantCulture;
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    int eq = raw.IndexOf('=');
                    if (raw.StartsWith("#") || eq < 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "saveGuid": s.SaveGuid = v; break;
                        case "guns": s.Guns = Split(v); break;
                        case "gunAmmo": s.GunAmmo = Split(v); break;
                        case "currentGun": int.TryParse(v, out s.CurrentGunId); break;
                        case "passives": s.Passives = Split(v); break;
                        case "actives": s.Actives = Split(v); break;
                        case "health": float.TryParse(v, NumberStyles.Float, c, out s.Health); break;
                        case "armor": float.TryParse(v, NumberStyles.Float, c, out s.Armor); break;
                        case "blanks": int.TryParse(v, out s.Blanks); break;
                        case "character": s.CharacterPrefab = v; break;
                        case "costume": int.TryParse(v, out s.Costume); break;
                    }
                }
                while (s.GunAmmo.Count < s.Guns.Count) s.GunAmmo.Add(-1);
                return s;
            }
            catch (Exception e) { Debug.LogWarning("CoopBot: could not read loadout: " + e.Message); return null; }
        }

        public static void Clear()
        {
            s_reconciledGuid = null;
            PendingRevive = null;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception) { }
        }

        private static string CurrentSaveGuid()
        {
            return GameStatsManager.HasInstance ? GameStatsManager.Instance.midGameSaveGuid : null;
        }

        // Called right after GameManager.DoMidgameSave (elevator departure etc.).
        public static void OnMidGameSave()
        {
            var brain = CoopBotPlugin.Brain;
            var gm = GameManager.Instance;
            if (brain == null || brain.Bot == null || gm.SecondaryPlayer != brain.Bot) return;
            // A ghost bot with DropItemsOnDeath=false holds its gear in PendingRevive, not in its inventory.
            var s = (PendingRevive != null && (brain.Bot.IsGhost || brain.Bot.healthHaver.IsDead)) ? PendingRevive : Capture(brain.Bot);
            s.SaveGuid = CurrentSaveGuid() ?? "";
            s_reconciledGuid = s.SaveGuid; // the live bot already matches this snapshot
            Write(s);
        }

        // ---------- reconcile ----------

        public static IEnumerator ReconcileLater(PlayerController bot)
        {
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            if (bot != null) ReconcileIfSaved(bot);
        }

        public static void ReconcileIfSaved(PlayerController bot)
        {
            string guid = CurrentSaveGuid();
            if (string.IsNullOrEmpty(guid) || guid == s_reconciledGuid) return;
            var s = Read();
            if (s == null || s.SaveGuid != guid) return; // stale snapshot from another run/save
            s_reconciledGuid = guid;
            if (bot.IsGhost || bot.healthHaver.IsDead)
            {
                // Don't hand gear to a ghost; give it back when it is revived.
                s.Health = -1f; s.Armor = -1f; s.Blanks = -1;
                PendingRevive = s;
                return;
            }
            Apply(bot, s, true);
            CoopBotPlugin.Say("Bot gear restored from save.");
        }

        private static Dictionary<int, int> Count(List<int> ids)
        {
            var d = new Dictionary<int, int>();
            foreach (int id in ids) { int n; d.TryGetValue(id, out n); d[id] = n + 1; }
            return d;
        }

        // Makes the bot hold exactly the snapshot's non-starting gear (plus its own body's starters).
        public static void Apply(PlayerController bot, BotSnapshot s, bool setVitals)
        {
            try
            {
                EncounterTrackable.SuppressNextNotification = true;

                // Guns: remove extras that are not starters of this body, then add missing, then set ammo.
                var wantGuns = Count(s.Guns);
                var haveGuns = new Dictionary<int, int>();
                foreach (var g in new List<Gun>(bot.inventory.AllGuns))
                {
                    if (g == null || CoopBotBrain.IsStartingGun(bot, g)) continue;
                    int want; wantGuns.TryGetValue(g.PickupObjectId, out want);
                    int have; haveGuns.TryGetValue(g.PickupObjectId, out have);
                    if (have >= want) bot.inventory.DestroyGun(g);
                    else haveGuns[g.PickupObjectId] = have + 1;
                }
                for (int i = 0; i < s.Guns.Count; i++)
                {
                    int id = s.Guns[i];
                    Gun owned = null;
                    foreach (var g in bot.inventory.AllGuns) if (g != null && g.PickupObjectId == id) owned = g;
                    if (owned == null)
                    {
                        var proto = PickupObjectDatabase.GetById(id) as Gun;
                        if (proto == null) continue;
                        EncounterTrackable.SuppressNextNotification = true;
                        owned = LootEngine.TryGiveGunToPlayer(proto.gameObject, bot, true);
                    }
                    if (owned != null && s.GunAmmo[i] >= 0) owned.CurrentAmmo = s.GunAmmo[i];
                }

                // Passives (duplicates allowed, so compare counts).
                var wantP = Count(s.Passives);
                var haveP = new Dictionary<int, int>();
                foreach (var p in new List<PassiveItem>(bot.passiveItems))
                {
                    if (p == null || IsStartingPassive(bot, p.PickupObjectId)) continue;
                    int want; wantP.TryGetValue(p.PickupObjectId, out want);
                    int have; haveP.TryGetValue(p.PickupObjectId, out have);
                    if (have >= want) bot.RemovePassiveItem(p.PickupObjectId);
                    else haveP[p.PickupObjectId] = have + 1;
                }
                foreach (var kv in wantP)
                {
                    int have; haveP.TryGetValue(kv.Key, out have);
                    for (int n = have; n < kv.Value; n++)
                    {
                        var item = PickupObjectDatabase.GetById(kv.Key) as PassiveItem;
                        if (item == null) break;
                        EncounterTrackable.SuppressNextNotification = true;
                        LootEngine.TryGivePrefabToPlayer(item.gameObject, bot, true);
                    }
                }

                // Actives.
                var wantA = Count(s.Actives);
                var haveA = new Dictionary<int, int>();
                foreach (var a in new List<PlayerItem>(bot.activeItems))
                {
                    if (a == null || IsStartingActive(bot, a.PickupObjectId)) continue;
                    int want; wantA.TryGetValue(a.PickupObjectId, out want);
                    int have; haveA.TryGetValue(a.PickupObjectId, out have);
                    if (have >= want) bot.RemoveActiveItem(a.PickupObjectId);
                    else haveA[a.PickupObjectId] = have + 1;
                }
                foreach (var kv in wantA)
                {
                    int have; haveA.TryGetValue(kv.Key, out have);
                    for (int n = have; n < kv.Value; n++)
                    {
                        var item = PickupObjectDatabase.GetById(kv.Key) as PlayerItem;
                        if (item == null) break;
                        EncounterTrackable.SuppressNextNotification = true;
                        LootEngine.TryGivePrefabToPlayer(item.gameObject, bot, true);
                    }
                }

                // Re-select the gun that was held.
                if (s.CurrentGunId >= 0 && bot.CurrentGun != null && bot.CurrentGun.PickupObjectId != s.CurrentGunId)
                {
                    var guns = bot.inventory.AllGuns;
                    int cur = guns.IndexOf(bot.CurrentGun);
                    for (int i = 0; i < guns.Count; i++)
                        if (guns[i] != null && guns[i].PickupObjectId == s.CurrentGunId && cur >= 0) { bot.inventory.ChangeGun(i - cur); break; }
                }

                if (setVitals)
                {
                    if (s.Health > 0f) bot.healthHaver.ForceSetCurrentHealth(Mathf.Min(s.Health, bot.healthHaver.GetMaxHealth()));
                    if (s.Armor >= 0f) bot.healthHaver.Armor = s.Armor;
                    if (s.Blanks >= 0) bot.Blanks = s.Blanks;
                }
            }
            catch (Exception e) { Debug.LogWarning("CoopBot: loadout reconcile failed: " + e); }
            finally { EncounterTrackable.SuppressNextNotification = false; }
        }

        // ---------- death ----------

        // Gear kept aside while the bot is a ghost (DropItemsOnDeath = false). Re-granted on revive.
        public static BotSnapshot PendingRevive;

        // Replaces PlayerController.HandleCoopDeathItemDropping for the bot. The game calls it when a
        // co-op player dies while the other is alive (HandleCoopDeath), right before
        // ResetToFactorySettings wipes the inventory back to starters and the player becomes a ghost.
        public static void OnBotCoopDeath(PlayerController bot)
        {
            if (!CoopBotPlugin.DropItemsOnDeath.Value)
            {
                var keep = Capture(bot);
                keep.Health = -1f; keep.Armor = -1f; keep.Blanks = -1;
                PendingRevive = keep;
                CoopBotPlugin.Say("Bot is down. It keeps its gear until revived.");
                return;
            }
            PendingRevive = null;
            int n = 0;
            try
            {
                var cur = bot.CurrentGun;
                if (cur != null)
                {
                    var mimic = cur.GetComponent<MimicGunController>();
                    if (mimic != null) mimic.ForceClearMimic(true);
                }
                foreach (var g in new List<Gun>(bot.inventory.AllGuns))
                {
                    if (g == null || CoopBotBrain.IsStartingGun(bot, g) || g.PersistsOnDeath || !g.CanActuallyBeDropped(bot)) continue;
                    Spread(bot.ForceDropGun(g), n++);
                }
                foreach (var p in new List<PassiveItem>(bot.passiveItems))
                {
                    if (p == null || p.PersistsOnDeath || p is ExtraLifeItem || IsStartingPassive(bot, p.PickupObjectId) || !p.CanActuallyBeDropped(bot)) continue;
                    Spread(bot.DropPassiveItem(p), n++);
                }
                foreach (var a in new List<PlayerItem>(bot.activeItems))
                {
                    if (a == null || a.PersistsOnDeath || IsStartingActive(bot, a.PickupObjectId) || !a.CanActuallyBeDropped(bot)) continue;
                    Spread(bot.DropActiveItem(a, 4f, true), n++);
                }
            }
            catch (Exception e) { Debug.LogWarning("CoopBot: death drop failed: " + e); }
            if (n > 0) CoopBotPlugin.Say("Bot is down and dropped " + n + " item(s).");
        }

        // Fan the drops out on a spiral around the death position so they don't stack.
        private static void Spread(DebrisObject d, int i)
        {
            if (d == null) return;
            var po = d.GetComponentInChildren<PickupObject>();
            if (po != null) { po.IgnoredByRat = true; po.ClearIgnoredByRatFlagOnPickup = true; }
            float ang = i * 137.5f * Mathf.Deg2Rad;
            float r = 0.9f + 0.35f * i;
            Vector3 off = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
            Vector2 target = (Vector2)(d.transform.position + off);
            // keep out of walls/pits: fall back to the unshifted spot
            var dun = GameManager.Instance.Dungeon;
            if (dun != null && dun.data != null)
            {
                IntVector2 c = target.ToIntVector2(VectorConversions.Floor);
                if (!dun.data.CheckInBounds(c) || dun.data[c] == null || dun.data[c].type != CellType.FLOOR) return;
            }
            d.transform.position = d.transform.position + off;
        }

        // Called every frame: give kept gear back once the ghost has been revived.
        public static void CheckRevive()
        {
            if (PendingRevive == null) return;
            var brain = CoopBotPlugin.Brain;
            if (brain == null || brain.Bot == null) return;
            var bot = brain.Bot;
            if (bot.IsGhost || bot.healthHaver.IsDead) return;
            var s = PendingRevive;
            PendingRevive = null;
            Apply(bot, s, false);
            CoopBotPlugin.Say("Bot revived with its gear.");
        }

        // ---------- trading ----------

        private static IEnumerator HandOver(PickupObject item, PlayerController to, string message)
        {
            yield return null; // let the drop finish before the pickup
            if (item == null || to == null) yield break;
            var ix = item as IPlayerInteractable;
            if (ix != null) ix.Interact(to);
            CoopBotPlugin.Say(message);
        }

        private static bool Ready(out PlayerController prim, out PlayerController bot)
        {
            prim = GameManager.Instance.PrimaryPlayer;
            bot = CoopBotPlugin.Brain != null ? CoopBotPlugin.Brain.Bot : null;
            if (bot == null || prim == null) { CoopBotPlugin.Say("No bot active."); return false; }
            if (bot.healthHaver.IsDead || bot.IsGhost) { CoopBotPlugin.Say("Your bot is down."); return false; }
            return true;
        }

        private static string Name(PickupObject p) { try { return p.EncounterNameOrDisplayName; } catch (Exception) { return p.name; } }

        public static bool IsStarter(PlayerController owner, PickupObject item)
        {
            if (item is Gun) return CoopBotBrain.IsStartingGun(owner, (Gun)item);
            if (item is PassiveItem) return IsStartingPassive(owner, item.PickupObjectId);
            if (item is PlayerItem) return IsStartingActive(owner, item.PickupObjectId);
            return true;
        }

        public static bool CanTransfer(PlayerController owner, PickupObject item)
        {
            if (owner == null || item == null || IsStarter(owner, item)) return false;
            try { return item.CanActuallyBeDropped(owner); } catch (Exception) { return false; }
        }

        // Moves one specific gun/active/passive from one player to the other via the game's drop -> pickup path.
        public static void Transfer(PickupObject item, PlayerController from, PlayerController to)
        {
            if (!CanTransfer(from, item) || to == null) { CoopBotPlugin.Say("That can't be traded (starter or undroppable)."); return; }
            DebrisObject d = null;
            if (item is Gun) d = from.ForceDropGun((Gun)item);
            else if (item is PassiveItem) d = from.DropPassiveItem((PassiveItem)item);
            else if (item is PlayerItem) d = from.DropActiveItem((PlayerItem)item);
            if (d == null) { CoopBotPlugin.Say("Couldn't drop " + Name(item) + " right now."); return; }
            bool toBot = CoopBotPlugin.Brain != null && to == CoopBotPlugin.Brain.Bot;
            GameManager.Instance.StartCoroutine(HandOver(item, to, (toBot ? "Gave " : "Took ") + Name(item) + (toBot ? " to the bot." : " from the bot.")));
        }

        // ---------- trade window ----------

        public static bool TradeOpen;
        private static Vector2 s_scrollMine, s_scrollBot;
        private static Rect s_window = new Rect(60, 60, 820, 520);
        private static bool s_cursorWas;
        private const string InputLock = "coopbot trade";

        public static void ToggleTrade()
        {
            if (TradeOpen) { CloseTrade(); return; }
            PlayerController prim, bot;
            if (!Ready(out prim, out bot)) return;
            var room = prim.CurrentRoom;
            if (room != null && room.HasActiveEnemies(RoomHandler.ActiveEnemyType.RoomClear)) { CoopBotPlugin.Say("Can't trade during combat."); return; }
            TradeOpen = true;
            prim.SetInputOverride(InputLock);
            s_cursorWas = Cursor.visible;
            Cursor.visible = true;
        }

        public static void CloseTrade()
        {
            if (!TradeOpen) return;
            TradeOpen = false;
            var prim = GameManager.HasInstance ? GameManager.Instance.PrimaryPlayer : null;
            if (prim != null) prim.ClearInputOverride(InputLock);
            Cursor.visible = s_cursorWas;
        }

        public static void TradeUpdate()
        {
            if (!TradeOpen) return;
            var gm = GameManager.Instance;
            var brain = CoopBotPlugin.Brain;
            // Close automatically if anything makes trading unsafe.
            if (brain == null || brain.Bot == null || gm.PrimaryPlayer == null || gm.IsLoadingLevel || gm.IsFoyer || brain.Bot.IsGhost || brain.Bot.healthHaver.IsDead
                || (gm.PrimaryPlayer.CurrentRoom != null && gm.PrimaryPlayer.CurrentRoom.HasActiveEnemies(RoomHandler.ActiveEnemyType.RoomClear)))
                CloseTrade();
            else Cursor.visible = true;
        }

        public static void DrawTrade()
        {
            if (!TradeOpen) return;
            var brain = CoopBotPlugin.Brain;
            var prim = GameManager.Instance.PrimaryPlayer;
            if (brain == null || brain.Bot == null || prim == null) return;
            s_window = GUI.Window(0xC00B, s_window, id =>
            {
                var bot = CoopBotPlugin.Brain.Bot;
                GUI.Label(new Rect(12, 22, 400, 22), "YOU: " + Vitals(prim));
                GUI.Label(new Rect(416, 22, 400, 22), "BOT: " + Vitals(bot));
                DrawColumn(new Rect(10, 48, 395, 430), ref s_scrollMine, prim, bot, "Give >");
                DrawColumn(new Rect(415, 48, 395, 430), ref s_scrollBot, bot, prim, "< Take");
                if (GUI.Button(new Rect(s_window.width - 110, s_window.height - 34, 100, 26), "Close")) CloseTrade();
                GUI.Label(new Rect(12, s_window.height - 32, 600, 24), "Grey = starter / can't be dropped. Trading is disabled while enemies are active.");
                GUI.DragWindow(new Rect(0, 0, 10000, 20));
            }, "Coop Bot - Trade  (" + CoopBotPlugin.TradeKey.Value + " to close)");
        }

        private static string Vitals(PlayerController p)
        {
            var c = CultureInfo.InvariantCulture;
            return "HP " + p.healthHaver.GetCurrentHealth().ToString("0.#", c) + "/" + p.healthHaver.GetMaxHealth().ToString("0.#", c)
                + "  Armor " + p.healthHaver.Armor.ToString("0", c) + "  Blanks " + p.Blanks;
        }

        private static void DrawColumn(Rect area, ref Vector2 scroll, PlayerController owner, PlayerController other, string verb)
        {
            var rows = new List<KeyValuePair<string, PickupObject>>();
            rows.Add(new KeyValuePair<string, PickupObject>("Guns", null));
            foreach (var g in owner.inventory.AllGuns) if (g != null) rows.Add(new KeyValuePair<string, PickupObject>(null, g));
            rows.Add(new KeyValuePair<string, PickupObject>("Active items", null));
            foreach (var a in owner.activeItems) if (a != null) rows.Add(new KeyValuePair<string, PickupObject>(null, a));
            rows.Add(new KeyValuePair<string, PickupObject>("Passive items", null));
            foreach (var p in owner.passiveItems) if (p != null) rows.Add(new KeyValuePair<string, PickupObject>(null, p));

            GUI.Box(area, "");
            float rowH = 24f;
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, area.width - 20, rows.Count * rowH + 8));
            float y = 4;
            PickupObject clicked = null;
            foreach (var r in rows)
            {
                if (r.Value == null) { GUI.Label(new Rect(6, y, 300, rowH), "<b>" + r.Key + "</b>"); y += rowH; continue; }
                var item = r.Value;
                bool ok = CanTransfer(owner, item);
                string label = Name(item);
                var g = item as Gun;
                if (g != null) label += g.InfiniteAmmo ? "  (inf)" : "  " + g.CurrentAmmo + "/" + g.AdjustedMaxAmmo;
                if (g != null && g == owner.CurrentGun) label = "> " + label;
                if (!ok) label += IsStarter(owner, item) ? "  [starter]" : "  [locked]";
                var prev = GUI.enabled;
                GUI.color = ok ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(new Rect(6, y, area.width - 110, rowH), label);
                GUI.enabled = ok;
                if (GUI.Button(new Rect(area.width - 100, y + 1, 72, rowH - 3), verb)) clicked = item;
                GUI.enabled = prev;
                GUI.color = Color.white;
                y += rowH;
            }
            GUI.EndScrollView();
            if (clicked != null) Transfer(clicked, owner, other); // after the loop: the lists change
        }

        // ---------- gear panel ----------

        private static GUIStyle s_style;
        private static string s_text = "";
        private static float s_nextRefresh;

        public static void DrawPanel()
        {
            var brain = CoopBotPlugin.Brain;
            if (brain == null || brain.Bot == null) return;
            if (Time.realtimeSinceStartup > s_nextRefresh) { s_nextRefresh = Time.realtimeSinceStartup + 0.5f; s_text = Describe(brain.Bot); }
            if (s_style == null) s_style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
            float w = 340f, h = Mathf.Min(Screen.height - 40f, 30f + 18f * (1 + s_text.Split('\n').Length));
            GUI.Box(new Rect(Screen.width - w - 12f, 60f, w, h), s_text, s_style);
        }

        private static string Describe(PlayerController bot)
        {
            var sb = new StringBuilder();
            var c = CultureInfo.InvariantCulture;
            sb.Append("BOT GEAR  (").Append(CoopBotPlugin.Version).Append(")\n");
            sb.Append("HP ").Append(bot.healthHaver.GetCurrentHealth().ToString("0.#", c)).Append('/').Append(bot.healthHaver.GetMaxHealth().ToString("0.#", c))
              .Append("  Armor ").Append(bot.healthHaver.Armor.ToString("0", c)).Append("  Blanks ").Append(bot.Blanks).Append('\n');
            sb.Append("Guns:\n");
            foreach (var g in bot.inventory.AllGuns)
            {
                if (g == null) continue;
                sb.Append(g == bot.CurrentGun ? " > " : "   ").Append(Name(g)).Append("  ");
                sb.Append(g.InfiniteAmmo ? "inf" : g.CurrentAmmo + "/" + g.AdjustedMaxAmmo);
                if (CoopBotBrain.IsStartingGun(bot, g)) sb.Append(" (starter)");
                sb.Append('\n');
            }
            sb.Append("Actives:\n");
            if (bot.activeItems.Count == 0) sb.Append("   -\n");
            foreach (var a in bot.activeItems) if (a != null) sb.Append(a == bot.CurrentItem ? " > " : "   ").Append(Name(a)).Append(a.IsOnCooldown ? " (cooldown)" : "").Append('\n');
            sb.Append("Passives:\n");
            if (bot.passiveItems.Count == 0) sb.Append("   -\n");
            foreach (var p in bot.passiveItems) if (p != null) sb.Append("   ").Append(Name(p)).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }
    }

    [HarmonyPatch(typeof(GameManager), "DoMidgameSave")]
    public static class MidGameSavePatch
    {
        public static void Postfix() { BotLoadout.OnMidGameSave(); }
    }
}

namespace CoopBot
{
    // Character persistence: when the game rebuilds player 2 from a mid-game save it always loads
    // "PlayerCoopCultist" (GameManager.GeneratePlayersFromMidGameSave). While that method runs we
    // swap that one prefab lookup for the bot's saved character, so the game's own restore
    // (items, ammo, health, costume) is applied to the right body with the right starter guns.
    [HarmonyPatch(typeof(GameManager), "GeneratePlayersFromMidGameSave")]
    public static class ContinueCharacterPatch
    {
        internal static string OverridePrefab;
        internal static bool Used;

        public static void Prefix(MidGameSaveData loadedSave)
        {
            OverridePrefab = null; Used = false;
            try
            {
                if (loadedSave == null || loadedSave.savedGameType != GameManager.GameType.COOP_2_PLAYER) return;
                if (loadedSave.levelSaved == GlobalDungeonData.ValidTilesets.FINALGEON) return; // pasts use special bodies
                if (!CoopBotPlugin.BotActive.Value) return;
                var snap = BotLoadout.Read();
                if (snap == null || string.IsNullOrEmpty(snap.CharacterPrefab) || snap.CharacterPrefab == "PlayerCoopCultist") return;
                if (snap.SaveGuid != loadedSave.midGameSaveGuid) return; // snapshot belongs to another save
                OverridePrefab = snap.CharacterPrefab;
            }
            catch (System.Exception e) { Debug.LogWarning("CoopBot: character restore prefix failed: " + e.Message); }
        }

        public static void Postfix()
        {
            try
            {
                if (!Used) return;
                var gm = GameManager.Instance;
                gm.RefreshAllPlayers();
                foreach (var p in gm.AllPlayers)
                {
                    // SecondaryPlayer's lazy lookup only recognises the Cultist, so register P2 explicitly.
                    if (p != null && p.PlayerIDX == 1) { gm.SecondaryPlayer = p; break; }
                }
                Debug.Log("CoopBot: restored bot as " + OverridePrefab);
            }
            finally { OverridePrefab = null; Used = false; }
        }
    }

    [HarmonyPatch(typeof(ResourceCache), "Acquire", new[] { typeof(string) })]
    public static class CoopPrefabSwapPatch
    {
        public static void Postfix(string resourceName, ref UnityEngine.Object __result)
        {
            string over = ContinueCharacterPatch.OverridePrefab;
            if (over == null || ContinueCharacterPatch.Used || resourceName != "PlayerCoopCultist") return;
            var prefab = BraveResources.Load(over) as GameObject;
            if (prefab == null) return; // fall back to the Cultist
            __result = prefab;
            ContinueCharacterPatch.Used = true;
        }
    }

    // Safety net: if the game drops its cached reference to P2, its lookup only finds a Cultist.
    // Fall back to "the non-primary player" so a non-Cultist bot is still player 2.
    [HarmonyPatch(typeof(GameManager), "get_SecondaryPlayer")]
    public static class SecondaryPlayerFallbackPatch
    {
        public static void Postfix(GameManager __instance, ref PlayerController __result)
        {
            if (__result != null || CoopBotPlugin.Brain == null && !CoopBotPlugin.BotActive.Value) return;
            if (GameManager.Instance.CurrentGameType == GameManager.GameType.SINGLE_PLAYER) return;
            var all = __instance.AllPlayers;
            if (all == null) return;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !all[i].IsPrimaryPlayer) { __result = all[i]; return; }
        }
    }
}

namespace CoopBot
{
    [HarmonyPatch(typeof(PlayerController), "HandleCoopDeathItemDropping")]
    public static class BotDeathDropPatch
    {
        public static bool Prefix(PlayerController __instance)
        {
            var brain = CoopBotPlugin.Brain;
            if (brain == null || __instance == null || __instance != brain.Bot) return true; // human players: vanilla
            BotLoadout.OnBotCoopDeath(__instance);
            return false;
        }
    }
}
