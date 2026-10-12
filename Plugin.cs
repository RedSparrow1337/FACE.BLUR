using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Faceblur
{
    [BepInPlugin("com.faceblur.mod", "Faceblur", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        enum Cat { Pmc, Scav, Boss, Follower, RaiderRogue, Other }

        class Mask
        {
            public Cat Category;
            public GameObject Go;
            public Transform Head;
            public Material Mat;
            public Player Owner;
            public Vector3 LastCfg = new Vector3(float.NaN, 0, 0);
            public float LastDiameter = float.NaN;
            public float LastBlocks = float.NaN;
        }

        readonly Dictionary<Cat, ConfigEntry<bool>> _catToggle = new Dictionary<Cat, ConfigEntry<bool>>();
        readonly Dictionary<string, Mask> _masks = new Dictionary<string, Mask>();

        ConfigEntry<bool> _master;
        ConfigEntry<float> _radius;
        ConfigEntry<float> _blocks;
        ConfigEntry<float> _up;
        ConfigEntry<float> _forward;
        ConfigEntry<float> _right;
        ConfigEntry<float> _maxDist;

        Shader _shader;
        float _timer;

        void Awake()
        {
            _master = Config.Bind("General", "Enabled", true, "Master switch for the whole mod");

            _catToggle[Cat.Pmc] = Config.Bind("Targets", "AI PMC (USEC/BEAR)", true, "Pixelate faces of AI PMCs");
            _catToggle[Cat.Scav] = Config.Bind("Targets", "Scavs", true, "Pixelate faces of regular scavs");
            _catToggle[Cat.Boss] = Config.Bind("Targets", "Bosses", true, "Pixelate faces of bosses");
            _catToggle[Cat.Follower] = Config.Bind("Targets", "Boss followers", true, "Pixelate faces of boss guards/followers");
            _catToggle[Cat.RaiderRogue] = Config.Bind("Targets", "Raiders / Rogues", true, "Pixelate faces of Raiders and Rogues");
            _catToggle[Cat.Other] = Config.Bind("Targets", "Other (cultists, etc.)", true, "Pixelate everyone else who is not the player");

            _radius = Config.Bind("Look", "Mask half-size (m)", 0.168f, new ConfigDescription("Half of the square's side length", new AcceptableValueRange<float>(0.05f, 0.4f)));
            _blocks = Config.Bind("Look", "Blocks across head", 5f, new ConfigDescription("Fewer = coarser mosaic", new AcceptableValueRange<float>(2f, 20f)));
            _up = Config.Bind("Look", "Offset up (m)", 0.047f, new ConfigDescription("Move mask up from the head bone (character's own up axis)", new AcceptableValueRange<float>(-0.3f, 0.4f)));
            _forward = Config.Bind("Look", "Offset forward (m)", 0.03f, new ConfigDescription("Move mask in the direction the character faces", new AcceptableValueRange<float>(-0.3f, 0.4f)));
            _right = Config.Bind("Look", "Offset right (m)", 0f, new ConfigDescription("Move mask to the character's right", new AcceptableValueRange<float>(-0.3f, 0.3f)));

            _maxDist = Config.Bind("Performance", "Max distance (m)", 200f, new ConfigDescription("Masks farther than this are hidden (saves GPU). Raise it if you use a high-zoom scope", new AcceptableValueRange<float>(30f, 600f)));

            string dir = Path.GetDirectoryName(Info.Location);
            string path = Path.Combine(dir, "faceblur.bundle");
            if (!File.Exists(path))
            {
                Logger.LogError("faceblur.bundle not found next to the dll: " + path);
                enabled = false;
                return;
            }

            var bundle = AssetBundle.LoadFromFile(path);
            var shaders = bundle != null ? bundle.LoadAllAssets<Shader>() : null;
            if (shaders == null || shaders.Length == 0)
            {
                Logger.LogError("No shader found in faceblur.bundle");
                enabled = false;
                return;
            }
            _shader = shaders[0];
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 0.25f) return;
            _timer = 0f;

            var world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                _masks.Clear(); // objects are destroyed together with the raid
                return;
            }

            var players = world.RegisteredPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i] as Player;
                if (p == null || p.IsYourPlayer) continue;
                if (_masks.ContainsKey(p.ProfileId)) continue;
                TryCreate(p);
            }

            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            float maxSqr = _maxDist.Value * _maxDist.Value;

            List<string> gone = null;
            foreach (var kv in _masks)
            {
                var m = kv.Value;
                if (m.Go == null)
                {
                    if (gone == null) gone = new List<string>();
                    gone.Add(kv.Key);
                    continue;
                }

                // No hooks on death on purpose: the mask is a child of the head bone,
                // so it stays on the corpse/ragdoll until the body is despawned.
                bool on = _master.Value && _catToggle[m.Category].Value;
                if (on && cam != null && m.Head != null)
                    on = (m.Head.position - camPos).sqrMagnitude <= maxSqr;
                if (m.Go.activeSelf != on) m.Go.SetActive(on);
                if (on) Apply(m);
            }
            if (gone != null) foreach (var id in gone) _masks.Remove(id);
        }

        void TryCreate(Player p)
        {
            Transform head = null;
            try { head = p.PlayerBones.Head.Original; } catch { }
            if (head == null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FaceblurMask";
            DestroyImmediate(go.GetComponent<Collider>()); // must not collide / be hit
            go.transform.SetParent(head, false);

            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mat = new Material(_shader);
            r.sharedMaterial = mat;

            var m = new Mask { Category = Classify(p), Go = go, Head = head, Mat = mat, Owner = p };
            _masks[p.ProfileId] = m;
            Apply(m);
        }

        void Apply(Mask m)
        {
            // Only touch the transform / material when a setting actually changed
            float diameter = _radius.Value * 2f;
            if (diameter != m.LastDiameter)
            {
                float parentScale = Mathf.Max(0.0001f, m.Head.lossyScale.x);
                m.Go.transform.localScale = Vector3.one * (diameter / parentScale);
                m.Mat.SetFloat("_Diameter", diameter);
                m.LastDiameter = diameter;
            }
            float blocks = _blocks.Value;
            if (blocks != m.LastBlocks)
            {
                m.Mat.SetFloat("_BlocksAcross", blocks);
                m.LastBlocks = blocks;
            }

            // Offset is defined in the character's own axes (up/forward/right), converted once
            // into the head bone's local space, so it keeps following the head and the corpse.
            var cfg = new Vector3(_up.Value, _forward.Value, _right.Value);
            if (cfg != m.LastCfg && m.Owner != null)
            {
                var root = m.Owner.Transform.Original;
                Vector3 world = root.up * cfg.x + root.forward * cfg.y + root.right * cfg.z;
                m.Go.transform.localPosition = m.Head.InverseTransformVector(world);
                m.LastCfg = cfg;
            }
        }

        static Cat Classify(Player p)
        {
            var info = p.Profile != null ? p.Profile.Info : null;
            string role = (info != null && info.Settings != null) ? info.Settings.Role.ToString() : "";

            if (role.StartsWith("boss") || role == "sectantPriest") return Cat.Boss;
            if (role.StartsWith("follower")) return Cat.Follower;
            if (role == "pmcBEAR" || role == "pmcUSEC") return Cat.Pmc;
            if (role == "pmcBot" || role == "exUsec") return Cat.RaiderRogue;
            if (role == "assault" || role == "marksman" || role == "cursedAssault" || role == "assaultGroup") return Cat.Scav;

            if (info != null && (info.Side == EPlayerSide.Usec || info.Side == EPlayerSide.Bear)) return Cat.Pmc;
            if (info != null && info.Side == EPlayerSide.Savage) return Cat.Scav;
            return Cat.Other;
        }
    }
}
