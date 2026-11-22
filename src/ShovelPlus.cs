// ShovelPlus - BepInEx mod for Family Time (Episode 4)
// Adds an adjustable brush size, an instant slope tool and a smoothing tool to the shovel.
// NOTE: written in C# 5 so it builds with the csc.exe that ships with Windows (.NET Framework 4.x).
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using PropInstance = GAT.PropChunkManager.PropCollisionInstance;

namespace ShovelPlus
{
    public enum ShovelMode { Brush, Slope, Smooth }

    [BepInPlugin(Guid, "ShovelPlus", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "familytime.shovelplus";
        public const string Version = "1.0.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // ---- config ----
        internal static ConfigEntry<ShovelMode> CfgMode;
        internal static ConfigEntry<int> CfgBrushRadius;
        internal static ConfigEntry<int> CfgMaxRadius;
        internal static ConfigEntry<int> CfgSlopeLength;
        internal static ConfigEntry<int> CfgSlopeWidth;
        internal static ConfigEntry<float> CfgSlopeAngle;
        internal static ConfigEntry<float> CfgSlopeAngleStep;
        internal static ConfigEntry<int> CfgSlopeDirectionSnap;
        internal static ConfigEntry<float> CfgSmoothStrength;
        internal static ConfigEntry<bool> CfgShowHud;
        internal static ConfigEntry<bool> CfgShowPreview;
        internal static ConfigEntry<Key> KeyBigger;
        internal static ConfigEntry<Key> KeySmaller;
        internal static ConfigEntry<Key> KeyCycleMode;

        // ---- live state ----
        internal static ShovelTool ActiveShovel;
        internal static int QueryExtent;            // >0 while QueueShovelAction runs: widen the prop query
        internal static int QueryCellX, QueryCellZ;
        internal static Vector2 PendingSlopeDir = new Vector2(0, 1);

        private string toast;
        private float toastUntil;
        private BrushPreview preview;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CfgMode = Config.Bind("General", "Mode", ShovelMode.Brush, "Current shovel mode (cycled in game with the mode key).");
            CfgShowHud = Config.Bind("General", "ShowHud", true, "Show the small ShovelPlus info box while the shovel is equipped.");
            CfgShowPreview = Config.Bind("General", "ShowPreview", true, "Draw an outline of the affected area on the terrain.");

            CfgBrushRadius = Config.Bind("Brush", "Radius", 0, "Brush radius in terrain corners. 0 = vanilla single-corner shovel.");
            CfgMaxRadius = Config.Bind("Brush", "MaxRadius", 24, "Upper limit for brush radius / slope length.");

            CfgSlopeLength = Config.Bind("Slope", "Length", 8, "How far the slope extends away from you (corners).");
            CfgSlopeWidth = Config.Bind("Slope", "Width", 4, "Slope width (corners).");
            CfgSlopeAngle = Config.Bind("Slope", "Angle", 20f, "Slope angle in degrees.");
            CfgSlopeAngleStep = Config.Bind("Slope", "AngleStep", 5f, "Degrees added/removed per key press.");
            CfgSlopeDirectionSnap = Config.Bind("Slope", "DirectionSnapDegrees", 45, "Snap the slope direction to multiples of this angle (0 = follow the camera exactly, 90 = only along the grid axes).");

            CfgSmoothStrength = Config.Bind("Smooth", "Strength", 0.5f, "How strongly one click pulls each corner towards the average of its neighbours (0.1 - 1).");

            KeyBigger = Config.Bind("Keys", "Bigger", Key.PageUp, "Increase size. Hold Ctrl: angle (slope) / strength (smooth). Hold Shift: slope width.");
            KeySmaller = Config.Bind("Keys", "Smaller", Key.PageDown, "Decrease size. Same modifiers as Bigger.");
            KeyCycleMode = Config.Bind("Keys", "CycleMode", Key.Home, "Cycle shovel mode: Brush -> Slope -> Smooth.");

            ShovelAccess.Init();
            new Harmony(Guid).PatchAll(typeof(Patches));
            preview = new BrushPreview();
            Log.LogInfo("ShovelPlus " + Version + " loaded.");
        }

        internal static bool ShovelInUse()
        {
            ShovelTool s = ActiveShovel;
            return s != null && s.isActiveAndEnabled && ShovelAccess.CanUseShovelInput(s);
        }

        private void Update()
        {
            if (!ShovelInUse())
            {
                preview.Hide();
                return;
            }
            HandleKeys();
            if (CfgShowPreview.Value) preview.Update(ActiveShovel); else preview.Hide();
        }

        private void HandleKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (Pressed(kb, KeyCycleMode.Value))
            {
                CfgMode.Value = (ShovelMode)(((int)CfgMode.Value + 1) % 3);
                Toast("Shovel mode: " + CfgMode.Value);
            }

            int dir = 0;
            if (Pressed(kb, KeyBigger.Value)) dir = 1;
            else if (Pressed(kb, KeySmaller.Value)) dir = -1;
            if (dir == 0) return;

            ShovelMode mode = CfgMode.Value;
            int max = Math.Max(1, CfgMaxRadius.Value);
            if (mode == ShovelMode.Slope)
            {
                if (ctrl)
                {
                    CfgSlopeAngle.Value = Mathf.Clamp(CfgSlopeAngle.Value + dir * CfgSlopeAngleStep.Value, 1f, 75f);
                    Toast("Slope angle: " + CfgSlopeAngle.Value.ToString("0") + "°");
                }
                else if (shift)
                {
                    CfgSlopeWidth.Value = Mathf.Clamp(CfgSlopeWidth.Value + dir, 1, max * 2);
                    Toast("Slope width: " + CfgSlopeWidth.Value);
                }
                else
                {
                    CfgSlopeLength.Value = Mathf.Clamp(CfgSlopeLength.Value + dir, 1, max * 2);
                    Toast("Slope length: " + CfgSlopeLength.Value);
                }
            }
            else if (mode == ShovelMode.Smooth && ctrl)
            {
                CfgSmoothStrength.Value = Mathf.Clamp(CfgSmoothStrength.Value + dir * 0.1f, 0.1f, 1f);
                Toast("Smooth strength: " + CfgSmoothStrength.Value.ToString("0.0"));
            }
            else
            {
                CfgBrushRadius.Value = Mathf.Clamp(CfgBrushRadius.Value + dir, 0, max);
                Toast("Brush radius: " + CfgBrushRadius.Value + (CfgBrushRadius.Value == 0 ? " (vanilla)" : ""));
            }
        }

