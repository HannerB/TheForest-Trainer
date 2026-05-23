using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HutongGames.PlayMaker;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using TheForest.Utils;
using UnityEngine;

namespace TheForestTrainer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.hannerb.theforest.trainer";
        public const string PluginName = "TheForestTrainer";
        public const string PluginVersion = "0.4.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static bool HomingShouldRun
        {
            get
            {
                return Instance != null
                    && Instance._aimbotEnabled
                    && Instance._arrowHomeToHead
                    && Instance._currentTarget != null;
            }
        }

        internal static Vector3 GetCurrentHeadPos()
        {
            if (Instance == null || Instance._currentTarget == null) return Vector3.zero;
            return Instance.GetTargetHeadPosition(Instance._currentTarget);
        }

        internal static void IncrementRedirected()
        {
            if (Instance != null) Instance._arrowsRedirected++;
        }

        private bool _panelVisible;

        private bool _godMode;
        private bool _infiniteStamina;
        private bool _infiniteEnergy;
        private bool _noHunger;
        private bool _noThirst;
        private bool _noCold;

        private bool _espEnabled;
        private float _espMaxDistance = 200f;
        private mutantAI[] _cachedMutants;
        private float _lastMutantScan;

        private bool _aimbotEnabled;
        private float _aimbotMaxDistance = 200f;
        private bool _aimbotDebug = true;
        private mutantAI _currentTarget;
        private Vector3 _lockAimPoint;
        private bool _lockActive;
        private float _lastAngleDrift;

        private bool _freezeMutants;
        private readonly System.Collections.Generic.Dictionary<int, Vector3> _frozenPos
            = new System.Collections.Generic.Dictionary<int, Vector3>();
        private readonly System.Collections.Generic.Dictionary<int, Quaternion> _frozenRot
            = new System.Collections.Generic.Dictionary<int, Quaternion>();

        private bool _infiniteArrows;
        private float _lastAmmoTopUp;

        private bool _arrowHomeToHead = true;
        private int _arrowsRedirected;

        private const float AIM_FOV_DOT = 0.5f;
        private const float AIM_LEAD_SPEED = 50f;

        private readonly System.Collections.Generic.Dictionary<int, Transform> _boneCache
            = new System.Collections.Generic.Dictionary<int, Transform>();

        private readonly System.Collections.Generic.Dictionary<int, Renderer[]> _renderersCache
            = new System.Collections.Generic.Dictionary<int, Renderer[]>();
        private readonly System.Collections.Generic.Dictionary<int, EnemyHealth> _healthCache
            = new System.Collections.Generic.Dictionary<int, EnemyHealth>();
        private readonly System.Collections.Generic.Dictionary<int, Animator> _animatorCache
            = new System.Collections.Generic.Dictionary<int, Animator>();
        private readonly System.Collections.Generic.Dictionary<int, Transform> _headCache
            = new System.Collections.Generic.Dictionary<int, Transform>();
        private readonly System.Collections.Generic.Dictionary<int, Bounds> _lastBounds
            = new System.Collections.Generic.Dictionary<int, Bounds>();
        private readonly System.Collections.Generic.Dictionary<int, float> _lastBoundsTime
            = new System.Collections.Generic.Dictionary<int, float>();
        private float _lastCountersUpdate;

        private readonly System.Collections.Generic.Dictionary<int, Vector3> _lastPos
            = new System.Collections.Generic.Dictionary<int, Vector3>();
        private readonly System.Collections.Generic.Dictionary<int, float> _lastTime
            = new System.Collections.Generic.Dictionary<int, float>();
        private readonly System.Collections.Generic.Dictionary<int, Vector3> _vel
            = new System.Collections.Generic.Dictionary<int, Vector3>();

        private bool _arrowNoDrop = true;
        private float _lastArrowScan;
        private readonly System.Collections.Generic.HashSet<int> _modifiedArrows
            = new System.Collections.Generic.HashSet<int>();

        private Rect _windowRect = new Rect(20, 20, 320, 0);
        private GUIStyle _espStyle;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Log.LogMessage(PluginName + " v" + PluginVersion + " cargado. F1 para abrir el panel.");
            Camera.onPreCull += OnAnyCamPreCull;

            try
            {
                Harmony harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo("Harmony patches aplicados");
            }
            catch (System.Exception ex)
            {
                Log.LogError("Harmony patch error: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnAnyCamPreCull;
        }

        private void OnAnyCamPreCull(Camera cam)
        {
            if (!_lockActive) return;
            if (LocalPlayer.MainCam == null || cam != LocalPlayer.MainCam) return;
            ApplyCameraLock(cam);
        }

        private void ApplyCameraLock(Camera cam)
        {
            Vector3 dir = _lockAimPoint - cam.transform.position;
            if (dir.sqrMagnitude < 0.0001f) return;

            Quaternion camRot = Quaternion.LookRotation(dir);
            _lastAngleDrift = Quaternion.Angle(cam.transform.rotation, camRot);
            cam.transform.rotation = camRot;
        }

        private Vector3 GetTargetHeadPosition(mutantAI m)
        {
            Transform headBone = GetCachedHeadBone(m);
            if (headBone != null) return headBone.position;
            Bounds b;
            if (TryGetMutantBounds(m, out b))
                return b.center + Vector3.up * (b.extents.y * 0.85f);
            return m.transform.position + Vector3.up * 1.7f;
        }

        // Reset counter each frame (incremented by Harmony patch)
        private void ResetRedirectedCounter()
        {
            _arrowsRedirected = 0;
        }

        private void ApplyFreezeMutants()
        {
            mutantAI[] muts = GetMutants();
            for (int i = 0; i < muts.Length; i++)
            {
                mutantAI m = muts[i];
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                int id = m.GetInstanceID();

                if (!_frozenPos.ContainsKey(id))
                {
                    _frozenPos[id] = m.transform.position;
                    _frozenRot[id] = m.transform.rotation;

                    Animator a = m.GetComponentInChildren<Animator>();
                    if (a != null) a.speed = 0f;
                    UnityEngine.AI.NavMeshAgent agent = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (agent != null && agent.enabled) agent.isStopped = true;
                }

                m.transform.position = _frozenPos[id];
                m.transform.rotation = _frozenRot[id];

                Rigidbody rb = m.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        private void UnfreezeAllMutants()
        {
            mutantAI[] muts = GetMutants();
            for (int i = 0; i < muts.Length; i++)
            {
                mutantAI m = muts[i];
                if (m == null) continue;
                Animator a = m.GetComponentInChildren<Animator>();
                if (a != null) a.speed = 1f;
                UnityEngine.AI.NavMeshAgent agent = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.enabled) agent.isStopped = false;
            }
            _frozenPos.Clear();
            _frozenRot.Clear();
        }

        private void ApplyInfiniteArrows()
        {
            if (Time.time - _lastAmmoTopUp < 1f) return;
            _lastAmmoTopUp = Time.time;

            PlayerInventory inv = LocalPlayer.Inventory;
            if (inv == null) return;

            BowController[] bows = UnityEngine.Object.FindObjectsOfType<BowController>();
            for (int i = 0; i < bows.Length; i++)
            {
                BowController b = bows[i];
                if (b == null || b._ammoItemId <= 0) continue;
                inv.AddItem(b._ammoItemId, 5);
            }
        }

        private void ApplyArrowNoDrop()
        {
            if (Time.time - _lastArrowScan < 0.1f) return;
            _lastArrowScan = Time.time;

            ArrowDamage[] arrows = UnityEngine.Object.FindObjectsOfType<ArrowDamage>();
            foreach (ArrowDamage a in arrows)
            {
                if (a == null) continue;
                int id = a.GetInstanceID();
                if (_modifiedArrows.Contains(id)) continue;

                Rigidbody rb = a.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.useGravity = false;
                    _modifiedArrows.Add(id);
                }
            }
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1))
            {
                _panelVisible = !_panelVisible;
            }

            PlayerStats stats = LocalPlayer.Stats;
            if (stats == null) return;

            if (_godMode)
            {
                stats.Health = 100f;
                stats.HealthTarget = 100f;
            }
            if (_infiniteStamina) stats.Stamina = 100f;
            if (_infiniteEnergy) stats.Energy = 100f;
            if (_noHunger) stats.Fullness = 1f;
            if (_noThirst) stats.Thirst = 0f;
            if (_noCold) stats.ColdArmor = 1f;

            if (_arrowNoDrop) ApplyArrowNoDrop();
            if (_infiniteArrows) ApplyInfiniteArrows();
            ResetRedirectedCounter();

            if (_freezeMutants) ApplyFreezeMutants();
            else if (_frozenPos.Count > 0) UnfreezeAllMutants();
        }

        private mutantAI[] GetMutants()
        {
            if (_cachedMutants == null || Time.time - _lastMutantScan > 0.5f)
            {
                _cachedMutants = UnityEngine.Object.FindObjectsOfType<mutantAI>();
                _lastMutantScan = Time.time;
            }
            return _cachedMutants;
        }

        private Transform GetAimBone(mutantAI m)
        {
            int id = m.GetInstanceID();
            Transform cached;
            if (_boneCache.TryGetValue(id, out cached) && cached != null)
            {
                return cached;
            }

            Animator anim = m.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                Transform t = anim.GetBoneTransform(HumanBodyBones.Chest);
                if (t == null) t = anim.GetBoneTransform(HumanBodyBones.UpperChest);
                if (t == null) t = anim.GetBoneTransform(HumanBodyBones.Spine);
                if (t == null) t = anim.GetBoneTransform(HumanBodyBones.Head);
                if (t != null)
                {
                    _boneCache[id] = t;
                    return t;
                }
            }

            Transform[] all = m.GetComponentsInChildren<Transform>();
            foreach (Transform t in all)
            {
                if (t == null) continue;
                string n = t.name.ToLower();
                if (n.Contains("chest") || n.Contains("spine") || n.Contains("torso"))
                {
                    _boneCache[id] = t;
                    return t;
                }
            }

            return null;
        }


        private bool TryGetMutantBounds(mutantAI m, out Bounds bounds)
        {
            int id = m.GetInstanceID();
            float t;
            if (_lastBoundsTime.TryGetValue(id, out t) && Time.time - t < 0.05f)
            {
                bounds = _lastBounds[id];
                return true;
            }

            Renderer[] rs;
            if (!_renderersCache.TryGetValue(id, out rs) || rs == null)
            {
                rs = m.GetComponentsInChildren<Renderer>();
                _renderersCache[id] = rs;
            }

            if (rs == null || rs.Length == 0)
            {
                bounds = default(Bounds);
                return false;
            }

            Bounds b = default(Bounds);
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled) continue;
                if (!any) { b = rs[i].bounds; any = true; }
                else { b.Encapsulate(rs[i].bounds); }
            }

            if (!any) { bounds = default(Bounds); return false; }
            _lastBounds[id] = b;
            _lastBoundsTime[id] = Time.time;
            bounds = b;
            return true;
        }

        private Animator GetCachedAnimator(mutantAI m)
        {
            int id = m.GetInstanceID();
            Animator anim;
            if (_animatorCache.TryGetValue(id, out anim) && anim != null) return anim;
            anim = m.GetComponentInChildren<Animator>();
            _animatorCache[id] = anim;
            return anim;
        }

        private Transform GetCachedHeadBone(mutantAI m)
        {
            int id = m.GetInstanceID();
            Transform head;
            if (_headCache.TryGetValue(id, out head) && head != null) return head;
            Animator anim = GetCachedAnimator(m);
            if (anim != null && anim.isHuman)
            {
                head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    _headCache[id] = head;
                    return head;
                }
            }
            return null;
        }

        private EnemyHealth GetHealth(mutantAI m)
        {
            int id = m.GetInstanceID();
            EnemyHealth eh;
            if (_healthCache.TryGetValue(id, out eh)) return eh;
            eh = m.GetComponent<EnemyHealth>();
            if (eh == null) eh = m.GetComponentInChildren<EnemyHealth>();
            _healthCache[id] = eh;
            return eh;
        }

        private bool IsMutantAlive(mutantAI m)
        {
            EnemyHealth eh = GetHealth(m);
            if (eh == null) return true;
            return eh.Health > 0;
        }

        private bool TryGetScreenRect(Camera cam, Bounds worldBounds, out Rect rect)
        {
            Vector3 c = worldBounds.center;
            Vector3 e = worldBounds.extents;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            bool any = false;

            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = c + new Vector3(
                    ((k & 1) == 0 ? -e.x : e.x),
                    ((k & 2) == 0 ? -e.y : e.y),
                    ((k & 4) == 0 ? -e.z : e.z));
                Vector3 sp = cam.WorldToScreenPoint(corner);
                if (sp.z < 0f) continue;
                any = true;
                if (sp.x < minX) minX = sp.x;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.y > maxY) maxY = sp.y;
            }

            if (!any || (maxX - minX) < 4f || (maxY - minY) < 4f)
            {
                rect = default(Rect);
                return false;
            }

            float topY = Screen.height - maxY;
            float botY = Screen.height - minY;
            rect = new Rect(minX, topY, maxX - minX, botY - topY);
            return true;
        }

        private void DrawBoxOutline(Rect r, float thickness, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void UpdateMutantVelocity(mutantAI m)
        {
            int id = m.GetInstanceID();
            Vector3 curPos = m.transform.position;
            float curT = Time.time;

            if (_lastTime.TryGetValue(id, out float lastT))
            {
                float dt = curT - lastT;
                if (dt > 0.01f && dt < 0.5f)
                {
                    Vector3 v = (curPos - _lastPos[id]) / dt;
                    if (_vel.TryGetValue(id, out Vector3 oldV))
                    {
                        v = Vector3.Lerp(oldV, v, 0.4f);
                    }
                    _vel[id] = v;
                }
            }
            _lastPos[id] = curPos;
            _lastTime[id] = curT;
        }

        private Vector3 GetAimPointForTarget(mutantAI m)
        {
            Transform bone = GetAimBone(m);
            if (bone != null) return bone.position;
            Bounds b;
            if (TryGetMutantBounds(m, out b)) return b.center;
            return m.transform.position + Vector3.up * 1.0f;
        }

        private mutantAI FindBestAimTarget()
        {
            Camera cam = LocalPlayer.MainCam;
            Transform playerTr = LocalPlayer.Transform;
            if (cam == null || playerTr == null) return null;

            Vector3 playerPos = playerTr.position;
            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;

            mutantAI[] mutants = GetMutants();
            mutantAI best = null;
            float bestDist = float.MaxValue;

            foreach (mutantAI m in mutants)
            {
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                if (!IsMutantAlive(m)) continue;
                UpdateMutantVelocity(m);

                float worldDist = Vector3.Distance(playerPos, m.transform.position);
                if (worldDist > _aimbotMaxDistance) continue;

                Vector3 aimPt = GetAimPointForTarget(m);
                Vector3 toTarget = (aimPt - camPos).normalized;
                if (Vector3.Dot(camFwd, toTarget) < AIM_FOV_DOT) continue;

                if (worldDist < bestDist)
                {
                    bestDist = worldDist;
                    best = m;
                }
            }

            return best;
        }

        private void LateUpdate()
        {
            _lockActive = false;

            if (!_aimbotEnabled || !UnityEngine.Input.GetMouseButton(1))
            {
                _currentTarget = null;
                return;
            }

            Camera cam = LocalPlayer.MainCam;
            if (cam == null) return;

            _currentTarget = FindBestAimTarget();
            if (_currentTarget == null) return;

            Vector3 aimBase = GetAimPointForTarget(_currentTarget);

            Vector3 v;
            if (_vel.TryGetValue(_currentTarget.GetInstanceID(), out v) && v.sqrMagnitude > 0.01f)
            {
                float dist = Vector3.Distance(cam.transform.position, aimBase);
                float t = dist / AIM_LEAD_SPEED;
                aimBase += v * t;
            }

            _lockAimPoint = aimBase;
            _lockActive = true;

            ApplyCameraLock(cam);
        }

        private void OnGUI()
        {
            if (_espStyle == null)
            {
                _espStyle = new GUIStyle(GUI.skin.box);
                _espStyle.normal.textColor = Color.white;
                _espStyle.fontStyle = FontStyle.Bold;
                _espStyle.alignment = TextAnchor.MiddleCenter;
            }

            bool isRepaint = Event.current.type == EventType.Repaint;

            if (isRepaint && _aimbotEnabled && _aimbotDebug
                && Time.unscaledTime - _lastCountersUpdate > 0.2f)
            {
                _lastCountersUpdate = Time.unscaledTime;
                UpdateCounters();
            }

            if (isRepaint)
            {
                if (_espEnabled) DrawEsp();
                if (_aimbotEnabled && _aimbotDebug) DrawAimbotDebug();
            }

            if (!_panelVisible) return;
            _windowRect = GUILayout.Window(0xF0E5, _windowRect, DrawWindow,
                "TheForestTrainer v" + PluginVersion + "  (F1 para cerrar)");
        }

        private int _aliveCount;
        private int _modifiedArrowsCount;

        private void UpdateCounters()
        {
            int alive = 0;
            mutantAI[] muts = GetMutants();
            for (int i = 0; i < muts.Length; i++)
            {
                mutantAI m = muts[i];
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                if (!IsMutantAlive(m)) continue;
                alive++;
            }
            _aliveCount = alive;
            _modifiedArrowsCount = _modifiedArrows.Count;
        }

        private void DrawAimbotDebug()
        {
            Rect bg = new Rect(10f, 10f, 360f, 240f);
            Color oldC = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float y = bg.y + 4f;
            float lh = 18f;
            float x = bg.x + 8f;
            float w = bg.width - 16f;

            GUI.Label(new Rect(x, y, w, lh), "==== SYSTEMS STATUS ===="); y += lh;

            GUI.Label(new Rect(x, y, w, lh),
                "[ESP]    " + (_espEnabled ? "ON" : "off") + "  mutantes vivos: " + _aliveCount); y += lh;

            string lockState = _lockActive ? "LOCK ACTIVE" : "idle (RMB)";
            GUI.Label(new Rect(x, y, w, lh),
                "[Aimbot] " + (_aimbotEnabled ? lockState : "off")); y += lh;

            if (_currentTarget != null)
            {
                Transform bone = GetAimBone(_currentTarget);
                EnemyHealth eh = GetHealth(_currentTarget);
                float d = Vector3.Distance(
                    LocalPlayer.Transform != null ? LocalPlayer.Transform.position : Vector3.zero,
                    _currentTarget.transform.position);

                GUI.Label(new Rect(x, y, w, lh),
                    "  Target: " + _currentTarget.name); y += lh;
                GUI.Label(new Rect(x, y, w, lh),
                    "  Hueso: " + (bone != null ? bone.name : "bounds.center")); y += lh;
                GUI.Label(new Rect(x, y, w, lh),
                    "  Dist: " + d.ToString("F1") + " m   HP: " +
                    (eh != null ? (eh.Health + "/" + eh.maxHealth) : "n/a")); y += lh;
                GUI.Label(new Rect(x, y, w, lh),
                    "  Angle drift: " + _lastAngleDrift.ToString("F2") + " grados"); y += lh;
            }
            else
            {
                GUI.Label(new Rect(x, y, w, lh), "  (sin target)"); y += lh * 4;
            }

            GUI.Label(new Rect(x, y, w, lh),
                "[NoDrop]  " + (_arrowNoDrop ? "ON" : "off") +
                "  flechas modificadas: " + _modifiedArrowsCount); y += lh;
            GUI.Label(new Rect(x, y, w, lh),
                "[Freeze]  " + (_freezeMutants ? "ON  congelados: " + _frozenPos.Count : "off")); y += lh;
            GUI.Label(new Rect(x, y, w, lh),
                "[InfAmmo] " + (_infiniteArrows ? "ON" : "off")); y += lh;
            GUI.Label(new Rect(x, y, w, lh),
                "[ArrowHome] " + (_arrowHomeToHead ? "ON" : "off") + "  redirigidas/frame: " + _arrowsRedirected); y += lh;

            GUI.color = oldC;
        }

        private void DrawEsp()
        {
            Camera cam = LocalPlayer.MainCam;
            Transform playerTr = LocalPlayer.Transform;
            if (cam == null || playerTr == null) return;

            Vector3 playerPos = playerTr.position;
            mutantAI[] mutants = GetMutants();

            Color oldColor = GUI.color;

            foreach (mutantAI m in mutants)
            {
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                if (!IsMutantAlive(m)) continue;

                float distance = Vector3.Distance(playerPos, m.transform.position);
                if (distance > _espMaxDistance) continue;

                Bounds b;
                if (!TryGetMutantBounds(m, out b)) continue;

                Rect box;
                if (!TryGetScreenRect(cam, b, out box)) continue;

                Color color;
                if (m == _currentTarget)
                {
                    color = new Color(0.2f, 1f, 1f, 1f);
                }
                else
                {
                    color = distance < 25f ? new Color(1f, 0.2f, 0.2f, 0.95f)
                          : distance < 75f ? new Color(1f, 0.7f, 0.2f, 0.9f)
                                            : new Color(1f, 1f, 0.3f, 0.85f);
                }

                DrawBoxOutline(box, 2f, color);

                string label = "MUTANT";
                Vector2 size = _espStyle.CalcSize(new GUIContent(label));
                Rect labelRect = new Rect(
                    box.x + (box.width - size.x) / 2f - 3f,
                    box.y - size.y - 4f,
                    size.x + 6f, size.y + 2f);
                GUI.color = color;
                GUI.Box(labelRect, label, _espStyle);
            }

            GUI.color = oldColor;
        }

        private void DrawWindow(int id)
        {
            PlayerStats stats = LocalPlayer.Stats;

            if (stats == null)
            {
                GUILayout.Label("Esperando jugador.");
                GUILayout.Label("Entra a una partida para ver stats.");
                GUI.DragWindow();
                return;
            }

            GUILayout.Label("--- Lectura en vivo ---");
            GUILayout.Label("Health:   " + stats.Health.ToString("F0") + " / 100");
            GUILayout.Label("Stamina:  " + stats.Stamina.ToString("F0") + " / 100");
            GUILayout.Label("Energy:   " + stats.Energy.ToString("F0") + " / 100");
            GUILayout.Label("Fullness: " + stats.Fullness.ToString("F2") + " / 1.00");
            GUILayout.Label("Thirst:   " + stats.Thirst.ToString("F2") + " / 1.00");

            GUILayout.Space(8);
            GUILayout.Label("--- Cheats ---");
            _godMode         = GUILayout.Toggle(_godMode,         " God Mode (vida infinita)");
            _infiniteStamina = GUILayout.Toggle(_infiniteStamina, " Stamina infinita");
            _infiniteEnergy  = GUILayout.Toggle(_infiniteEnergy,  " Energy infinita");
            _noHunger        = GUILayout.Toggle(_noHunger,        " Sin hambre");
            _noThirst        = GUILayout.Toggle(_noThirst,        " Sin sed");
            _noCold          = GUILayout.Toggle(_noCold,          " Sin frio");

            GUILayout.Space(8);
            GUILayout.Label("--- ESP / Marcadores ---");
            _espEnabled = GUILayout.Toggle(_espEnabled, " ESP activado (marcadores de mutantes)");

            int count = _cachedMutants != null ? _cachedMutants.Length : 0;
            GUILayout.Label("Mutantes en escena: " + count);

            GUILayout.Label("Distancia maxima: " + _espMaxDistance.ToString("F0") + " m");
            _espMaxDistance = GUILayout.HorizontalSlider(_espMaxDistance, 50f, 500f);

            GUILayout.Space(8);
            GUILayout.Label("--- Aimbot (mantener click derecho) ---");
            _aimbotEnabled = GUILayout.Toggle(_aimbotEnabled, " Aimbot activado");
            GUILayout.Label("Distancia maxima: " + _aimbotMaxDistance.ToString("F0") + " m");
            _aimbotMaxDistance = GUILayout.HorizontalSlider(_aimbotMaxDistance, 20f, 300f);
            _aimbotDebug = GUILayout.Toggle(_aimbotDebug, " Debug HUD (esquina superior)");

            GUILayout.Space(8);
            GUILayout.Label("--- Modificadores ---");
            _arrowNoDrop = GUILayout.Toggle(_arrowNoDrop, " Flechas sin caida");
            _arrowHomeToHead = GUILayout.Toggle(_arrowHomeToHead, " Flechas siguen a la cabeza (con target lockeado)");
            _infiniteArrows = GUILayout.Toggle(_infiniteArrows, " Flechas infinitas");
            _freezeMutants = GUILayout.Toggle(_freezeMutants, " Congelar mutantes (pin de posicion)");

            GUILayout.Space(8);

            if (GUILayout.Button("Recargar todo una vez"))
            {
                stats.Health = 100f;
                stats.HealthTarget = 100f;
                stats.Stamina = 100f;
                stats.Energy = 100f;
                stats.Fullness = 1f;
                stats.Thirst = 0f;
                Log.LogInfo("Stats recargados manualmente");
            }

            GUI.DragWindow();
        }
    }

    [HarmonyPatch(typeof(arrowTrajectory), "Update")]
    internal static class ArrowTrajectoryUpdatePatch
    {
        static void Postfix(arrowTrajectory __instance)
        {
            if (!Plugin.HomingShouldRun) return;
            if (__instance == null) return;

            Rigidbody rb = __instance.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;
            if (rb.velocity.sqrMagnitude < 1f) return;

            Vector3 headPos = Plugin.GetCurrentHeadPos();
            if (headPos == Vector3.zero) return;

            Vector3 dir = headPos - rb.position;
            float dist = dir.magnitude;
            if (dist < 0.01f) return;
            dir /= dist;

            float speed = Mathf.Max(rb.velocity.magnitude, 80f);
            rb.velocity = dir * speed;

            Plugin.IncrementRedirected();
        }
    }
}
