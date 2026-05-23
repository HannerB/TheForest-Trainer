using BepInEx;
using BepInEx.Logging;
using TheForest.Utils;
using UnityEngine;

namespace TheForestTrainer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.hannerb.theforest.trainer";
        public const string PluginName = "TheForestTrainer";
        public const string PluginVersion = "0.3.0";

        internal static ManualLogSource Log;

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

        private Rect _windowRect = new Rect(20, 20, 320, 0);
        private GUIStyle _espStyle;

        private void Awake()
        {
            Log = Logger;
            Log.LogMessage(PluginName + " v" + PluginVersion + " cargado. F1 para abrir el panel.");
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

        private void OnGUI()
        {
            if (_espStyle == null)
            {
                _espStyle = new GUIStyle(GUI.skin.box);
                _espStyle.normal.textColor = Color.white;
                _espStyle.fontStyle = FontStyle.Bold;
                _espStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (_espEnabled) DrawEsp();

            if (!_panelVisible) return;
            _windowRect = GUILayout.Window(0xF0E5, _windowRect, DrawWindow,
                "TheForestTrainer v" + PluginVersion + "  (F1 para cerrar)");
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

                Vector3 mPos = m.transform.position;
                float distance = Vector3.Distance(playerPos, mPos);
                if (distance > _espMaxDistance) continue;

                Vector3 headPos = mPos + Vector3.up * 1.8f;
                Vector3 screenPos = cam.WorldToScreenPoint(headPos);
                if (screenPos.z < 0f) continue;

                GUI.color = distance < 25f ? new Color(1f, 0.2f, 0.2f, 0.95f)
                          : distance < 75f ? new Color(1f, 0.7f, 0.2f, 0.9f)
                                            : new Color(1f, 1f, 0.3f, 0.85f);

                string label = "MUTANT  " + distance.ToString("F0") + "m";
                Vector2 size = _espStyle.CalcSize(new GUIContent(label));
                float guiY = Screen.height - screenPos.y;
                Rect rect = new Rect(screenPos.x - size.x / 2f, guiY - size.y - 8f, size.x + 8f, size.y + 4f);

                GUI.Box(rect, label, _espStyle);
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
}