        private static bool Pressed(Keyboard kb, Key key)
        {
            if (key == Key.None) return false;
            try { return kb[key].wasPressedThisFrame; }
            catch { return false; }
        }

        private void Toast(string msg)
        {
            toast = msg;
            toastUntil = Time.unscaledTime + 1.5f;
        }

        private GameHud hud;

        private void LateUpdate()
        {
            if (hud == null) hud = new GameHud();
            bool show = CfgShowHud.Value && ShovelInUse();
            hud.Show(show ? BuildHudText() : null);
        }

        // Info text, styled with the game's own rich text (same font, size and outline as its crosshair hints).
        private string BuildHudText()
        {
            ShovelMode mode = CfgMode.Value;
            string hint = "<size=75%><alpha=#CC>";
            string text;
            const string nl = "\n";
            if (mode == ShovelMode.Slope)
                text = "Shovel: Slope  -  " + CfgSlopeAngle.Value.ToString("0") + "°, length " + CfgSlopeLength.Value + ", width " + CfgSlopeWidth.Value +
                       nl + hint + "Left click: slope down    Right click: slope up" +
                       nl + "PgUp/PgDn length   Shift: width   Ctrl: angle   Home: mode</size><alpha=#FF>";
            else if (mode == ShovelMode.Smooth)
                text = "Shovel: Smooth  -  radius " + Math.Max(1, CfgBrushRadius.Value) + ", strength " + CfgSmoothStrength.Value.ToString("0.0") +
                       nl + hint + "Click to smooth" +
                       nl + "PgUp/PgDn radius   Ctrl: strength   Home: mode</size><alpha=#FF>";
            else
                text = "Shovel: Brush  -  radius " + CfgBrushRadius.Value + (CfgBrushRadius.Value == 0 ? " (normal)" : "") +
                       nl + hint + "PgUp/PgDn radius   Home: mode</size><alpha=#FF>";
            if (toast != null && Time.unscaledTime < toastUntil) text += nl + "<color=#FFD36B>" + toast + "</color>";
            return text;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Access to private ShovelTool members
    // ------------------------------------------------------------------------------------------
    internal static class ShovelAccess
    {
        internal delegate bool TryGetCellD(ShovelTool self, out int cellX, out int cellZ, out Vector3 actionPos);

        internal static Func<ShovelTool, bool> CanUseShovelInput;
        internal static TryGetCellD TryGetTargetTerrainCell;
        internal static Func<int, int, Vector3, int> GetClosestCornerIndex;
        internal static Func<ShovelTool, PropInstance[], int, int, bool> IsCornerBlockedByTree;
        internal static Func<ShovelTool, PropInstance[], int, int, bool> IsCellBlockedByTree;
        internal static Func<ShovelTool, PropInstance, bool> BlocksDigging;
        internal static Action<int, int, int, int, List<uint>> BuildQueryCellsRect;
        static System.Reflection.FieldInfo fYClamp, fTolerance, fCamera;
        internal static byte YClampHeight(ShovelTool s) { return (byte)fYClamp.GetValue(s); }
        internal static int PlanarHeightTolerance(ShovelTool s) { return (int)fTolerance.GetValue(s); }
        internal static Camera CameraField(ShovelTool s) { return (Camera)fCamera.GetValue(s); }

        internal static void Init()
        {
            Type t = typeof(ShovelTool);
            CanUseShovelInput = AccessTools.MethodDelegate<Func<ShovelTool, bool>>(AccessTools.PropertyGetter(t, "CanUseShovelInput"));
            TryGetTargetTerrainCell = AccessTools.MethodDelegate<TryGetCellD>(AccessTools.Method(t, "TryGetTargetTerrainCell"));
            GetClosestCornerIndex = AccessTools.MethodDelegate<Func<int, int, Vector3, int>>(AccessTools.Method(t, "GetClosestCornerIndex"));
            IsCornerBlockedByTree = AccessTools.MethodDelegate<Func<ShovelTool, PropInstance[], int, int, bool>>(AccessTools.Method(t, "IsCornerBlockedByTree"));
            IsCellBlockedByTree = AccessTools.MethodDelegate<Func<ShovelTool, PropInstance[], int, int, bool>>(AccessTools.Method(t, "IsCellBlockedByTree"));
            BlocksDigging = AccessTools.MethodDelegate<Func<ShovelTool, PropInstance, bool>>(AccessTools.Method(t, "BlocksDigging"));
            BuildQueryCellsRect = AccessTools.MethodDelegate<Action<int, int, int, int, List<uint>>>(
                AccessTools.Method(t, "BuildQueryCells", new Type[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(List<uint>) }));
            fYClamp = AccessTools.Field(t, "yClampHeight");
            fTolerance = AccessTools.Field(t, "planarHeightTolerance");
            fCamera = AccessTools.Field(t, "camera");
        }

        internal static Camera GetCamera(ShovelTool s)
        {
            Camera c = null;
            try { c = CameraField(s); } catch { }
            if (c == null) c = Camera.main;
            return c;
        }

        /// Returns the terrain corner the shovel is currently aiming at.
        internal static bool TryGetTargetCorner(ShovelTool s, out int cornerX, out int cornerZ)
        {
            cornerX = 0; cornerZ = 0;
            int cx, cz; Vector3 pos;
            if (!TryGetTargetTerrainCell(s, out cx, out cz, out pos)) return false;
            int idx = GetClosestCornerIndex(cx, cz, pos);
            Vector2Int off = CornerOffset(idx);
            cornerX = cx + off.x; cornerZ = cz + off.y;
            return true;
        }

        internal static Vector2Int CornerOffset(int idx)
        {
            switch (idx)
            {
                case 1: return new Vector2Int(1, 0);
                case 2: return new Vector2Int(1, 1);
                case 3: return new Vector2Int(0, 1);
                default: return new Vector2Int(0, 0);
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Terrain operations
    // ------------------------------------------------------------------------------------------
    internal static class TerrainOps
    {
        const float RawToWorld = 0.5f; // Heightfield.heightScale

        internal static bool IsValidHeightPoint(int x, int z)
        {
            return x >= -2048 && x <= 2047 && z >= -2048 && z <= 2047;
        }

        internal static Vector2 SnapDirection(Vector3 forward)
        {
            Vector2 d = new Vector2(forward.x, forward.z);
            if (d.sqrMagnitude < 1e-6f) d = new Vector2(0, 1);
            d.Normalize();
            int snap = Plugin.CfgSlopeDirectionSnap.Value;
            if (snap > 0)
            {
                float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                a = Mathf.Round(a / snap) * snap;
                d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
            }
            return d;
        }

        /// Extent (in cells) around the target that the action may touch.
        internal static int NeededExtent()
        {
            ShovelMode m = Plugin.CfgMode.Value;
            int r = Math.Max(0, Plugin.CfgBrushRadius.Value);
            if (m == ShovelMode.Slope)
                return Math.Max(Plugin.CfgSlopeLength.Value, (Plugin.CfgSlopeWidth.Value + 1) / 2) + 2;
            if (m == ShovelMode.Smooth) return Math.Max(1, r) + 2;
            return r + 1;
        }

        internal static bool IsVanilla(bool planarAction)
        {
            ShovelMode m = Plugin.CfgMode.Value;
            if (m == ShovelMode.Brush || (m == ShovelMode.Slope && planarAction))
                return Plugin.CfgBrushRadius.Value <= 0;
            return false;
        }

        // Circle of corners around (cx, cz)
        internal static List<Vector2Int> CircleCorners(int cx, int cz, int r)
        {
            List<Vector2Int> list = new List<Vector2Int>();
            float lim = (r + 0.3f) * (r + 0.3f);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dz * dz <= lim) list.Add(new Vector2Int(cx + dx, cz + dz));
            return list;
        }

        /// Main entry: apply the current mode at the given corner. Returns true if anything changed.
        internal static bool Apply(ShovelTool tool, Heightfield hf, int cornerX, int cornerZ, int delta, PropInstance[] instances, bool planarAction)
        {
            Dictionary<long, byte> changes = new Dictionary<long, byte>();
            ShovelMode mode = Plugin.CfgMode.Value;

            if (mode == ShovelMode.Slope && !planarAction)
                ComputeSlope(tool, hf, cornerX, cornerZ, delta, instances, changes);
            else if (mode == ShovelMode.Smooth)
                ComputeSmooth(tool, hf, cornerX, cornerZ, instances, changes);
            else
                ComputeBrush(tool, hf, cornerX, cornerZ, delta, instances, planarAction, changes);

            if (changes.Count == 0) return false;

            foreach (KeyValuePair<long, byte> kv in changes)
                hf.SetRawHeight(KeyX(kv.Key), KeyZ(kv.Key), kv.Value);

            MarkEffects(tool, hf, changes, instances);
            UprootSaplings(tool, changes, instances);
            return true;
        }

        static long Key(int x, int z) { return ((long)x << 32) | (uint)z; }
        static int KeyX(long k) { return (int)(k >> 32); }
        static int KeyZ(long k) { return (int)(uint)(k & 0xffffffffL); }

        static bool Editable(ShovelTool tool, PropInstance[] instances, int x, int z)
        {
            return IsValidHeightPoint(x, z) && !ShovelAccess.IsCornerBlockedByTree(tool, instances, x, z);
        }

        static void ComputeBrush(ShovelTool tool, Heightfield hf, int cx, int cz, int delta, PropInstance[] instances, bool planar, Dictionary<long, byte> changes)
        {
            int r = Math.Max(0, Plugin.CfgBrushRadius.Value);
            byte clamp = ShovelAccess.YClampHeight(tool);
            int tol = Math.Max(0, ShovelAccess.PlanarHeightTolerance(tool));
            foreach (Vector2Int p in CircleCorners(cx, cz, r))
            {
                if (!Editable(tool, instances, p.x, p.y)) continue;
                int h = hf.GetRawHeight(p.x, p.y);
                int target;
                if (planar)
                {
                    if (Math.Abs(h - clamp) > tol) continue;
                    bool moves = delta >= 0 ? h < clamp : h > clamp;
                    if (!moves) continue;
                    target = clamp;
                }
                else
                {
                    target = Mathf.Clamp(h + delta, 0, 255);
                }
                if (target != h) changes[Key(p.x, p.y)] = (byte)target;
            }
        }

        static void ComputeSlope(ShovelTool tool, Heightfield hf, int cx, int cz, int delta, PropInstance[] instances, Dictionary<long, byte> changes)
        {
            Vector2 d = Plugin.PendingSlopeDir;
            Vector2 perp = new Vector2(-d.y, d.x);
            int length = Math.Max(1, Plugin.CfgSlopeLength.Value);
            float halfW = Math.Max(1, Plugin.CfgSlopeWidth.Value) * 0.5f;
            float rawPerUnit = Mathf.Tan(Mathf.Clamp(Plugin.CfgSlopeAngle.Value, 1f, 75f) * Mathf.Deg2Rad) / RawToWorld;
            float sign = delta >= 0 ? 1f : -1f;
            int h0 = hf.GetRawHeight(cx, cz);

            int ext = length + (int)Mathf.Ceil(halfW) + 1;
            for (int dz = -ext; dz <= ext; dz++)
                for (int dx = -ext; dx <= ext; dx++)
                {
                    float t = dx * d.x + dz * d.y;      // distance along slope
                    float s = dx * perp.x + dz * perp.y; // sideways offset
                    if (t < -0.01f || t > length + 0.01f || Mathf.Abs(s) > halfW + 0.01f) continue;
                    int x = cx + dx, z = cz + dz;
                    if (!Editable(tool, instances, x, z)) continue;
                    int target = Mathf.Clamp(Mathf.RoundToInt(h0 + sign * Mathf.Max(0f, t) * rawPerUnit), 0, 255);
                    if (target != hf.GetRawHeight(x, z)) changes[Key(x, z)] = (byte)target;
                }
        }

        static void ComputeSmooth(ShovelTool tool, Heightfield hf, int cx, int cz, PropInstance[] instances, Dictionary<long, byte> changes)
        {
            int r = Math.Max(1, Plugin.CfgBrushRadius.Value);
            float strength = Mathf.Clamp(Plugin.CfgSmoothStrength.Value, 0.05f, 1f);

            // snapshot the heights first (SetRawHeight is deferred, but be explicit)
            int n = r + 1;
            int size = 2 * n + 1;
            int[] h = new int[size * size];
            for (int dz = -n; dz <= n; dz++)
                for (int dx = -n; dx <= n; dx++)
                    h[(dz + n) * size + (dx + n)] = hf.GetRawHeight(cx + dx, cz + dz);

            float lim = (r + 0.3f) * (r + 0.3f);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float dist2 = dx * dx + dz * dz;
                    if (dist2 > lim) continue;
                    int x = cx + dx, z = cz + dz;
                    if (!Editable(tool, instances, x, z)) continue;

                    // 3x3 weighted average (center 4, edges 2, diagonals 1)
                    float sum = 0, wsum = 0;
                    for (int oz = -1; oz <= 1; oz++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            float w = (ox == 0 && oz == 0) ? 4f : (ox == 0 || oz == 0) ? 2f : 1f;
                            sum += w * h[(dz + oz + n) * size + (dx + ox + n)];
                            wsum += w;
                        }
                    float avg = sum / wsum;
                    int cur = h[(dz + n) * size + (dx + n)];

                    // soften the effect towards the edge of the brush
                    float falloff = 1f - Mathf.Clamp01(Mathf.Sqrt(dist2) / (r + 1f));
                    float k = strength * Mathf.Lerp(0.5f, 1f, falloff);
                    float diff = (avg - cur) * k;
                    int step = Mathf.RoundToInt(diff);
                    if (step == 0 && Mathf.Abs(avg - cur) >= 0.75f) step = avg > cur ? 1 : -1; // avoid getting stuck on byte rounding
                    if (step == 0) continue;
                    changes[Key(x, z)] = (byte)Mathf.Clamp(cur + step, 0, 255);
                }
        }

        static void MarkEffects(ShovelTool tool, Heightfield hf, Dictionary<long, byte> changes, PropInstance[] instances)
        {
            HashSet<long> cells = new HashSet<long>();
            foreach (long k in changes.Keys)
            {
                int x = KeyX(k), z = KeyZ(k);
                for (int cx = x - 1; cx <= x; cx++)
                    for (int cz = z - 1; cz <= z; cz++)
                        cells.Add(Key(cx, cz));
            }
            foreach (long c in cells)
            {
                int x = KeyX(c), z = KeyZ(c);
                if (!ShovelAccess.IsCellBlockedByTree(tool, instances, x, z))
                    hf.SetEffectsTexturePixel(x, z, 1f, 1f);
            }
        }

        static void UprootSaplings(ShovelTool tool, Dictionary<long, byte> changes, PropInstance[] instances)
        {
            if (instances == null || instances.Length == 0 || World.isShuttingDown) return;
            PropChunkManager pcm = World.propChunkManager;
            if (pcm == null) return;
            HashSet<long> removed = new HashSet<long>();
            for (int i = 0; i < instances.Length; i++)
            {
                PropInstance inst = instances[i];
                if (ShovelAccess.BlocksDigging(tool, inst)) continue;
                if (inst.prop == null || !pcm.IsGrowingStage(inst.prop)) continue;
                int wx = inst.worldX, wz = inst.worldZ;
                bool hit = changes.ContainsKey(Key(wx, wz)) || changes.ContainsKey(Key(wx + 1, wz)) ||
                           changes.ContainsKey(Key(wx, wz + 1)) || changes.ContainsKey(Key(wx + 1, wz + 1));
                if (!hit || !removed.Add(Key(wx, wz))) continue;
                pcm.RemoveProp(new Unity.Mathematics.uint2((uint)wx, (uint)wz));
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Harmony patches
    // ------------------------------------------------------------------------------------------
    internal static class Patches
    {
        [HarmonyPatch(typeof(ShovelTool), "OnEnable")]
        [HarmonyPostfix]
        static void OnEnablePostfix(ShovelTool __instance)
        {
            Plugin.ActiveShovel = __instance;
        }

        [HarmonyPatch(typeof(ShovelTool), "OnDisable")]
        [HarmonyPostfix]
        static void OnDisablePostfix(ShovelTool __instance)
        {
            if (Plugin.ActiveShovel == __instance) Plugin.ActiveShovel = null;
        }

        // Remember slope direction and widen the prop/tree query to cover the whole affected area.
        [HarmonyPatch(typeof(ShovelTool), "QueueShovelAction")]
        [HarmonyPrefix]
        static void QueuePrefix(ShovelTool __instance, int cellX, int cellZ, bool planarAction)
        {
            if (TerrainOps.IsVanilla(planarAction)) { Plugin.QueryExtent = 0; return; }
            Plugin.QueryExtent = TerrainOps.NeededExtent();
            Plugin.QueryCellX = cellX;
            Plugin.QueryCellZ = cellZ;
            Camera cam = ShovelAccess.GetCamera(__instance);
            Plugin.PendingSlopeDir = TerrainOps.SnapDirection(cam != null ? cam.transform.forward : Vector3.forward);
        }

        [HarmonyPatch(typeof(ShovelTool), "QueueShovelAction")]
        [HarmonyFinalizer]
        static void QueueFinalizer()
        {
            Plugin.QueryExtent = 0;
        }

        [HarmonyPatch(typeof(PropChunkManager), "GetPropCollisionInstances",
            new Type[] { typeof(List<uint>), typeof(Action<PropInstance[]>), typeof(bool), typeof(Action), typeof(bool) })]
        [HarmonyPrefix]
        static void GetPropCollisionInstancesPrefix(List<uint> cells)
        {
            int e = Plugin.QueryExtent;
            if (e <= 1 || cells == null) return;
            Plugin.QueryExtent = 0; // only the shovel's own query
            int x = Plugin.QueryCellX, z = Plugin.QueryCellZ;
            ShovelAccess.BuildQueryCellsRect(x - e, x + e, z - e, z + e, cells);
        }

        // Replace the single-corner modification with the selected brush / slope / smooth operation.
        [HarmonyPatch(typeof(ShovelTool), "TryModifyCellCorners")]
        [HarmonyPrefix]
        static bool TryModifyPrefix(ShovelTool __instance, Heightfield heightfield, int cellX, int cellZ, int delta,
                                    int closestCornerIndex, PropInstance[] instances, bool planarAction, ref bool __result)
        {
            if (TerrainOps.IsVanilla(planarAction)) return true;
            try
            {
                Vector2Int off = ShovelAccess.CornerOffset(closestCornerIndex);
                __result = TerrainOps.Apply(__instance, heightfield, cellX + off.x, cellZ + off.y, delta, instances, planarAction);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ShovelPlus failed, falling back to vanilla shovel: " + ex);
                return true;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Outline preview drawn on the terrain
    // ------------------------------------------------------------------------------------------
    internal class BrushPreview
    {
        Mesh mesh;
        Material mat;
        bool matFailed;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> idx = new List<int>();

        public void Hide() { }

        bool EnsureResources()
        {
            if (mat != null) return true;
            if (matFailed) return false;
            string[] shaders = { "Hidden/Internal-Colored", "Sprites/Default", "Universal Render Pipeline/Unlit", "UI/Default" };
            foreach (string name in shaders)
            {
                Shader sh = Shader.Find(name);
                if (sh == null) continue;
                mat = new Material(sh);
                mat.hideFlags = HideFlags.HideAndDontSave;
                if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
                if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);
                Plugin.Log.LogInfo("Preview using shader " + name);
                break;
            }
            if (mat == null)
            {
                matFailed = true;
                Plugin.Log.LogWarning("No usable shader found for the brush preview; preview disabled.");
                return false;
            }
            mesh = new Mesh();
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.MarkDynamic();
            return true;
        }

        public void Update(ShovelTool tool)
        {
            ShovelMode mode = Plugin.CfgMode.Value;
            if (mode == ShovelMode.Brush && Plugin.CfgBrushRadius.Value <= 0) return; // vanilla has its own preview
            Heightfield hf = World.heightfield;
            if (hf == null || World.isShuttingDown) return;
            int cx, cz;
            if (!ShovelAccess.TryGetTargetCorner(tool, out cx, out cz)) return;
            if (!EnsureResources()) return;

            verts.Clear(); colors.Clear(); idx.Clear();
            Color main = new Color(1f, 0.85f, 0.3f, 0.9f);

            if (mode == ShovelMode.Slope)
            {
                Camera cam = ShovelAccess.GetCamera(tool);
                Vector2 d = TerrainOps.SnapDirection(cam != null ? cam.transform.forward : Vector3.forward);
                Vector2 perp = new Vector2(-d.y, d.x);
                float L = Math.Max(1, Plugin.CfgSlopeLength.Value);
                float hw = Math.Max(1, Plugin.CfgSlopeWidth.Value) * 0.5f;
                float y0 = Heightfield.RawHeightToWorldHeight(hf.GetRawHeight(cx, cz));
                float rise = Mathf.Tan(Plugin.CfgSlopeAngle.Value * Mathf.Deg2Rad) * L;
                Vector3 o = new Vector3(cx, 0, cz);
                Vector3 D = new Vector3(d.x, 0, d.y), P = new Vector3(perp.x, 0, perp.y);
                Color up = new Color(0.4f, 1f, 0.4f, 0.9f), down = new Color(1f, 0.45f, 0.35f, 0.9f);
                // up ramp (RMB) and down ramp (LMB)
                Ramp(o, D, P, L, hw, y0, y0 + rise, up);
                Ramp(o, D, P, L, hw, y0, y0 - rise, down);
                // footprint on the current ground
                Vector3 a = o - P * hw, b = o + P * hw, c = o + P * hw + D * L, e = o - P * hw + D * L;
                GroundLine(hf, a, b, main); GroundLine(hf, b, c, main); GroundLine(hf, c, e, main); GroundLine(hf, e, a, main);
            }
            else
            {
                float r = Math.Max(mode == ShovelMode.Smooth ? 1 : 0, Plugin.CfgBrushRadius.Value) + 0.3f;
                const int seg = 64;
                Vector3 c0 = new Vector3(cx, 0, cz);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    GroundLine(hf, c0 + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r),
                                   c0 + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r),
                               mode == ShovelMode.Smooth ? new Color(0.45f, 0.8f, 1f, 0.9f) : main, 1);
                }
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0);
        }

        void Ramp(Vector3 o, Vector3 D, Vector3 P, float L, float hw, float yStart, float yEnd, Color col)
        {
            Vector3 a = o - P * hw, b = o + P * hw, c = o + P * hw + D * L, e = o - P * hw + D * L;
            a.y = b.y = yStart + 0.05f; c.y = e.y = yEnd + 0.05f;
            Line(a, b, col); Line(b, c, col); Line(c, e, col); Line(e, a, col);
            Line((a + b) * 0.5f, (c + e) * 0.5f, col);
        }

        void GroundLine(Heightfield hf, Vector3 from, Vector3 to, Color col, int steps = 0)
        {
            if (steps <= 0) steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) * 2f));
            Vector3 prev = from; prev.y = hf.GetWorldHeight(prev.x, prev.z) + 0.08f;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
                p.y = hf.GetWorldHeight(p.x, p.z) + 0.08f;
                Line(prev, p, col);
                prev = p;
            }
        }

        void Line(Vector3 a, Vector3 b, Color c)
        {
            idx.Add(verts.Count); verts.Add(a); colors.Add(c);
            idx.Add(verts.Count); verts.Add(b); colors.Add(c);
        }
    }
}

namespace ShovelPlus
{
    /// A text label that looks exactly like the game's own crosshair hint text: it is a copy of that
    /// TextMeshPro object (same font, material, outline, colour, canvas), moved to the bottom-left corner.
    internal class GameHud
    {
        TextMeshProUGUI label;
        bool failed;
        float nextTry;

        bool Ensure()
        {
            if (label != null) return true;
            if (failed && Time.unscaledTime < nextTry) return false;
            failed = true; nextTry = Time.unscaledTime + 2f; // retry every 2s until the game UI exists
            try
            {
                Crosshair crosshair = World.crosshair;
                if (crosshair == null) return false;
                TMP_Text source = HarmonyLib.AccessTools.Field(typeof(Crosshair), "crosshairText").GetValue(crosshair) as TMP_Text;
                if (source == null || source.canvas == null) return false;
                Canvas canvas = source.canvas.rootCanvas;

                GameObject go = UnityEngine.Object.Instantiate(source.gameObject, canvas.transform, false);
                go.name = "ShovelPlusHud";
                // keep only the text itself: no game scripts that might overwrite or animate it
                foreach (Component c in go.GetComponents<Component>())
                    if (!(c is Transform) && !(c is TMP_Text) && !(c is CanvasRenderer)) UnityEngine.Object.Destroy(c);
                for (int i = go.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);

                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(28f, 120f);
                rt.sizeDelta = new Vector2(900f, 220f);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;

                label = go.GetComponent<TextMeshProUGUI>();
                if (label == null) { UnityEngine.Object.Destroy(go); return false; }
                label.alignment = TextAlignmentOptions.BottomLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.richText = true;
                label.raycastTarget = false;
                label.text = "";
                go.SetActive(false);
                failed = false;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not create the shovel info text: " + ex.Message);
                return false;
            }
        }

        public void Show(string text)
        {
            if (text == null)
            {
                if (label != null && label.gameObject.activeSelf) label.gameObject.SetActive(false);
                return;
            }
            if (!Ensure()) return;
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            if (label.text != text) label.text = text;
        }
    }
}
